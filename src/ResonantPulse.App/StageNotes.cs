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
/// One asked-for save of a song's notes: the project and the song it is for, the text, and a number that tells it apart from
/// every other request, even one for the same song with the same text.
/// </summary>
internal readonly record struct NotesRequest(int Project, int Song, string Text, long Id);

/// <summary>
/// What the Stage notes box holds, for the song it shows, against what the project holds and what is on its way to being
/// saved. The box is a draft: it keeps what was typed until the project has it, and a save that failed leaves the draft in the
/// box and askable again. A request is finished by itself, never by another request that happens to carry the same text, and a
/// draft belongs to the project it was typed in.
/// </summary>
internal sealed class StageNotesDraft
{
    private long _lastId;
    private NotesRequest? _pending;
    private NotesRequest? _failed;

    /// <summary>The project the box belongs to (a number that changes when the project is replaced), or -1 for none.</summary>
    public int Project { get; private set; } = -1;

    /// <summary>The song the box shows, or -1 for none.</summary>
    public int Song { get; private set; } = -1;

    /// <summary>What the project holds as the notes of <see cref="Song"/>.</summary>
    public string Saved { get; private set; } = "";

    private bool IsHere(NotesRequest? request) => request is { } asked && asked.Project == Project && asked.Song == Song;

    /// <summary>The text the box is meant to hold: what is on its way to being saved, else what the project holds.</summary>
    private string Expected => IsHere(_pending) ? _pending!.Value.Text : Saved;

    /// <summary>
    /// What a page that has just been built shows for the song: a save still on its way, else a draft that could not be saved
    /// (it survives the page being rebuilt, and is saved again when asked for), else what the project holds.
    /// </summary>
    private string Shown => IsHere(_pending) ? _pending!.Value.Text : IsHere(_failed) ? _failed!.Value.Text : Saved;

    /// <summary>
    /// The page shows <paramref name="song"/> of <paramref name="project"/> (whose saved notes are <paramref name="saved"/>).
    /// Returns the text to put in the box, or null when the box already shows this song: its draft is kept, for instance when
    /// a selection that was refused is put back.
    /// </summary>
    public string? Show(int song, string saved, int project = 0)
    {
        if (song == Song && project == Project) return null;
        Song = song;
        Project = project;
        Saved = song >= 0 ? saved : "";
        return song >= 0 ? Shown : "";
    }

    /// <summary>The page is gone; the next <see cref="Show"/> loads from the project again.</summary>
    public void Reset() => Song = -1;

    /// <summary>True when <paramref name="text"/> is neither what the project holds nor what is already on its way.</summary>
    public bool NeedsSave(string text) => Song >= 0 && text != Expected;

    /// <summary>
    /// The box lost focus, or is about to be replaced, with <paramref name="text"/> in it. Returns the request that saves it, or
    /// null when there is nothing to save. A box that is back to what the project holds no longer wants a draft that failed
    /// before, so that draft is dropped rather than shown again later.
    /// </summary>
    public NotesRequest? Ask(string text)
    {
        if (NeedsSave(text)) return Request(text);
        if (Song >= 0 && !IsHere(_pending) && IsHere(_failed)) _failed = null;
        return null;
    }

    /// <summary>Notes that <paramref name="text"/> has been queued for saving, and returns the request that stands for it.</summary>
    public NotesRequest Request(string text)
    {
        var request = new NotesRequest(Project, Song, text, ++_lastId);
        _pending = request;
        // A newer request for the same song supersedes a draft that failed before it: asking for that draft again later would
        // put the older words on top of these.
        if (_failed is { } failed && failed.Project == request.Project && failed.Song == request.Song) _failed = null;
        return request;
    }

    /// <summary>The project now holds the text of <paramref name="request"/>.</summary>
    public void Completed(NotesRequest request)
    {
        if (request.Project == Project && request.Song == Song) Saved = request.Text;
        Release(request);
        // A save for the song worked, so a draft that failed before it is out of date.
        if (_failed is { } failed && failed.Project == request.Project && failed.Song == request.Song && failed.Id < request.Id)
            _failed = null;
    }

    /// <summary>
    /// The project did not take the text of <paramref name="request"/>. The draft stays in the box, is kept across a rebuild of
    /// the page, and is saved again when it is next asked for. A failure of a save that a newer one has replaced is not
    /// remembered.
    /// </summary>
    public void Failed(NotesRequest request)
    {
        if (_pending is { } pending && pending.Id == request.Id) _failed = request;
        Release(request);
    }

    /// <summary>
    /// The song or the project of <paramref name="request"/> is gone, so it can never be saved: it is dropped, and with it a
    /// draft that failed before it, so nothing is left asking for a save that cannot happen. The error was shown when it failed.
    /// </summary>
    public void Abandoned(NotesRequest request)
    {
        Release(request);
        if (_failed is { } failed && failed.Project == request.Project && failed.Song == request.Song && failed.Id <= request.Id)
            _failed = null;
    }

    /// <summary>True when a draft typed in <paramref name="project"/> could not be saved and has not been saved since.</summary>
    public bool HasFailed(int project) => _failed is { } failed && failed.Project == project;

    /// <summary>
    /// Asks again for a draft that could not be saved, when the page that showed it is gone (so there is no box to ask from).
    /// Null when there is none, or when it was typed in a project that has since been replaced: that draft is dropped.
    /// </summary>
    public NotesRequest? RetryFailed(int project)
    {
        if (_failed is not { } failed) return null;
        if (failed.Project != project)
        {
            _failed = null;
            return null;
        }
        var request = failed with { Id = ++_lastId };
        _pending = request;
        return request;
    }

    /// <summary>What Esc puts back: the notes as the project holds them, or as they are about to be. It also drops a failed draft.</summary>
    public string Restore()
    {
        if (IsHere(_failed)) _failed = null;
        return Expected;
    }

    // A newer request stays on its way when an older one finishes.
    private void Release(NotesRequest request)
    {
        if (_pending is { } pending && pending.Id == request.Id) _pending = null;
    }
}
