using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;

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
        internal int Ended;
        private int _asked;

        /// <summary>The project as the window holds it: what the commits leave, and what a choice that is made meanwhile is judged against.</summary>
        internal PulseDocument Document = Fresh(120);

        /// <summary>The tempo the project holds.</summary>
        internal double Tempo
        {
            get => Document.Settings.Tempo;
            set => Document.Settings.Tempo = value;
        }

        /// <summary>The meter (beats/denominator) that each edit of the project left, in the order they were published.</summary>
        internal readonly List<string> Meters = [];

        private static PulseDocument Fresh(double tempo) => new()
        {
            Settings = { Tempo = tempo, BeatsPerBar = 4, BeatDenominator = 4, BeatUnitQuarters = 1, Subdivision = 2 }
        };

        /// <summary>The meter the project holds, such as 4/4.</summary>
        internal string Meter => $"{Document.Settings.BeatsPerBar}/{Document.Settings.BeatDenominator}";

        /// <summary>Run when a loop has ended, in the turn in which it ends, before the wait for that loop returns to its caller.</summary>
        internal Action? OnEnded;

        internal Rig() => Nudges = new TempoNudges(Commit, Guard, () =>
        {
            Ended++;
            OnEnded?.Invoke();
        });

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

        /// <summary>The project that is current: another one once <see cref="ReplaceAsync"/> has been finished.</summary>
        internal int Project;

        /// <summary>Run when a replacement has been published, still under the edit lock.</summary>
        internal Action? OnReplaced;

        /// <summary>
        /// A rig in which the project can be replaced. Its commits check, under the edit lock and before anything else, that the project is
        /// still the one the tempo was asked for, and leave nothing behind when it is not, as the window's commit does.
        /// </summary>
        internal static Rig WithProjects() => new(true);

        private Rig(bool replaceable) => Nudges = new TempoNudges(CommitFor, Guard, () =>
        {
            Ended++;
            OnEnded?.Invoke();
        }, () => Project);

        /// <summary>One commit of a tempo that was asked for the project that was current then.</summary>
        internal Task CommitFor(double bpm, Func<bool> stillTheProject)
        {
            Interlocked.Increment(ref _asked);
            return DocumentPublication.RunAsync(_edits, async published =>
            {
                if (!stillTheProject()) throw new OperationCanceledException();
                var failure = await Gate();
                Tempo = bpm;
                Written.Add(bpm);
                published();
                if (failure is not null) throw failure;
            }, _ => { });
        }

        /// <summary>
        /// The project is replaced (Open, New, a ready-made rhythm, the next song): the replacement takes its place in the edit queue like
        /// a commit, and once it has been finished the project is another one, with a tempo of its own.
        /// </summary>
        internal Task ReplaceAsync(double tempo) => DocumentPublication.RunAsync(_edits, async published =>
        {
            await Gate();
            Project++;
            Document = Fresh(tempo);
            published();
            OnReplaced?.Invoke();
        }, _ => { });

        /// <summary>
        /// The window's commit of an edit, as a ready-made meter or Done in the Custom meter panel makes it: under the edit lock the edit
        /// runs on a copy of the project (it starts as 4/4 in quarter notes with every beat split in two, at the tempo the project holds),
        /// and the copy is the project once it has been published. An edit that throws leaves nothing behind.
        /// </summary>
        internal Task CommitEdit(Action<PulseDocument> edit)
        {
            Interlocked.Increment(ref _asked);
            return DocumentPublication.RunAsync(_edits, async published =>
            {
                var revised = LocalWorkspace.Clone(Document);
                edit(revised);
                var failure = await Gate();
                Document = revised;
                Written.Add(Tempo);
                Meters.Add(Meter);
                published();
                if (failure is not null) throw failure;
            }, _ => { });
        }

        /// <summary>A ready-made meter that changes the click unit, 6/8, as it was applied before it waited: committed at once.</summary>
        internal Task ConvertNow() => CommitEdit(document => MeterPresets.ApplyOneClickPerBeat(document.Settings, 6, 8, [3, 3]));

        /// <summary>The same ready-made meter (6/8 unless another is given) as the window applies it: after the nudges that are on their way, and only into the project it was chosen for.</summary>
        internal Task<bool> ApplyMeterAsync(MeterPreset? preset = null) =>
            MeterPresets.ApplyAfterNudgesAsync(Nudges, () => Document.Settings, CommitEdit, preset ?? new MeterPreset(6, 8, [3, 3]));

        /// <summary>Done in the Custom meter panel with a meter typed, as the window makes it: after the nudges and the edits that wait for them.</summary>
        internal Task<bool> CustomMeterAsync(int beats, int denominator) =>
            Nudges.EditAfterNudgesAsync(CommitEdit, document => MeterPresets.Apply(document.Settings, beats, denominator));

        /// <summary>The same Done as it was committed before it waited: at once, behind whatever is in the edit queue.</summary>
        internal Task CustomMeterNow(int beats, int denominator) =>
            CommitEdit(document => MeterPresets.Apply(document.Settings, beats, denominator));

        /// <summary>Completes once this many commits have been asked for, whether they have the edit lock or wait for it.</summary>
        internal async Task AskedAsync(int count)
        {
            var deadline = DateTime.UtcNow + Limit;
            while (Asked < count)
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException($"Only {Asked} commits were asked for, not {count}.");
                await Task.Delay(1);
            }
        }

        /// <summary>
        /// Finishes the commits that have the lock, one after the other, until the loop and what waits for it have ended, so that a test that
        /// expects a commit to be dropped fails on what was written, not on a wait that never ends.
        /// </summary>
        internal async Task FinishUntilSettledAsync()
        {
            var deadline = DateTime.UtcNow + Limit;
            while (!Nudges.Settled.IsCompleted)
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The loop did not end.");
                bool waiting;
                lock (_lock) waiting = _finished < _gates.Count;
                if (waiting) Finish();
                else await Task.Delay(5);
            }
        }
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
    public async Task ANudgeLoopThatStartsWhileATypedTempoWaitsForTheOneBeforeItIsCommittedBeforeIt()
    {
        // Done is pressed twice (the panel was dismissed and opened again in between) and, while the second waits for the first, the panel
        // is dismissed and the tempo nudged: a loop that did not exist when the second Done was pressed. Whatever was asked for until the
        // second Done's commit is queued is committed before it, as for a loop that was already running.
        var rig = new Rig();
        var first = rig.DoneAsync(90);
        var second = rig.DoneAsync(80);
        rig.Nudges.Request(121);
        rig.Nudges.Request(122);

        rig.Finish();
        await first.WaitAsync(Limit);
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.StartedAsync(3);
        rig.Finish();
        await rig.StartedAsync(4);
        rig.Finish();
        await second.WaitAsync(Limit);
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { 90.0, 121.0, 122.0, 80.0 }, rig.Written, "the nudges asked for before its commit was queued come first, the typed tempo last");
        Assert.AreEqual(80.0, rig.Tempo);
    }

    [TestMethod]
    public async Task ANudgeAskedForWhenTheLoopEndsIsCommittedBeforeATypedTempoThatWaitedForThatLoop()
    {
        // Done is pressed while the loop commits. In the turn in which that loop ends, before the wait for it returns, the tempo is nudged
        // twice: a loop that did not exist when the wait began. Both nudges were asked for before Done's commit is queued, so both are
        // committed before it; the typed tempo is not committed between them.
        var rig = new Rig();
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        var done = rig.DoneAsync(90);
        rig.OnEnded = () =>
        {
            rig.OnEnded = null;
            rig.Nudges.Request(130);
            rig.Nudges.Request(131);
            asked.SetResult();
        };

        rig.Finish();
        // The first commit of the new loop takes the lock inside the first request, so waiting for it to start alone would let this
        // thread release it before the second request has been made.
        await asked.Task.WaitAsync(Limit);
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.StartedAsync(3);
        rig.Finish();
        await rig.StartedAsync(4);
        rig.Finish();
        await done.WaitAsync(Limit);
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { 121.0, 130.0, 131.0, 90.0 }, rig.Written, "the nudges asked for before its commit was queued come first, the typed tempo last");
        Assert.AreEqual(90.0, rig.Tempo);
        Assert.AreEqual(2, rig.Ended, "two loops: the one that was running and the one that started when it ended");
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

    [TestMethod]
    public async Task ANudgeWhoseCommitIsQueuedBehindAReplacementOfTheProjectIsNotCommittedIntoTheReplacement()
    {
        // The loop is committing 121 and 122 is its newest request when the project is replaced (Open, New, a ready-made rhythm, the next
        // song): the replacement takes its place in the edit queue behind the commit that runs, and the loop's next commit behind it.
        var rig = Rig.WithProjects();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);
        var replacement = rig.ReplaceAsync(120);

        rig.Finish();
        await rig.StartedAsync(2);
        await rig.AskedAsync(2);
        rig.Finish();
        await replacement.WaitAsync(Limit);
        await rig.FinishUntilSettledAsync();

        CollectionAssert.AreEqual(new[] { 121.0 }, rig.Written, "122 was asked for the project that went: its commit leaves nothing behind");
        Assert.AreEqual(120.0, rig.Tempo, "the replacement keeps its own tempo");
        Assert.AreEqual(2, rig.Asked, "the commit of 122 was asked for, queued behind the replacement, and dropped there");
        Assert.AreEqual(0, rig.Reported.Count, "a nudge that is dropped because its project is gone is not an error");
        Assert.AreEqual(1, rig.Ended);
    }

    [TestMethod]
    public async Task ANudgeAskedForTheReplacementIsCommittedEvenWhenItIsTheSameNumberAsTheOneThatWasDropped()
    {
        // The person nudges the replacement to 122 the moment it is published, while the loop's commit of the 122 that was asked for the
        // project that went is still queued behind it. The loop drops that one and then commits the new one: it is not "the same
        // request again", it is another project's.
        var rig = Rig.WithProjects();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);
        rig.OnReplaced = () => rig.Nudges.Request(122);
        var replacement = rig.ReplaceAsync(120);

        rig.Finish();
        await rig.StartedAsync(2);
        await rig.AskedAsync(2);
        rig.Finish();
        await rig.StartedAsync(3);
        rig.Finish();
        await replacement.WaitAsync(Limit);
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { 121.0, 122.0 }, rig.Written, "the 122 of the project that went was dropped, the 122 of the replacement was committed");
        Assert.AreEqual(122.0, rig.Tempo);
        Assert.AreEqual(3, rig.Asked, "121, the dropped 122 and the replacement's own 122");
        Assert.AreEqual(0, rig.Reported.Count);
        Assert.IsNull(rig.Nudges.Requested);
        Assert.AreEqual(1, rig.Ended, "one loop for all of it");
    }

    [TestMethod]
    public async Task TheRequestOfAProjectThatWasReplacedIsNotShownAndNotCommitted()
    {
        var rig = Rig.WithProjects();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);
        Assert.AreEqual(122.0, rig.Nudges.Requested);

        rig.Project++;

        Assert.IsNull(rig.Nudges.Requested, "the dial shows the replacement's own tempo, not what was asked for the project that went");
        rig.Finish();
        await rig.FinishUntilSettledAsync();
        CollectionAssert.AreEqual(new[] { 121.0 }, rig.Written, "the commit that was running lands; 122 is not committed");
        Assert.AreEqual(1, rig.Asked, "the loop does not ask for a commit of what was asked for another project");
        Assert.AreEqual(1, rig.Ended);
    }

    [TestMethod]
    public async Task ACommitThatIsCancelledForAnyOtherReasonStillEndsTheLoopAndDropsWhatWasAskedAfterIt()
    {
        // Only a commit that was dropped because its project is gone is passed over; the project is the current one here.
        var rig = Rig.WithProjects();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);

        rig.Fail(new OperationCanceledException());
        await rig.SettledAsync();

        CollectionAssert.AreEqual(Array.Empty<double>(), rig.Written);
        Assert.AreEqual(1, rig.Asked, "122 is not asked for: the loop ended with the cancelled commit, as it always did");
        Assert.IsNull(rig.Nudges.Requested);
        Assert.AreEqual(1, rig.Ended);
    }

    [TestMethod]
    public async Task ATempoTypedAfterADroppedNudgeStillWaitsForTheLoopAndThenCommitsIntoTheReplacement()
    {
        // Done in the replacement's panel while the loop's last commit, for the project that went, is still queued behind the replacement.
        var rig = Rig.WithProjects();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);
        var replacement = rig.ReplaceAsync(120);
        rig.Finish();
        await rig.StartedAsync(2);
        await rig.AskedAsync(2);
        var done = rig.DoneAsync(90);
        Assert.AreEqual(2, rig.Asked, "Done waits for the loop, which has the dropped commit of 122 to go through");

        rig.Finish();
        await replacement.WaitAsync(Limit);
        await rig.FinishUntilSettledAsync();
        await done.WaitAsync(Limit);

        CollectionAssert.AreEqual(new[] { 121.0, 90.0 }, rig.Written, "the nudge of the project that went was dropped; what was typed is committed");
        Assert.AreEqual(90.0, rig.Tempo);
    }

    [TestMethod]
    public async Task AReadyMadeMeterThatConvertsTheTempoIsCommittedAfterTheNudgesThatAreOnTheirWay()
    {
        // 4/4 in quarter notes with every beat split in two, to 6/8: the click unit becomes an eighth, so the tempo is doubled (the same clicks
        // at the same speed). The meter is chosen while the loop commits 121 and 123 is its newest request.
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);
        rig.Nudges.Request(123);

        var meter = rig.ApplyMeterAsync();
        Assert.IsFalse(meter.IsCompleted);
        Assert.AreEqual(1, rig.Asked, "the meter has not asked for its commit: it waits for the nudges, it does not queue behind the one that runs");
        rig.Finish();
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.StartedAsync(3);
        rig.Finish();
        Assert.IsTrue(await meter.WaitAsync(Limit), "the meter was applied");
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { 121.0, 123.0, 246.0 }, rig.Written, "the nudges first, then the meter, which converts the tempo that was nudged to");
        Assert.AreEqual(246.0, rig.Tempo, "123 quarter notes are 246 eighth notes");
        Assert.IsNull(rig.Nudges.Requested);
    }

    [TestMethod]
    public async Task AReadyMadeMeterChosenForAProjectThatWasReplacedIsNotAppliedToTheReplacement()
    {
        // The meter was chosen on the page of the project that goes. The loop has a commit to finish first, so the meter's commit is queued
        // behind the replacement, and it leaves the replacement alone.
        var rig = Rig.WithProjects();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        var replacement = rig.ReplaceAsync(120);
        var meter = rig.ApplyMeterAsync();

        rig.Finish();
        await rig.StartedAsync(2);
        await rig.AskedAsync(2);
        rig.Finish();
        await replacement.WaitAsync(Limit);

        Assert.IsFalse(await meter.WaitAsync(Limit), "nothing was applied");
        CollectionAssert.AreEqual(new[] { 121.0 }, rig.Written);
        Assert.AreEqual(120.0, rig.Tempo, "the replacement keeps its own tempo: it was not converted");
        Assert.AreEqual(0, rig.Reported.Count);
    }

    [TestMethod]
    public async Task WithoutWaitingAReadyMadeMeterIsCommittedBetweenTheLoopsCommitsAndItsConversionIsLost()
    {
        // What choosing a meter did before it waited: its commit is queued behind the one that runs, and the loop's next commit (its newest,
        // older request, in the old unit) behind that. Kept so that the test above cannot pass because this rig has no race.
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);
        rig.Nudges.Request(123);

        var meter = rig.ConvertNow();
        rig.Finish();
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.StartedAsync(3);
        rig.Finish();
        await meter.WaitAsync(Limit);
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { 121.0, 242.0, 123.0 }, rig.Written);
        Assert.AreEqual(123.0, rig.Tempo, "the converted tempo was overwritten by a nudge that was asked for before it, in the old unit");
    }

    [TestMethod]
    public async Task ACustomMeterChosenAfterAReadyMadeMeterThatWaitsIsAppliedAfterItAndIsTheLastWord()
    {
        // 6/8 is picked while the loop commits, and then 3/4 is entered in the Custom meter panel and Done is pressed: the meter that was
        // chosen last is the one the project ends on, and the Custom meter does not ask for its commit before the ready-made one has ended.
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);

        var readyMade = rig.ApplyMeterAsync();
        var custom = rig.CustomMeterAsync(3, 4);
        Assert.AreEqual(1, rig.Asked, "neither has asked for its commit: both wait for the nudges");
        rig.Finish();
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.StartedAsync(3);
        Assert.AreEqual(3, rig.Asked, "the Custom meter waits for the ready-made meter, which was chosen before it, to have ended");
        rig.Finish();
        Assert.IsTrue(await readyMade.WaitAsync(Limit), "the ready-made meter was applied");
        await rig.StartedAsync(4);
        rig.Finish();
        Assert.IsTrue(await custom.WaitAsync(Limit), "the Custom meter was applied");
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { "6/8", "3/4" }, rig.Meters, "in the order they were chosen");
        Assert.AreEqual("3/4", rig.Meter);
        Assert.AreEqual(1.0, rig.Document.Settings.BeatUnitQuarters, "3/4 is counted in quarter notes");
        CollectionAssert.AreEqual(new[] { 121.0, 122.0, 244.0, 244.0 }, rig.Written, "the nudges first, then the meters; the Custom meter keeps the tempo number");
    }

    [TestMethod]
    public async Task WithoutWaitingACustomMeterIsCommittedAheadOfAReadyMadeMeterThatWaitsAndIsOverwrittenByIt()
    {
        // What Done in the Custom meter panel did before it waited: committed at once, ahead of a ready-made meter that was chosen earlier and
        // that still waits for the nudges, which then lands on top of it. Kept so that the test above cannot pass because this rig has no race.
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);

        var readyMade = rig.ApplyMeterAsync();
        var custom = rig.CustomMeterNow(3, 4);
        rig.Finish();
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.StartedAsync(3);
        rig.Finish();
        await rig.StartedAsync(4);
        rig.Finish();
        await custom.WaitAsync(Limit);
        await readyMade.WaitAsync(Limit);
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { "3/4", "6/8" }, rig.Meters);
        Assert.AreEqual("6/8", rig.Meter, "the meter that was chosen first was applied last");
    }

    [TestMethod]
    public async Task EditsThatWaitForTheNudgesAreCommittedInTheOrderTheyWereChosenWhateverTheirKind()
    {
        // A ready-made meter, Done in the Tempo panel, Done in the Custom meter panel and another ready-made meter, chosen in that order while
        // the loop commits. The chain is first in, first out: each one asks for its commit only when the one chosen before it has ended,
        // whichever of the waits the end of the loop runs first, so the project ends as the last one made it.
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);

        var first = rig.ApplyMeterAsync();
        var second = rig.DoneAsync(90);
        var third = rig.CustomMeterAsync(3, 4);
        var fourth = rig.ApplyMeterAsync(new MeterPreset(5, 4, [3, 2]));
        Assert.IsTrue(rig.Nudges.TypedEditsOnTheirWay);
        Assert.AreEqual(1, rig.Asked, "none of them has asked for its commit");

        for (var commit = 1; commit <= 6; commit++)
        {
            await rig.StartedAsync(commit);
            Assert.AreEqual(commit, rig.Asked, $"commit {commit} was asked for alone: the edits chosen after it wait for it to end");
            rig.Finish();
        }
        Assert.IsTrue(await first.WaitAsync(Limit));
        await second.WaitAsync(Limit);
        Assert.IsTrue(await third.WaitAsync(Limit));
        Assert.IsTrue(await fourth.WaitAsync(Limit));
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { 121.0, 122.0, 244.0, 90.0, 90.0, 90.0 }, rig.Written, "the nudges, then each edit in the order it was chosen");
        CollectionAssert.AreEqual(new[] { "6/8", "3/4", "5/4" }, rig.Meters);
        Assert.AreEqual("5/4", rig.Meter, "the one chosen last is the one the project ends on");
        Assert.AreEqual(90.0, rig.Tempo);
        Assert.IsFalse(rig.Nudges.TypedEditsOnTheirWay);
    }

    [TestMethod]
    public async Task ACustomMeterTypedForAProjectThatWasReplacedIsNotAppliedToTheReplacement()
    {
        // Done was pressed on the page of the project that goes. The loop has a commit to finish first, so the Custom meter's commit is queued
        // behind the replacement, and it leaves the replacement alone.
        var rig = Rig.WithProjects();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        var replacement = rig.ReplaceAsync(100);
        var custom = rig.CustomMeterAsync(3, 4);

        rig.Finish();
        await rig.StartedAsync(2);
        await rig.AskedAsync(2);
        rig.Finish();
        await replacement.WaitAsync(Limit);

        Assert.IsFalse(await custom.WaitAsync(Limit), "nothing was applied");
        await rig.SettledAsync();
        CollectionAssert.AreEqual(new[] { 121.0 }, rig.Written);
        Assert.AreEqual(0, rig.Meters.Count);
        Assert.AreEqual("4/4", rig.Meter, "the replacement keeps its own meter");
        Assert.AreEqual(100.0, rig.Tempo);
        Assert.AreEqual(0, rig.Reported.Count);
    }

    [TestMethod]
    public async Task ACustomMeterIsNotAppliedToAProjectThatWasReplacedWhileItWasStillWaitingForTheLoop()
    {
        // The replacement is published while the Custom meter still waits for the loop (the loop has a commit of its own queued behind the
        // replacement), so the project is another one when the Custom meter asks for its commit. It belongs to the project it was typed for,
        // which is read when Done is pressed and not when its commit is asked for.
        var rig = Rig.WithProjects();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        rig.Nudges.Request(122);
        var replacement = rig.ReplaceAsync(100);
        var custom = rig.CustomMeterAsync(3, 4);

        rig.Finish();
        await rig.StartedAsync(2);
        await rig.AskedAsync(2);
        Assert.IsFalse(custom.IsCompleted, "the Custom meter still waits for the loop, which has its commit of 122 queued behind the replacement");
        rig.Finish();
        await replacement.WaitAsync(Limit);
        await rig.FinishUntilSettledAsync();

        Assert.IsFalse(await custom.WaitAsync(Limit), "nothing was applied");
        CollectionAssert.AreEqual(new[] { 121.0 }, rig.Written, "the nudge of the project that went was dropped too");
        Assert.AreEqual(0, rig.Meters.Count);
        Assert.AreEqual("4/4", rig.Meter, "the replacement keeps its own meter");
        Assert.AreEqual(100.0, rig.Tempo);
        Assert.AreEqual(0, rig.Reported.Count);
    }

    [TestMethod]
    public async Task AMeterEditThatFailsGoesToItsCallerAndDoesNotHoldUpTheOneChosenAfterIt()
    {
        var rig = new Rig();
        var failing = rig.Nudges.EditAfterNudgesAsync(rig.CommitEdit, _ => throw new InvalidOperationException("The meter cannot be applied."));
        var custom = rig.CustomMeterAsync(3, 4);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => failing.WaitAsync(Limit));
        await rig.StartedAsync(1);
        rig.Finish();
        Assert.IsTrue(await custom.WaitAsync(Limit));
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { "3/4" }, rig.Meters, "the failed edit left nothing behind");
        Assert.IsFalse(rig.Nudges.TypedEditsOnTheirWay);
    }

    [TestMethod]
    public async Task AMeterThatTheCommitRefusesIsNotAppliedAndHoldsUpNothing()
    {
        // A locked project refuses the edit and queues nothing: the edit does not run and the meter was not applied.
        var rig = new Rig();
        var refused = rig.Nudges.EditAfterNudgesAsync(_ => Task.CompletedTask, _ => Assert.Fail("The edit must not run."));

        Assert.IsFalse(await refused.WaitAsync(Limit), "it was not applied");
        Assert.IsFalse(rig.Nudges.TypedEditsOnTheirWay);
        var custom = rig.CustomMeterAsync(3, 4);
        await rig.StartedAsync(1);
        rig.Finish();
        Assert.IsTrue(await custom.WaitAsync(Limit));
        Assert.AreEqual("3/4", rig.Meter);
    }

    [TestMethod]
    public async Task AMeterCommitThatIsCancelledForAnyOtherReasonGoesToItsCaller()
    {
        // Only a project that was replaced makes a cancelled commit a quiet no-op; a cancellation of any other kind is not hidden.
        var rig = Rig.WithProjects();
        var meter = rig.Nudges.EditAfterNudgesAsync(_ => Task.FromException(new OperationCanceledException("Stopped.")), _ => { });

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => meter.WaitAsync(Limit));
        Assert.IsFalse(rig.Nudges.TypedEditsOnTheirWay);
    }

    [TestMethod]
    public async Task ACustomMeterWhenNothingIsOnItsWayIsCommittedAtOnce()
    {
        var rig = new Rig();
        var custom = rig.CustomMeterAsync(3, 4);

        Assert.AreEqual(1, rig.Asked, "it took its place in the edit queue in the turn it was pressed");
        Assert.IsTrue(rig.Nudges.TypedEditsOnTheirWay, "it is on its way until it has ended");
        rig.Finish();
        Assert.IsTrue(await custom.WaitAsync(Limit));
        await rig.SettledAsync();
        Assert.IsFalse(rig.Nudges.TypedEditsOnTheirWay);
        Assert.AreEqual("3/4", rig.Meter);
    }

    [TestMethod]
    public async Task AReadyMadeMeterThatTheProjectHoldsIsStillAppliedWhenAnEarlierMeterIsOnItsWay()
    {
        // The project holds 4/4 and 6/8 is picked, which waits for the nudges. Then 4/4 is picked again: judged against what the project holds
        // now that would change nothing, and the 6/8 would land after it. The last choice is 4/4, so it is applied after the 6/8.
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);

        var sixEight = rig.ApplyMeterAsync();
        var fourFour = rig.ApplyMeterAsync(new MeterPreset(4, 4, []));
        Assert.IsTrue(rig.Nudges.TypedEditsOnTheirWay);
        rig.Finish();
        await rig.StartedAsync(2);
        rig.Finish();
        await rig.StartedAsync(3);
        rig.Finish();
        Assert.IsTrue(await sixEight.WaitAsync(Limit));
        Assert.IsTrue(await fourFour.WaitAsync(Limit), "4/4 was applied again, after the 6/8");
        await rig.SettledAsync();

        CollectionAssert.AreEqual(new[] { "6/8", "4/4" }, rig.Meters);
        Assert.AreEqual("4/4", rig.Meter);
        Assert.AreEqual(1.0, rig.Document.Settings.BeatUnitQuarters);
    }

    [TestMethod]
    public async Task AReadyMadeMeterThatTheProjectHoldsAlreadyIsNotAppliedAgainWhenOnlyNudgesAreOnTheirWay()
    {
        // The same pick with nothing but a nudge on its way: nothing changes, so nothing is committed (applying it would restart the accents).
        var rig = new Rig();
        rig.Nudges.Request(121);
        await rig.StartedAsync(1);
        Assert.IsFalse(rig.Nudges.TypedEditsOnTheirWay, "a nudge only changes the tempo");

        var applied = await rig.ApplyMeterAsync(new MeterPreset(4, 4, [])).WaitAsync(Limit);

        Assert.IsFalse(applied);
        Assert.AreEqual(1, rig.Asked, "it asked for no commit: only the nudge's");
        rig.Finish();
        await rig.SettledAsync();
        Assert.AreEqual(0, rig.Meters.Count);
    }
}
