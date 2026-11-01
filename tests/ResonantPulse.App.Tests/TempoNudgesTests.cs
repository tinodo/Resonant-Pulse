using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class TempoNudgesTests
{
    /// <summary>A test that is waiting for something that never comes fails after this long; it does not hang.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The nudges, a tempo that is typed and the edit queue as the window has them: a project tempo that commits change, an edit lock that
    /// is handed out in the order it was asked for, and commits that take as long as the test says. A commit waits in the queue,
    /// then (when it has the lock) waits for the test to finish it, then writes the tempo and publishes it, as CommitAsync does; the
    /// recovery file that is written after it can fail (FailAfterPublishing) as well as the commit before it (Fail).
    /// </summary>
    private sealed class Rig
    {
        private readonly SemaphoreSlim _edits = new(1);
        private readonly List<TaskCompletionSource<Exception?>> _gates = [];
        private readonly List<(int Count, TaskCompletionSource Source)> _waiters = [];
        private int _finished;
        private readonly object _lock = new();
        internal readonly List<double> Written = [];
        internal readonly List<Exception> Reported = [];
        internal readonly TempoNudges Nudges;
        internal double Tempo = 120;
        internal int Ended;
        private int _asked;

        internal Rig() => Nudges = new TempoNudges(Commit, Guard, () => Ended++);

        /// <summary>How many commits have the edit lock so far (each one is waiting to be finished, or has been).</summary>
        internal int Started { get { lock (_lock) return _gates.Count; } }

        /// <summary>How many commits were asked for, whether they have the edit lock or wait for it.</summary>
        internal int Asked => Volatile.Read(ref _asked);

        /// <summary>One commit of a tempo, through the real publication transaction and a first-in, first-out lock.</summary>
        internal Task Commit(double bpm)
        {
            Interlocked.Increment(ref _asked);
            return DocumentPublication.RunAsync(_edits, async published =>
            {
                var failure = await Gate();
                Tempo = bpm;
                Written.Add(bpm);
                published();
                if (failure is not null) throw failure;
            }, _ => { });
        }

        private Task<Exception?> Gate()
        {
            var gate = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
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

        /// <summary>Completes once this many commits have the lock.</summary>
        internal Task StartedAsync(int count)
        {
            lock (_lock)
            {
                if (_gates.Count >= count) return Task.CompletedTask;
                var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, source));
                return source.Task.WaitAsync(Limit);
            }
        }

        /// <summary>The commit that has had the lock longest and is still waiting is done.</summary>
        internal void Finish()
        {
            lock (_lock) _gates[_finished++].SetResult(null);
        }

        /// <summary>The same commit fails before it has written anything (the project cannot be validated).</summary>
        internal void Fail(Exception exception)
        {
            lock (_lock) _gates[_finished++].SetException(exception);
        }

        /// <summary>The same commit is published and then fails (the recovery file cannot be written): the project holds the tempo.</summary>
        internal void FailAfterPublishing(Exception exception)
        {
            lock (_lock) _gates[_finished++].SetResult(exception);
        }

        /// <summary>The loop has ended and so has a typed tempo that was on its way: what was asked for has been committed, refused or dropped.</summary>
        internal Task SettledAsync() => Nudges.Settled.WaitAsync(Limit);

        /// <summary>What the window's error handling does: shows the error and lets nothing escape the loop.</summary>
        private async Task Guard(Func<Task> action)
        {
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { lock (Reported) Reported.Add(exception); }
        }

        /// <summary>Done in the Tempo panel with a tempo typed: its commit waits for the nudges that are on their way.</summary>
        internal Task DoneAsync(double typed) => Nudges.AfterNudgesAsync(() => Commit(typed));
    }

    [TestMethod]
    public async Task NothingIsOnItsWayBeforeTheFirstRequest()
    {
        var rig = new Rig();

        Assert.IsNull(rig.Nudges.Requested);
        Assert.IsTrue(rig.Nudges.Settled.IsCompleted);
        await rig.SettledAsync();
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
        await rig.SettledAsync();

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
        Assert.AreEqual(1, rig.Asked, "the others wait outside the edit queue: they are not commits yet, not even ones that wait for the lock");
        rig.Finish();
        await rig.StartedAsync(2);
        Assert.AreEqual(124.0, rig.Nudges.Requested, "the newest is still on its way while it is committed");
        Assert.IsFalse(rig.Nudges.Settled.IsCompleted);
        rig.Finish();
        await rig.SettledAsync();

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
        await rig.SettledAsync();

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
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { 121.0 }, rig.Written, "the newest wanted value is the one that is being committed already");
        Assert.AreEqual(1, rig.Asked);
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
        Assert.AreEqual(1, rig.Asked, "Done has not asked for its commit: it waits for the nudges, it does not queue behind the one that runs");
        rig.Finish();
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.StartedAsync(3);
        rig.Finish();
        await done.WaitAsync(Limit);
        await rig.SettledAsync();

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
        await done.WaitAsync(Limit);
        await rig.SettledAsync();

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
        await done.WaitAsync(Limit);
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
        await done.WaitAsync(Limit);
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { 90.0, 91.0 }, rig.Written, "the later request is the later commit");
        Assert.AreEqual(91.0, rig.Tempo);
    }

    [TestMethod]
    public async Task ANudgeMadeWhileTheTypedTempoWaitsIsTakenOnByTheLoopAndCommittedBeforeIt()
    {
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        var done = rig.DoneAsync(90);

        rig.Nudges.Request(130);
        rig.Finish();
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.StartedAsync(3);
        rig.Finish();
        await done.WaitAsync(Limit);

        CollectionAssert.AreEqual(new[] { 121.0, 130.0, 90.0 }, rig.Written, "the loop was still at work, so what it took on lands first");
        Assert.AreEqual(90.0, rig.Tempo);
    }

    [TestMethod]
    public async Task SettledIncludesATypedTempoThatIsOnItsWayAndSoWhatWaitsForItNeverPassesIt()
    {
        // Closing, Start and Tap to start wait for Settled. A Done that has been pressed is part of it from that moment, even after the
        // loop it waited for has ended and before its own commit is done, whatever order the waiters wake in.
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        var done = rig.DoneAsync(90);
        var closing = rig.Nudges.Settled;

        rig.Finish();
        await rig.StartedAsync(2);

        Assert.IsFalse(closing.IsCompleted, "the loop has ended, but the typed tempo has not been committed yet");
        Assert.IsFalse(rig.Nudges.Settled.IsCompleted);
        rig.Finish();
        await closing.WaitAsync(Limit);
        await done.WaitAsync(Limit);
        CollectionAssert.AreEqual(new[] { 121.0, 90.0 }, rig.Written);
        Assert.AreEqual(90.0, rig.Tempo, "what waited for Settled sees the typed tempo");
    }

    [TestMethod]
    public async Task SettledIncludesATypedTempoThatWaitsEvenWhenNothingWasOnItsWay()
    {
        var rig = new Rig();
        var done = rig.DoneAsync(90);

        Assert.IsFalse(rig.Nudges.Settled.IsCompleted, "its commit is on its way");
        rig.Finish();
        await rig.SettledAsync();
        await done.WaitAsync(Limit);
        Assert.IsTrue(rig.Nudges.Settled.IsCompleted);
    }

    [TestMethod]
    public async Task ATypedTempoThatFailsGoesToItsCallerAndNeverFailsSettled()
    {
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        var done = rig.DoneAsync(90);
        rig.Finish();
        await rig.StartedAsync(2);
        var failure = new IOException("The project cannot be validated.");

        rig.Fail(failure);

        var thrown = await Assert.ThrowsExactlyAsync<IOException>(() => done.WaitAsync(Limit));
        Assert.AreSame(failure, thrown, "Done shows it in the panel");
        await rig.SettledAsync();
        Assert.AreEqual(121.0, rig.Tempo, "the nudge before it landed and the typed tempo did not");
        Assert.IsTrue(rig.Nudges.Settled.IsCompleted, "closing and Start do not fail for it");
    }

    [TestMethod]
    public async Task TwoTypedTemposThatOverlapAreBothPartOfSettled()
    {
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        var first = rig.DoneAsync(90);
        var second = rig.DoneAsync(80);

        rig.Finish();
        await rig.StartedAsync(2);
        rig.Finish();
        await first.WaitAsync(Limit);
        await rig.StartedAsync(3);

        Assert.IsFalse(rig.Nudges.Settled.IsCompleted, "the second typed tempo is still on its way");
        rig.Finish();
        await second.WaitAsync(Limit);
        await rig.SettledAsync();
        CollectionAssert.AreEqual(new[] { 121.0, 90.0, 80.0 }, rig.Written, "in the order they were typed");
    }

    [TestMethod]
    public async Task ATypedTempoThatCouldEndAtOnceStillWaitsForTheOneBeforeItSoSettledCannotPassThatOne()
    {
        // A Done on a project that is locked ends without committing anything, but the Done before it is still being applied: the
        // later one ends after the earlier one, so what waits for Settled never passes the earlier one.
        var rig = new Rig();
        var first = rig.DoneAsync(90);
        var second = rig.Nudges.AfterNudgesAsync(() => Task.CompletedTask);

        Assert.IsFalse(second.IsCompleted, "it waits for the first typed tempo");
        Assert.IsFalse(rig.Nudges.Settled.IsCompleted, "the first typed tempo has not been committed yet");
        rig.Finish();
        await first.WaitAsync(Limit);
        await second.WaitAsync(Limit);
        await rig.SettledAsync();
        CollectionAssert.AreEqual(new[] { 90.0 }, rig.Written);
    }

    [TestMethod]
    public async Task ATypedCommitThatThrowsAtOnceEndsAndNeverFailsSettled()
    {
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        var failure = new InvalidOperationException("Nothing could be asked for.");
        var done = rig.Nudges.AfterNudgesAsync(() => throw failure);

        rig.Finish();

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => done.WaitAsync(Limit));
        Assert.AreSame(failure, thrown);
        await rig.SettledAsync();
        Assert.IsTrue(rig.Nudges.Settled.IsCompleted);
        CollectionAssert.AreEqual(new[] { 121.0 }, rig.Written, "the nudge that was on its way landed");
    }

    [TestMethod]
    public async Task ATypedTempoDoesNotAskForItsCommitBeforeTheOneTypedBeforeItHasEnded()
    {
        // Two presses of Done (the panel was dismissed and opened again between them) while nothing is on its way: the second waits for the
        // first, so the order is the order they were pressed and does not depend on which wait is run first.
        var rig = new Rig();
        var first = rig.DoneAsync(90);
        var second = rig.DoneAsync(80);

        Assert.AreEqual(1, rig.Asked, "the second typed tempo has not asked for its commit: it waits for the first");
        Assert.IsFalse(second.IsCompleted);
        rig.Finish();
        await first.WaitAsync(Limit);
        await rig.StartedAsync(2);
        rig.Finish();
        await second.WaitAsync(Limit);
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { 90.0, 80.0 }, rig.Written, "in the order they were pressed");
        Assert.AreEqual(80.0, rig.Tempo);
    }

    [TestMethod]
    public async Task ATypedTempoThatFailsDoesNotHoldUpTheOneTypedAfterIt()
    {
        var rig = new Rig();
        var first = rig.DoneAsync(90);
        var second = rig.DoneAsync(80);

        rig.Fail(new IOException("The project cannot be validated."));
        await Assert.ThrowsExactlyAsync<IOException>(() => first.WaitAsync(Limit));
        await rig.StartedAsync(2);
        rig.Finish();
        await second.WaitAsync(Limit);
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { 80.0 }, rig.Written, "the first did not write anything, and the second still ran");
        Assert.AreEqual(80.0, rig.Tempo);
    }

    [TestMethod]
    public async Task SettledOfALoopThatEndedStaysCompleteWhenAnotherLoopStarts()
    {
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        var first = rig.Nudges.Settled;
        rig.Finish();
        await first.WaitAsync(Limit);

        rig.Nudges.Request(130);
        await rig.StartedAsync(2);

        Assert.IsTrue(first.IsCompleted, "what waited for the first loop is not held up by the second");
        Assert.IsFalse(rig.Nudges.Settled.IsCompleted, "what waits now waits for the loop that is running");
        rig.Finish();
        await rig.SettledAsync();
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
        var failure = new IOException("The project cannot be validated.");

        rig.Fail(failure);
        await rig.SettledAsync();

        Assert.AreEqual(1, rig.Reported.Count);
        Assert.AreSame(failure, rig.Reported[0]);
        Assert.IsNull(rig.Nudges.Requested, "what was asked for after the failed commit is dropped with the loop");
        Assert.AreEqual(1, rig.Ended);
        Assert.AreEqual(1, rig.Asked, "nothing else was asked for");
        CollectionAssert.AreEqual(Array.Empty<double>(), rig.Written);
        rig.Nudges.Request(130);
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.SettledAsync();
        CollectionAssert.AreEqual(new[] { 130.0 }, rig.Written);
    }

    [TestMethod]
    public async Task ARecoveryFileThatCannotBeWrittenAfterThePublicationLeavesTheNudgeInTheProjectAndEndsTheLoop()
    {
        // The project holds the tempo before the recovery file is written, so a failure there is reported and the tempo stays.
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(125);
        var failure = new IOException("The recovery file cannot be written.");

        rig.FailAfterPublishing(failure);
        await rig.SettledAsync();

        Assert.AreSame(failure, rig.Reported.Single());
        CollectionAssert.AreEqual(new[] { 121.0 }, rig.Written);
        Assert.AreEqual(121.0, rig.Tempo, "published before the failure");
        Assert.IsNull(rig.Nudges.Requested, "nothing is on its way, so the dial shows the project's 121");
        Assert.AreEqual(1, rig.Ended);
    }

    [TestMethod]
    public async Task ATempoTypedAfterACommitThatFailedStillWaitsForTheLoopAndThenCommits()
    {
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        var done = rig.DoneAsync(90);
        Assert.AreEqual(1, rig.Asked, "Done waits for the loop even though the commit that the loop is at will fail");

        rig.Fail(new IOException("The project cannot be validated."));
        await rig.StartedAsync(2);
        rig.Finish();
        await done.WaitAsync(Limit);

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
        await rig.SettledAsync();
        CollectionAssert.AreEqual(new[] { 121.0 }, rig.Written, "the commit that was running lands; 125 is not committed");
        Assert.AreEqual(1, rig.Asked);
        Assert.AreEqual(1, rig.Ended);
    }
}
