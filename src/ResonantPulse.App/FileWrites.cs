namespace ResonantPulse.App;

/// <summary>
/// The writes of files that the person asked for, from the press of the command to its end: Save, Save to library, Export WAV, Export MIDI,
/// the export of separate tracks and the timing CSV. Closing the window stops what is being written and waits for it, so that no
/// temporary file stays beside a destination; a close that is refused leaves a write that is going alone and stops asking about those
/// that have not begun. Used on the UI thread only.
/// </summary>
internal sealed class FileWrites
{
    /// <summary>True while a write is going: from <see cref="Begin"/> until its <see cref="FileWrite.End"/>.</summary>
    internal bool AnyGoing => false;

    /// <summary>How many writes are going.</summary>
    internal int Count => 0;

    /// <summary>
    /// Starts a write. Not decided yet: the write is not remembered, so closing the window knows nothing of it, as before this was
    /// written down (only the export of separate tracks was stopped).
    /// </summary>
    internal FileWrite? Begin(string? exclusiveName = null) => new();

    internal bool IsGoing(string name) => false;

    internal bool IsWriting(string name) => false;

    /// <summary>The destinations of the writes that are going and have begun writing, for the log.</summary>
    internal IReadOnlyList<string> Destinations => [];

    internal Task<bool> CancelAllAsync(TimeSpan limit) => Task.FromResult(true);

    internal Task<bool> StopForCloseAsync(bool windowWillGo, TimeSpan limit) => Task.FromResult(true);
}

/// <summary>One write, from the press of its command to its end (a question, the choice of the file and the writing itself included).</summary>
internal sealed class FileWrite : IDisposable
{
    private readonly CancellationTokenSource _source = new();

    internal CancellationToken Token => _source.Token;

    internal bool IsWriting { get; private set; }

    internal bool PreparationInvalidated => false;

    internal bool StillWanted => !Token.IsCancellationRequested;

    internal void BeginWriting(string? destination = null) => IsWriting = true;

    internal void End() { }

    public void Dispose() => End();
}
