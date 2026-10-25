using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

/// <summary>
/// Couples an editor command to the document revision its controls describe.
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
