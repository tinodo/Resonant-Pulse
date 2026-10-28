using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

internal enum EditorChangeKind
{
    Document,
    MasterGainOnly,
    GlobalTempoOnly,
    PracticeTimerOnly,
    SoundAndAccentsOnly,
    /// <summary>Play-page meter, tempo unit, groups and feel; Studio drafts are invalidated but playback may follow live.</summary>
    PlayStructure,
    /// <summary>
    /// One field changed in place (a song's notes; in Studio a lane's volume, a beat's strength, the seed). Nothing on the page
    /// was added, removed or reordered, so every control and index on it stays valid and the page is not rebuilt, not even when
    /// writing the recovery file fails: what is being typed meanwhile stays where it is. Never live: it is heard at the next
    /// Start, if it concerns playback at all.
    /// </summary>
    InPlaceField
}

internal static class EditorRevisionPolicy
{
    // Only these changes leave every Studio form/index valid: global scalars, and a field changed in place. A structural lane
    // edit (add, remove, duplicate, rotate, convert) and a program-step edit remain Document changes.
    private static bool PreservesDrafts(EditorChangeKind change) =>
        change is EditorChangeKind.MasterGainOnly or EditorChangeKind.GlobalTempoOnly or
            EditorChangeKind.PracticeTimerOnly or EditorChangeKind.SoundAndAccentsOnly or EditorChangeKind.InPlaceField;

    public static int Advance(int revision, EditorChangeKind change = EditorChangeKind.Document) =>
        PreservesDrafts(change) ? revision : unchecked(revision + 1);

    public static bool ShouldRebuild(bool recoverySucceeded, EditorChangeKind change) =>
        !recoverySucceeded && !PreservesDrafts(change);
}

/// <summary>
/// Couples an editor command to the content revision its controls describe.
/// Overlap is rejected; late events from replaced pages are also rejected.
/// </summary>
internal sealed class EditorOperationGate
{
    private readonly StageOperationGate _operations = new();

    public bool IsBusy => _operations.IsBusy;

    public Task<bool> TryRunAsync(int expectedRevision, Func<int> currentRevision, Func<Task> operation) =>
        _operations.TryRunAsync(async () =>
        {
            EnsureRevision(expectedRevision, currentRevision());
            await operation();
        });

    public static void EnsureRevision(int expectedRevision, int currentRevision)
    {
        if (expectedRevision != currentRevision) throw new EditorRevisionChangedException();
    }
}

internal sealed class EditorRevisionChangedException : InvalidOperationException
{
    public EditorRevisionChangedException() : base(Text.Get("EditorChanged")) { }
}
