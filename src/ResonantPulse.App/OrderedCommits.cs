namespace ResonantPulse.App;

/// <summary>
/// The commit contract for one editable value (docs/design/pages-design.md). Edits are applied one at a time, in the order
/// they were made, however slowly an earlier one saves. An edit that is empty, invalid or refused leaves the value as it was,
/// and the field is put back to the last value that was really committed, but only when no newer edit is waiting: the newer
/// edit then decides what the field shows, so an older failure can never undo a newer success.
/// One instance belongs to the setting, not to a field: a page that is rebuilt while a save is still pending attaches its new
/// field to the same instance, so the old queue can never overwrite a newer edit and the new field shows what was saved.
/// Call it from one thread (the UI thread).
/// </summary>
internal sealed class OrderedCommits
{
    private readonly OrderedWork _queue = new();
    private Action<double>? _show;
    private long _latest;

    internal OrderedCommits(double committed) => Committed = committed;

    /// <summary>The value of the latest edit that was committed successfully.</summary>
    internal double Committed { get; private set; }

    /// <summary>Binds the field that shows this value. A rebuilt page binds its new field; the old one is forgotten.</summary>
    internal void Attach(Action<double> show) => _show = show;

    /// <summary>
    /// Queues an edit. <paramref name="commit"/> says whether it was accepted (and is never called for a value that is not a
    /// finite number). A rejected edit, or one that throws, puts the bound field back to <see cref="Committed"/>.
    /// </summary>
    internal Task Propose(double value, Func<double, Task<bool>> commit)
    {
        var version = ++_latest;
        return _queue.Run(async () =>
        {
            var applied = false;
            try { applied = double.IsFinite(value) && await commit(value); }
            finally
            {
                if (applied) Committed = value;
                else if (version == _latest) _show?.Invoke(Committed);
            }
        });
    }
}
