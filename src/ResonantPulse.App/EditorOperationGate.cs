using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

internal enum EditorChangeKind
{
    Document,
    MasterGainOnly,
    GlobalTempoOnly,
    PracticeTimerOnly,
    SoundAndAccentsOnly
}

internal static class EditorRevisionPolicy
{
    // Only these global scalar changes leave every Studio form/index valid.
    // Lane volume and program-step tempo edits remain Document changes.
    private static bool PreservesDrafts(EditorChangeKind change) =>
        change is EditorChangeKind.MasterGainOnly or EditorChangeKind.GlobalTempoOnly or EditorChangeKind.PracticeTimerOnly;

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
