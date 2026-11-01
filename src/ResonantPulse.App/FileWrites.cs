namespace ResonantPulse.App;

/// <summary>
/// The writes of files that the person asked for, from the press of the command to its end: Save, Save to library, Export WAV, Export MIDI,
/// the export of separate tracks and the timing CSV. They are written atomically (to a temporary file beside the destination, which is
/// then moved over it), so a write that is going when the window closes would leave that temporary file behind: closing the window stops
/// what is being written and waits for it, at most a limit, and the file that was at the destination stays as it was. A close that is
/// refused (the project could not be saved, so the window stays) leaves a write that is writing alone, and tells one that is not writing
/// yet (it is still being prepared: a plan is compiled, notes are saved, a question is waiting) that the person was closing the window, so
/// that nothing is asked about it afterwards. One guard for all of them: each of them has the same questions to answer when the window
/// closes. Used on the UI thread only.
/// </summary>
internal sealed class FileWrites
{
    private readonly List<FileWrite> _going = [];

    /// <summary>True once the window has begun to stop the writes because it is going: from then on a write is cancelled as it begins.</summary>
    private bool _stopping;

    /// <summary>
    /// Told about an error that the preparation of a command that is being dropped ended with (see <see cref="FileWrite.PrepareAsync{T}"/>).
    /// The person is told nothing, because the error would replace the notice that says why the window stayed; the owner keeps the cause in
    /// the log, so that no error goes unreported.
    /// </summary>
    internal Action<Exception>? DroppedError { get; init; }

    /// <summary>True while a write is going: from <see cref="Begin"/> until its <see cref="FileWrite.End"/>.</summary>
    internal bool AnyGoing => _going.Count > 0;

    /// <summary>How many writes are going.</summary>
    internal int Count => _going.Count;

    /// <summary>
    /// Starts a write. Writes of different commands (and two presses of the same one) may overlap, as they always could: none is refused
    /// or made to wait here.
    /// </summary>
    internal FileWrite Begin() => Add(null);

    /// <summary>
    /// Starts a write of which there can be only one at a time, called <paramref name="name"/> (the export of separate tracks: its
    /// command is off while it goes). Null, and no write, when one of that name is going already.
    /// </summary>
    internal FileWrite? TryBeginExclusive(string name) => IsGoing(name) ? null : Add(name);

    /// <summary>
    /// Runs a write whose file is saved after a preparation that takes time (the copy of the project that a replacement saves to the
    /// library is saved after the new project has been validated). The write begins before the preparation and goes on until the save has
    /// ended, however it ends, so that a close that comes at any moment in between sees it: it cancels it and waits for it. Were it registered
    /// only when the save began, it would not be among the writes that the close has already counted.
    /// </summary>
    internal async Task RunAsync(Func<Task> prepare, Func<FileWrite, Task> save)
    {
        using var write = Begin();
        await prepare();
        await save(write);
    }

    private FileWrite Add(string? name)
    {
        var write = new FileWrite(this, name);
        _going.Add(write);
        // A command that was still on its way when the window began to close (queued behind an edit, or being prepared) begins its write
        // after the close has counted the writes that are going: nothing would cancel it, and the window would go while it saved.
        if (_stopping) write.Cancel();
        return write;
    }

    /// <summary>True while a write called <paramref name="name"/> is going (from its begin until its end).</summary>
    internal bool IsGoing(string name) => _going.Any(write => write.Name == name);

    /// <summary>True while a write called <paramref name="name"/> is being written (from <see cref="FileWrite.BeginWriting"/> until its end).</summary>
    internal bool IsWriting(string name) => _going.Any(write => write.Name == name && write.IsWriting);

    /// <summary>The destinations of the writes that are going and have begun writing a file, for the log.</summary>
    internal IReadOnlyList<string> Destinations => _going.Select(write => write.Destination).OfType<string>().ToArray();

    /// <summary>
    /// Stops every write that is going and waits until all of them have ended, at most <paramref name="limit"/>. True when no write is going
    /// any more, false when one was still going when the limit passed (the caller goes on without it). The window is going: a write that
    /// begins from now on is cancelled as it begins.
    /// </summary>
    internal async Task<bool> CancelAllAsync(TimeSpan limit)
    {
        _stopping = true;
        var timeout = Task.Delay(limit);
        // A write that begins while the close waits is cancelled as it begins (see Add) and is waited for too: the loop looks again. Only a
        // write that has not ended counts, so that the loop cannot go round without ever waiting.
        while (true)
        {
            var going = _going.Where(write => !write.Ended.IsCompleted).ToArray();
            if (going.Length == 0) return true;
            foreach (var write in going) write.Cancel();
            var ended = Task.WhenAll(going.Select(write => write.Ended));
            if (await Task.WhenAny(ended, timeout).ConfigureAwait(true) != ended) return false;
        }
    }

    /// <summary>
    /// What closing the window does about the writes that are going. When the window will go, they are stopped and waited for (see
    /// <see cref="CancelAllAsync"/>). When the close is refused, a write that is writing is left to go on, and one that is not writing yet
    /// is told that its preparation is void (see <see cref="FileWrite.PreparationInvalidated"/>): a plan that is still being compiled is not
    /// asked about afterwards. True when no write is going any more.
    /// </summary>
    internal Task<bool> StopForCloseAsync(bool windowWillGo, TimeSpan limit)
    {
        if (windowWillGo) return CancelAllAsync(limit);
        foreach (var write in _going)
            if (!write.IsWriting) write.InvalidatePreparation();
        return Task.FromResult(!AnyGoing);
    }

    /// <summary>Called by a write when it has ended: it is not going any more.</summary>
    internal void Forget(FileWrite write) => _going.Remove(write);
}

/// <summary>One write of a file, from the press of its command to its end. Dispose it (or call <see cref="End"/>) however the write goes.</summary>
internal sealed class FileWrite : IDisposable
{
    private readonly FileWrites _owner;
    private readonly CancellationTokenSource _source = new();
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isEnded;

    internal FileWrite(FileWrites owner, string? name)
    {
        _owner = owner;
        Name = name;
        Token = _source.Token;
    }

    /// <summary>The name of a write of which there can be only one at a time, or null.</summary>
    internal string? Name { get; }

    /// <summary>Cancelled when the window closes while the write is going. The write passes it to what writes the file.</summary>
    internal CancellationToken Token { get; }

    /// <summary>True from <see cref="BeginWriting"/> until the write ends: the question was answered, a file was chosen and it is being written.</summary>
    internal bool IsWriting { get; private set; }

    /// <summary>The file being written, once known (for the log: a temporary file may be left beside it).</summary>
    internal string? Destination { get; private set; }

    /// <summary>
    /// True when closing the window was refused before the write began writing. The write looks at it before it asks its question or opens
    /// its dialog: a plan that was still being compiled when the close was attempted is not asked about afterwards (the person was closing
    /// the window). A question that was open was answered No by the close, and a save dialog that was open is left to the person.
    /// </summary>
    internal bool PreparationInvalidated { get; private set; }

    /// <summary>
    /// Whether the person is still waiting for this write: no close was refused while it was being prepared, and the window is not going.
    /// The write looks at it right before it asks a question or opens a dialog.
    /// </summary>
    internal bool StillWanted => !PreparationInvalidated && !Token.IsCancellationRequested;

    /// <summary>Completes when the write has ended, however it went (done, refused, failed or cancelled).</summary>
    internal Task Ended => _ended.Task;

    /// <summary>
    /// Runs the preparation of a write: a file is looked at, a plan is compiled, notes are saved, before the write asks its question or opens
    /// its dialog. <c>Wanted</c> is false when, by the time the preparation ends, a close was refused meanwhile or the window is going: the
    /// person was closing the window, so the command ends silently, whether the preparation went well or failed (an error of the
    /// preparation is not shown either, it would replace the notice that says why the window stayed; it is given to
    /// <see cref="FileWrites.DroppedError"/> so that the log keeps it, except a cancellation, which is how the window that goes stops a
    /// preparation). An error of a preparation that is still wanted goes to the caller.
    /// </summary>
    internal async Task<(bool Wanted, T Result)> PrepareAsync<T>(Func<Task<T>> prepare)
    {
        try
        {
            var result = await prepare();
            return (StillWanted, result);
        }
        catch (Exception error) when (!StillWanted)
        {
            if (error is not OperationCanceledException) _owner.DroppedError?.Invoke(error);
            return (false, default!);
        }
    }

    /// <summary>
    /// Everything the person was asked is answered and the file is chosen: from here a refused close leaves the write alone (what remains
    /// is compiling a plan or writing the file). <paramref name="destination"/> is named in the log if the write does not stop: the file,
    /// or the one that is about to be made.
    /// </summary>
    internal void BeginWriting(string? destination = null)
    {
        if (_isEnded) return;
        IsWriting = true;
        Destination = destination;
    }

    /// <summary>
    /// Ends the write, however it went. Harmless when it has ended already. Call it only after whatever uses <see cref="Token"/> has
    /// returned: the source of the token is disposed here.
    /// </summary>
    internal void End()
    {
        if (_isEnded) return;
        _isEnded = true;
        IsWriting = false;
        _owner.Forget(this);
        _source.Dispose();
        _ended.TrySetResult();
    }

    public void Dispose() => End();

    internal void Cancel()
    {
        if (_isEnded) return;
        try { _source.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    internal void InvalidatePreparation()
    {
        if (!_isEnded && !IsWriting) PreparationInvalidated = true;
    }
}
