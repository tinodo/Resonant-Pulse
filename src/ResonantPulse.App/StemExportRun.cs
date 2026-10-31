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
    private bool _writing;
    private bool _preparationInvalidated;

    /// <summary>True from <see cref="TryBegin"/> until <see cref="End"/>: while the question is open, while the file is chosen and while it is written.</summary>
    internal bool IsRunning => _source is not null;

    /// <summary>True from <see cref="BeginWriting"/> until <see cref="End"/>: the question was answered Continue, a file was chosen and the archive is being written.</summary>
    internal bool IsWriting => _source is not null && _writing;

    /// <summary>
    /// True when closing the window was refused before the run began writing. The run looks at it before it asks its question: a plan
    /// that was still being compiled when the close was attempted is not asked about afterwards (the person was closing the window).
    /// A question that was open was answered No by the close, and a save dialog that was open is left to the person.
    /// </summary>
    internal bool PreparationInvalidated => _source is not null && _preparationInvalidated;

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
        _writing = false;
        _preparationInvalidated = false;
        token = _source.Token;
        return true;
    }

    /// <summary>The archive is about to be written: from here a refused close leaves the run alone. Does nothing when no run is going.</summary>
    internal void BeginWriting()
    {
        if (_source is not null) _writing = true;
    }

    /// <summary>Ends the run that began, however it went (done, refused, failed or cancelled). Does nothing when none is going.</summary>
    internal void End()
    {
        var source = _source;
        var ended = _ended;
        _source = null;
        _ended = null;
        _writing = false;
        _preparationInvalidated = false;
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

    /// <summary>
    /// What closing the window does about a run. When the window will go, the run is stopped and waited for (see
    /// <see cref="CancelAsync"/>). When the close is refused (the project could not be saved, so the window stays) a run that is
    /// writing is left to go on, and one that is not yet writing is told that its preparation is void (see
    /// <see cref="PreparationInvalidated"/>): a plan that is still being compiled is not asked about afterwards. True when no run is
    /// going any more.
    /// </summary>
    internal Task<bool> StopForCloseAsync(bool windowWillGo, TimeSpan limit)
    {
        if (windowWillGo) return CancelAsync(limit);
        if (_source is not null && !_writing) _preparationInvalidated = true;
        return Task.FromResult(!IsRunning);
    }

    /// <summary>
    /// What the page does when the time of a transient notice (Saved, a restored session) has run out. Only an informational notice
    /// fades: a warning or an error stays until it is dismissed, so nothing replaces it. A transient notice can have replaced the one
    /// that says the tracks are being saved (that one has no time of its own), so while the archive is being written it comes back.
    /// </summary>
    internal FadedNotice WhenTransientNoticeFades(bool informational) =>
        !informational ? FadedNotice.Keep : IsWriting ? FadedNotice.ShowSaving : FadedNotice.Close;
}

/// <summary>What the page does with its notice when the time of a transient one has run out (<see cref="StemExportRun.WhenTransientNoticeFades"/>).</summary>
internal enum FadedNotice
{
    /// <summary>Nothing: a warning or an error stays until it is dismissed.</summary>
    Keep,
    /// <summary>The notice closes.</summary>
    Close,
    /// <summary>The notice that says the tracks are being saved comes back.</summary>
    ShowSaving
}
