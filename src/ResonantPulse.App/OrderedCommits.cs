namespace ResonantPulse.App;

/// <summary>
/// The commit contract for one editable value (docs/design/pages-design.md). Edits are applied one at a time, in the order
/// they were made, however slowly an earlier one saves. An edit that is empty, invalid or refused leaves the value as it was,
/// and the field is put back to the last value that was really committed, but only when no newer edit is waiting: the newer
/// edit then decides what the field shows, so an older failure can never undo a newer success.
/// Call it from one thread (the UI thread).
/// </summary>
internal sealed class OrderedCommits
{
    private readonly OrderedWork _queue = new();
    private long _latest;

    internal OrderedCommits(double committed) => Committed = committed;

    /// <summary>The value of the latest edit that was committed successfully.</summary>
    internal double Committed { get; private set; }

    /// <summary>
    /// Queues an edit. <paramref name="commit"/> says whether it was accepted (and is never called for a value that is not a
    /// finite number); <paramref name="revert"/> puts the field back to <see cref="Committed"/>.
    /// </summary>
    internal Task Propose(double value, Func<double, Task<bool>> commit, Action revert)
    {
        var version = ++_latest;
        return _queue.Run(async () =>
        {
            var applied = false;
            try { applied = double.IsFinite(value) && await commit(value); }
            finally
            {
                if (applied) Committed = value;
                else if (version == _latest) revert();
            }
        });
    }
}
