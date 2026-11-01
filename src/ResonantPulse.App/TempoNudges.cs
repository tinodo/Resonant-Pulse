namespace ResonantPulse.App;

/// <summary>
/// The tempo that the person nudges to (a stepper, the wheel over the dial, TAP): it is shown at once and committed one value at a
/// time. While a commit runs, further requests only change the wanted value, and the one loop then commits the newest directly, so
/// holding a stepper or tapping quickly never queues a backlog and never reorders: the newest request is the next commit. (A commit
/// that fails ends the loop, and what was asked for after it is dropped with it, as it always was.)
/// <para>
/// A tempo that is typed in the Tempo panel is not a nudge: it is an edit of its own, and it has to come after the nudges that are on
/// their way. The edit queue is first in, first out, so an edit made while the loop is committing is queued between two of the loop's
/// commits, and the loop then goes on to commit its older value over it. <see cref="AfterNudgesAsync"/> makes the typed edit wait for
/// the typed tempos pressed before it and for the loop, until no loop is running, so that whatever was nudged to until the moment its
/// commit is queued is committed before it (only the newest request of a burst is committed at all, and a failed commit or a page that
/// is built again drops the rest, as before), however many loops that takes (one can start while an earlier typed tempo is being
/// applied, and only a person who keeps nudging can hold it up, as they could with the one loop it waited for before); a nudge asked
/// for after its commit has been queued is committed after it, in queue order. While it is on its way,
/// <see cref="Settled"/> includes it, so that closing, Start and Tap to start, which wait for the tempo to settle, wait for it too and
/// not only for the loop. (Closing waits for at most its four seconds and a second attempt leaves anyway.)
/// </para>
/// <para>
/// A nudge belongs to the project that was shown when it was asked for: it is a number in that project's unit, read off its dial. The
/// project can be replaced (Open, New, a ready-made rhythm, the next song of a setlist) while a nudge waits, or while its commit waits in
/// the edit queue behind the replacement, and what was nudged to must never be written into the project that took its place. A request is
/// therefore dropped from <see cref="Requested"/> the moment the project is another one (the dial shows the replacement's own tempo), the
/// loop does not ask for a commit of it, and a commit that is already queued is told to leave nothing behind
/// (<c>stillTheProject</c>, checked inside the edit, as Done's commit checks the project it was typed for). A commit that is dropped for
/// that reason is not an error, and the loop goes on to what was asked for after it, for the project that is there now, even when that is
/// the same number.
/// </para>
/// Pure and for the UI thread only, so every order the commits can finish in is tested without a window.
/// </summary>
internal sealed class TempoNudges
{
   private readonly Func<double, Func<bool>, Task> _commit;
   private readonly Func<Func<Task>, Task> _guard;
   private readonly Action _ended;
   private readonly Func<int> _project;
   private bool _running;
   private Task _loop = Task.CompletedTask;
   private Task _typed = Task.CompletedTask;
   private double? _requested;
   private int _requestedFor;

   /// <summary>For a project that is never replaced.</summary>
   internal TempoNudges(Func<double, Task> commit, Func<Func<Task>, Task> guard, Action ended)
       : this((wanted, _) => commit(wanted), guard, ended, () => 0)
   {
   }

   /// <param name="commit">
   /// Commits one tempo to the project (it waits in the edit queue, so it finishes later). It must call the second argument inside the edit,
   /// under the edit lock, before it changes anything, and throw <see cref="OperationCanceledException"/> when it says false: the project
   /// that the tempo was asked for has been replaced, and the commit must leave nothing behind (no Undo step, no new project, no save).
   /// </param>
   /// <param name="guard">Runs the loop and reports what goes wrong in it, without letting it escape (the window's error handling).</param>
   /// <param name="ended">
   /// Called when the loop has ended and nothing is on its way any more: the window shows the project's tempo again. It must not ask
   /// for a tempo: a loop whose commits all finish at once (a locked project) has ended before its own task is recorded, which would
   /// replace the task of a loop started from here. (The unit tests do ask from here, with a commit that is held, to stand for an event
   /// that runs in the turn in which a loop ends.)
   /// </param>
   /// <param name="project">Which project is the current one: a number that changes whenever the whole project is replaced.</param>
   internal TempoNudges(Func<double, Func<bool>, Task> commit, Func<Func<Task>, Task> guard, Action ended, Func<int> project)
   {
       _commit = commit;
       _guard = guard;
       _ended = ended;
       _project = project;
   }

   /// <summary>
   /// The newest tempo that was asked for and that the project does not hold yet; null while nothing is on its way, and for a request that
   /// was asked for a project that has been replaced since.
   /// </summary>
   internal double? Requested => _requested is { } && _requestedFor == _project() ? _requested : null;

    /// <summary>
    /// Completes when the loop that is running (or the last one that ran) has ended, and so has the commit of a tempo typed in the
    /// panel, if one is being applied: every tempo asked for before the moment this is read has been committed or dropped (a commit
    /// that fails ends the loop and drops what was asked for after it, and a page that is built again forgets what was waiting, see
    /// <see cref="Forget"/>), and nothing older can be committed after it. It never fails. A new Start (a resume from Pause or from a
    /// fault does not wait), Tap to start and closing (for at most its four seconds) wait for it.
    /// </summary>
    internal Task Settled => Task.WhenAll(_loop, _typed);

    /// <summary>
    /// Asks for a tempo (already within 1 to 1000 BPM and to a tenth, and not the one asked for last) and starts the loop that commits
    /// it, unless one is running, which then commits it when its current commit is done.
    /// </summary>
    /// <param name="shown">Called once the request is recorded and before anything is committed: the dial and the announcement follow it at once.</param>
    internal void Request(double bpm, Action? shown = null)
    {
        _requested = bpm;
        _requestedFor = _project();
        shown?.Invoke();
        if (_running) return;
        _running = true;
        _loop = _guard(async () =>
        {
            try
            {
                var committed = double.NaN;
                var committedFor = 0;
                // The same number asked for another project is another request, so both are compared.
                while (Requested is { } wanted && (wanted != committed || _requestedFor != committedFor))
                {
                    committed = wanted;
                    var asked = committedFor = _requestedFor;
                    bool StillTheProject() => asked == _project();
                    try { await _commit(wanted, StillTheProject); }
                    // The project it was asked for has been replaced: the commit left nothing behind, which is not an error. What was
                    // asked for after it (for the project that is there now) is still committed.
                    catch (OperationCanceledException) when (!StillTheProject()) { }
                }
            }
            finally
            {
                _running = false;
                _requested = null;
                _ended();
            }
        });
    }

    /// <summary>
    /// Runs the commit of a tempo that was typed (Done in the Tempo panel) once the typed tempos that were pressed before it have
    /// ended and no loop is running, so that it is committed after the nudges that were asked for until then, and not between two
    /// of the loop's commits. It is part of
    /// <see cref="Settled"/> from the moment it is called until it has ended, however it ends; what it throws goes to the caller.
    /// Called with the edit queue not held (the loop needs it to finish).
    /// </summary>
    /// <param name="typed">
    /// Starts the commit. It must take its place in the edit queue before it awaits anything, as the window's commit does (its first
    /// await is the queue's own wait; a locked project refuses the edit and queues nothing), so that on the UI thread, where nothing
    /// else runs in between, no nudge can be queued between the last look at the loop and the commit.
    /// </param>
    internal async Task AfterNudgesAsync(Func<Task> typed)
    {
        // The typed tempo that was pressed before this one, or a completed task. It is only ever completed, never failed, so waiting
        // for it is safe.
        var earlier = _typed;
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Registered before anything is awaited, so no waiter can pass it while it is still on its way. A typed tempo ends only after
        // the one before it has ended (it waits for it below), so the newest stands for all of them.
        _typed = ended.Task;
        try
        {
            // After the typed tempos that were pressed before this one, in the order they were pressed, whatever order the
            // continuations of their waits are run in.
            await earlier;
            // After every nudge that was asked for until the moment this commit is queued: the loop that is running, and one that
            // starts while this waits (a nudge made while an earlier typed tempo was being applied). Nothing is awaited between the
            // last look at the loop and the call that queues the commit (see the parameter), so no nudge can come in between.
            while (!_loop.IsCompleted) await _loop;
            await typed();
        }
        finally { ended.SetResult(); }
    }

    /// <summary>
    /// The page is built again: what was nudged to and not committed yet is dropped, and the loop ends after the commit that is running
    /// (that one still lands). A typed tempo that is waiting is not dropped: it still lands, or is refused when its project was replaced.
    /// </summary>
    internal void Forget() => _requested = null;
}
