namespace ResonantPulse.App;

/// <summary>
/// The tempo that the person nudges to (a stepper, the wheel over the dial, TAP): it is shown at once and committed one value at a
/// time. While a commit runs, further requests only change the wanted value, and the one loop then commits the newest directly, so
/// holding a stepper or tapping quickly never queues a backlog, never reorders, and the last value asked for is the last one committed.
/// <para>
/// A tempo that is typed in the Tempo panel is not a nudge: it is an edit of its own, and it has to come after every nudge that is
/// still on its way. The edit queue is first in, first out, so an edit made while the loop is committing is queued between two of the
/// loop's commits, and the loop then goes on to commit its older value over it. What must come after the nudges (Done in the Tempo panel,
/// Start, Tap to start, closing) therefore waits for <see cref="Settled"/> first, which completes when the loop has committed the
/// newest value asked for and has nothing left to commit.
/// </para>
/// Pure and for the UI thread only, so every order the commits can finish in is tested without a window.
/// </summary>
internal sealed class TempoNudges
{
    private readonly Func<double, Task> _commit;
    private readonly Func<Func<Task>, Task> _guard;
    private readonly Action _ended;
    private bool _running;

    /// <param name="commit">Commits one tempo to the project (it waits in the edit queue, so it finishes later).</param>
    /// <param name="guard">Runs the loop and reports what goes wrong in it, without letting it escape (the window's error handling).</param>
    /// <param name="ended">Called when the loop has ended and nothing is on its way any more: the window shows the project's tempo again.</param>
    internal TempoNudges(Func<double, Task> commit, Func<Func<Task>, Task> guard, Action ended)
    {
        _commit = commit;
        _guard = guard;
        _ended = ended;
    }

    /// <summary>The newest tempo that was asked for and that the project does not hold yet; null while nothing is on its way.</summary>
    internal double? Requested { get; private set; }

    /// <summary>
    /// Completes when the loop that is running (or the last one that ran) has ended: every tempo asked for before the moment this is
    /// read has been committed or refused, and nothing older can be committed after it. Closing, Start and Tap to start wait for it.
    /// </summary>
    internal Task Settled { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Asks for a tempo (already within 1 to 1000 BPM and to a tenth, and not the one asked for last) and starts the loop that commits
    /// it, unless one is running, which then commits it when its current commit is done.
    /// </summary>
    /// <param name="shown">Called once the request is recorded and before anything is committed: the dial and the announcement follow it at once.</param>
    internal void Request(double bpm, Action? shown = null)
    {
        Requested = bpm;
        shown?.Invoke();
        if (_running) return;
        _running = true;
        Settled = _guard(async () =>
        {
            try
            {
                var committed = double.NaN;
                while (Requested is { } wanted && wanted != committed)
                {
                    committed = wanted;
                    await _commit(wanted);
                }
            }
            finally
            {
                _running = false;
                Requested = null;
                _ended();
            }
        });
    }

    /// <summary>
    /// The page is built again: what was asked for and not committed yet is dropped, and the loop ends after the commit that is running
    /// (that one still lands).
    /// </summary>
    internal void Forget() => Requested = null;
}
