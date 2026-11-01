using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class TempoNudgesTests
{
    /// <summary>
    /// The nudges, a tempo that is typed and the edit queue as the window has them: a project tempo that commits change, an edit lock that
    /// is handed out in the order it was asked for, and commits that take as long as the test says. A commit waits in the queue,
    /// then (when it has the lock) waits for the test to finish it, then writes the tempo and publishes it, as CommitAsync does.
    /// </summary>
    private sealed class Rig
    {
        private readonly SemaphoreSlim _edits = new(1);
        private readonly List<TaskCompletionSource> _gates = [];
        private readonly List<(int Count, TaskCompletionSource Source)> _waiters = [];
        private int _finished;
        private readonly object _lock = new();
        internal readonly List<double> Written = [];
        internal readonly List<Exception> Reported = [];
        internal readonly TempoNudges Nudges;
        internal double Tempo = 120;
        internal int Ended;

        internal Rig() => Nudges = new TempoNudges(Commit, Guard, () => Ended++);

        /// <summary>How many commits have the edit lock so far (each one is waiting to be finished, or has been).</summary>
        internal int Started { get { lock (_lock) return _gates.Count; } }

        /// <summary>One commit of a tempo, through the real publication transaction and a first-in, first-out lock.</summary>
        internal Task Commit(double bpm) => DocumentPublication.RunAsync(_edits, async published =>
        {
            await Gate();
            Tempo = bpm;
            Written.Add(bpm);
            published();
        }, _ => { });

        private Task Gate()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                _gates.Add(gate);
                foreach (var waiter in _waiters.Where(waiter => _gates.Count >= waiter.Count).ToList())
                {
                    _waiters.Remove(waiter);
                    waiter.Source.TrySetResult();
                }
            }
            return gate.Task;
        }

        /// <summary>Completes once this many commits have the lock. A test that never gets there fails after ten seconds, it does not hang.</summary>
        internal Task StartedAsync(int count)
        {
            lock (_lock)
            {
                if (_gates.Count >= count) return Task.CompletedTask;
                var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, source));
                return source.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }

        /// <summary>The commit that has had the lock longest and is still waiting is done.</summary>
        internal void Finish()
        {
            lock (_lock) _gates[_finished++].SetResult();
        }

        /// <summary>The same commit fails (the project cannot be validated, or the recovery file cannot be written).</summary>
        internal void Fail(Exception exception)
        {
            lock (_lock) _gates[_finished++].SetException(exception);
        }

        /// <summary>What the window's error handling does: shows the error and lets nothing escape the loop.</summary>
        private async Task Guard(Func<Task> action)
        {
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { lock (Reported) Reported.Add(exception); }
        }

        /// <summary>Done in the Tempo panel with a tempo typed: it waits for the nudges that are on their way, then commits what was typed.</summary>
        internal async Task DoneAsync(double typed)
        {
            await Nudges.Settled;
            await Commit(typed);
        }
    }

    [TestMethod]
    public async Task NothingIsOnItsWayBeforeTheFirstRequest()
    {
        var rig = new Rig();

        Assert.IsNull(rig.Nudges.Requested);
        Assert.IsTrue(rig.Nudges.Settled.IsCompleted);
        await rig.Nudges.Settled;
        Assert.AreEqual(0, rig.Started);
    }

    [TestMethod]
    public async Task ARequestIsShownBeforeAnythingIsCommittedAndThenCommitted()
    {
        var rig = new Rig();
        double? shownRequested = null;
        var startedWhenShown = -1;

        rig.Nudges.Request(121, () =>
        {
            shownRequested = rig.Nudges.Requested;
            startedWhenShown = rig.Started;
        });

        Assert.AreEqual(121.0, shownRequested, "the dial can show what was asked for at once");
        Assert.AreEqual(0, startedWhenShown, "and it does so before anything is committed");
        await rig.StartedAsync(1);
        Assert.AreEqual(121.0, rig.Nudges.Requested, "it stays until it is the project's");
        Assert.IsFalse(rig.Nudges.Settled.IsCompleted);
        rig.Finish();
        await rig.Nudges.Settled;

        CollectionAssert.AreEqual(new[] { 121.0 }, rig.Written);
        Assert.AreEqual(121.0, rig.Tempo);
        Assert.IsNull(rig.Nudges.Requested, "nothing is on its way any more, so the project's tempo is shown");
        Assert.AreEqual(1, rig.Ended);
    }

    [TestMethod]
    public async Task RequestsMadeWhileACommitRunsAreReducedToTheNewestOneAndNeverQueueABacklog()
    {
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);

        // A stepper that is held asks every 80 milliseconds, far faster than a commit finishes.
        rig.Nudges.Request(122);
        rig.Nudges.Request(123);
        rig.Nudges.Request(124);

        Assert.AreEqual(124.0, rig.Nudges.Requested);
        Assert.AreEqual(1, rig.Started, "the others wait outside the edit queue: they are not commits yet");
        rig.Finish();
        await rig.StartedAsync(2);
        Assert.AreEqual(124.0, rig.Nudges.Requested, "the newest is still on its way while it is committed");
        Assert.IsFalse(rig.Nudges.Settled.IsCompleted);
        rig.Finish();
        await rig.Nudges.Settled;

        CollectionAssert.AreEqual(new[] { 121.0, 124.0 }, rig.Written, "the one that ran, then the newest; 122 and 123 were never committed");
        Assert.AreEqual(124.0, rig.Tempo);
        Assert.IsNull(rig.Nudges.Requested);
        Assert.AreEqual(1, rig.Ended, "one loop for the whole burst");
    }

    [TestMethod]
    public async Task ARequestThatTakesTheTempoBackToWhatWasCommittedIsStillCommittedLast()
    {
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);
        rig.Nudges.Request(121);
        rig.Nudges.Request(120);
        rig.Finish();
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.Nudges.Settled;

        CollectionAssert.AreEqual(new[] { 121.0, 120.0 }, rig.Written);
        Assert.AreEqual(120.0, rig.Tempo);
    }

    [TestMethod]
    public async Task ARequestThatNothingHasChangedSinceTheLastCommitIsNotCommittedAgain()
    {
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);
        rig.Nudges.Request(121);
        rig.Finish();
        await rig.Nudges.Settled;

        CollectionAssert.AreEqual(new[] { 121.0 }, rig.Written, "the newest wanted value is the one that is being committed already");
        Assert.AreEqual(1, rig.Started);
    }

    [TestMethod]
    public async Task ATempoTypedWhileNudgesAreOnTheirWayIsCommittedAfterThemAndIsTheLastWord()
    {
        // The three nudges of the first-use check, and a Done pressed while the first one is still being committed.
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);
        rig.Nudges.Request(123);

        var done = rig.DoneAsync(90);
        Assert.IsFalse(done.IsCompleted);
        Assert.AreEqual(1, rig.Started, "Done has not committed anything: it waits for the nudges");
        rig.Finish();
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.StartedAsync(3);
        rig.Finish();
        await done;
        await rig.Nudges.Settled;

        CollectionAssert.AreEqual(new[] { 121.0, 123.0, 90.0 }, rig.Written, "the nudges first, the typed tempo last");
        Assert.AreEqual(90.0, rig.Tempo, "the project holds what was typed");
        Assert.IsNull(rig.Nudges.Requested, "so nothing newer is on its way for the dial to show");
    }

    [TestMethod]
    public async Task WithoutWaitingTheTypedTempoIsCommittedBetweenTheLoopsCommitsAndLost()
    {
        // What Done did before it waited: its commit is queued behind the one that is running, and the loop's next commit (its newest,
        // older request) is queued behind that. Kept so that the test above cannot pass because this rig has no race.
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);
        rig.Nudges.Request(123);

        var done = rig.Commit(90);
        rig.Finish();
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.StartedAsync(3);
        rig.Finish();
        await done;
        await rig.Nudges.Settled;

        CollectionAssert.AreEqual(new[] { 121.0, 90.0, 123.0 }, rig.Written);
        Assert.AreEqual(123.0, rig.Tempo, "the typed tempo was overwritten by a nudge that was asked for before it");
    }

    [TestMethod]
    public async Task ATempoTypedWhenNothingIsOnItsWayIsCommittedAtOnce()
    {
        var rig = new Rig();

        var done = rig.DoneAsync(90);

        Assert.AreEqual(1, rig.Started, "no waiting: the loop is not running");
        rig.Finish();
        await done;
        CollectionAssert.AreEqual(new[] { 90.0 }, rig.Written);
    }

    [TestMethod]
    public async Task ANudgeMadeAfterTheTypedTempoWasQueuedIsCommittedAfterItAndWins()
    {
        var rig = new Rig();
        var done = rig.DoneAsync(90);
        Assert.AreEqual(1, rig.Started);

        rig.Nudges.Request(91);
        rig.Finish();
        await rig.StartedAsync(2);
        rig.Finish();
        await done;
        await rig.Nudges.Settled;

        CollectionAssert.AreEqual(new[] { 90.0, 91.0 }, rig.Written, "the later request is the later commit");
        Assert.AreEqual(91.0, rig.Tempo);
    }

    [TestMethod]
    public async Task SettledOfALoopThatEndedStaysCompleteWhenAnotherLoopStarts()
    {
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        var first = rig.Nudges.Settled;
        rig.Finish();
        await first;

        rig.Nudges.Request(130);
        await rig.StartedAsync(2);

        Assert.IsTrue(first.IsCompleted, "what waited for the first loop is not held up by the second");
        Assert.IsFalse(rig.Nudges.Settled.IsCompleted, "what waits now waits for the loop that is running");
        rig.Finish();
        await rig.Nudges.Settled;
        CollectionAssert.AreEqual(new[] { 121.0, 130.0 }, rig.Written);
        Assert.AreEqual(2, rig.Ended);
    }

    [TestMethod]
    public async Task ACommitThatFailsIsReportedAndLeavesNothingOnItsWayAndAnotherLoopCanStart()
    {
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(125);
        var failure = new IOException("The recovery file cannot be written.");

        rig.Fail(failure);
        await rig.Nudges.Settled;

        Assert.AreEqual(1, rig.Reported.Count);
        Assert.AreSame(failure, rig.Reported[0]);
        Assert.IsNull(rig.Nudges.Requested, "what was asked for after the failed commit is dropped with the loop");
        Assert.AreEqual(1, rig.Ended);
        Assert.AreEqual(1, rig.Started, "nothing else was committed");
        rig.Nudges.Request(130);
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.Nudges.Settled;
        CollectionAssert.AreEqual(new[] { 130.0 }, rig.Written);
    }

    [TestMethod]
    public async Task ATempoTypedAfterACommitThatFailedStillWaitsForTheLoopAndThenCommits()
    {
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        var done = rig.DoneAsync(90);

        rig.Fail(new IOException("The recovery file cannot be written."));
        await rig.StartedAsync(2);
        rig.Finish();
        await done;

        CollectionAssert.AreEqual(new[] { 90.0 }, rig.Written);
        Assert.AreEqual(90.0, rig.Tempo);
    }

    [TestMethod]
    public async Task ForgetDropsWhatIsNotCommittedYetAndTheCommitThatRunsStillLands()
    {
        // A page that is built again (the note value changed, a project opened) forgets the nudges that were waiting.
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(125);

        rig.Nudges.Forget();

        Assert.IsNull(rig.Nudges.Requested);
        rig.Finish();
        await rig.Nudges.Settled;
        CollectionAssert.AreEqual(new[] { 121.0 }, rig.Written, "the commit that was running lands; 125 is not committed");
        Assert.AreEqual(1, rig.Started);
        Assert.AreEqual(1, rig.Ended);
    }
}
