namespace ResonantPulse.App;

/// <summary>
/// The one export of separate tracks that may be going at a time, as the Library page sees it: whether the command can act, the
/// token that stops it, and a way for closing the window to stop it and wait until it has gone, so that no temporary archive is
/// left beside its destination. Used on the UI thread only.
/// </summary>
internal sealed class StemExportRun
{
    private CancellationTokenSource? _source;
    private TaskCompletionSource? _ended;

    /// <summary>True from <see cref="TryBegin"/> until <see cref="End"/>: while the question is open, while the file is chosen and while it is written.</summary>
    internal bool IsRunning => _source is not null;

    /// <summary>Starts a run. False, and no run, when one is going already.</summary>
    internal bool TryBegin(out CancellationToken token)
    {
        if (_source is not null)
        {
            token = default;
            return false;
        }
        _source = new CancellationTokenSource();
        _ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        token = _source.Token;
        return true;
    }

    /// <summary>Ends the run that began, however it went (done, refused, failed or cancelled). Does nothing when none is going.</summary>
    internal void End()
    {
        var source = _source;
        var ended = _ended;
        _source = null;
        _ended = null;
        source?.Dispose();
        ended?.TrySetResult();
    }

    /// <summary>
    /// Stops the run that is going and waits until it has ended, at most <paramref name="limit"/>. True when no run is going any
    /// more, false when it was still going when the limit passed (the caller goes on without it).
    /// </summary>
    internal async Task<bool> CancelAsync(TimeSpan limit)
    {
        var source = _source;
        var ended = _ended;
        if (source is null || ended is null) return true;
        try { source.Cancel(); }
        catch (ObjectDisposedException) { return true; }
        return await Task.WhenAny(ended.Task, Task.Delay(limit)).ConfigureAwait(true) == ended.Task;
    }
}
