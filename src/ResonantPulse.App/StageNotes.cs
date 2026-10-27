namespace ResonantPulse.App;

/// <summary>
/// Saves that land in the order they were asked for. Each one runs after the one before it has finished, whether that
/// succeeded or not: a failure belongs to the save that failed (it reports it), and is never a reason to skip a later save.
/// Used from the UI thread only, so nothing is locked.
/// </summary>
internal sealed class SaveQueue
{
    private Task _tail = Task.CompletedTask;

    /// <summary>Asks for <paramref name="save"/> to run once every save asked for before it has finished.</summary>
    public Task Enqueue(Func<Task> save)
    {
        var previous = _tail;
        async Task RunAfterAsync()
        {
            try { await previous; }
            catch (Exception) { /* the earlier save reported its own failure */ }
            await save();
        }
        return _tail = RunAfterAsync();
    }

    /// <summary>Completes when every save asked for so far has finished. Never throws: failures are the saves' own.</summary>
    public async Task WhenIdleAsync()
    {
        try { await _tail; }
        catch (Exception) { /* reported by the save that failed */ }
    }
}

/// <summary>
/// What the Stage notes box holds, for the song it shows, against what the project holds and what is on its way to being
/// saved. The box is a draft: it keeps what was typed until the project has it, and a save that failed leaves the draft in the
/// box and askable again.
/// </summary>
internal sealed class StageNotesDraft
{
    private int _pendingSong = -1;
    private string? _pending;
    private int _failedSong = -1;
    private string? _failed;

    /// <summary>The song the box shows, or -1 for none.</summary>
    public int Song { get; private set; } = -1;

    /// <summary>What the project holds as the notes of <see cref="Song"/>.</summary>
    public string Saved { get; private set; } = "";

    /// <summary>The text the box is meant to hold: what is on its way to being saved, else what the project holds.</summary>
    private string Expected => _pendingSong == Song && _pending is not null ? _pending : Saved;

    /// <summary>
    /// What a page that has just been built shows for the song: a save still on its way, else a draft that could not be saved
    /// (it survives the page being rebuilt, and is saved again when asked for), else what the project holds.
    /// </summary>
    private string Shown =>
        _pendingSong == Song && _pending is not null ? _pending
        : _failedSong == Song && _failed is not null ? _failed
        : Saved;

    /// <summary>
    /// The page shows <paramref name="song"/> (whose saved notes are <paramref name="saved"/>). Returns the text to put in the box,
    /// or null when the box already shows this song: its draft is kept, for instance when a selection that was refused is
    /// put back.
    /// </summary>
    public string? Show(int song, string saved)
    {
        if (song == Song) return null;
        Song = song;
        Saved = song >= 0 ? saved : "";
        return song >= 0 ? Shown : "";
    }

    /// <summary>The page is gone; the next <see cref="Show"/> loads from the project again.</summary>
    public void Reset() => Song = -1;

    /// <summary>True when <paramref name="text"/> is neither what the project holds nor what is already on its way.</summary>
    public bool NeedsSave(string text) => Song >= 0 && text != Expected;

    /// <summary>Notes that <paramref name="text"/> has been queued for saving. Returns the song it belongs to.</summary>
    public int Request(string text)
    {
        _pendingSong = Song;
        _pending = text;
        return Song;
    }

    /// <summary>The project now holds <paramref name="text"/> as the notes of <paramref name="song"/>.</summary>
    public void Completed(int song, string text)
    {
        if (Song == song) Saved = text;
        Release(song, text);
        // A save for the song worked, so a draft that failed before it is out of date.
        if (_failedSong == song) ClearFailed();
    }

    /// <summary>
    /// The project did not take <paramref name="text"/>. The draft stays in the box, is kept across a rebuild of the page, and is
    /// saved again when it is next asked for. A failure of a save that a newer one has replaced is not remembered.
    /// </summary>
    public void Failed(int song, string text)
    {
        if (_pendingSong == song && _pending == text)
        {
            _failedSong = song;
            _failed = text;
        }
        Release(song, text);
    }

    /// <summary>What Esc puts back: the notes as the project holds them, or as they are about to be. It also drops a failed draft.</summary>
    public string Restore()
    {
        if (_failedSong == Song) ClearFailed();
        return Expected;
    }

    private void ClearFailed()
    {
        _failedSong = -1;
        _failed = null;
    }

    // A newer request for the song stays on its way when an older one finishes.
    private void Release(int song, string text)
    {
        if (_pendingSong != song || _pending != text) return;
        _pendingSong = -1;
        _pending = null;
    }
}
