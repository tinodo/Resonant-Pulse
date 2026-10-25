namespace ResonantPulse.App;

/// <summary>
/// Rejects overlapping Stage requests instead of queuing stale selections.
/// Ownership includes validation, publication, selection bookkeeping and start.
/// </summary>
internal sealed class StageOperationGate
{
    private int _isBusy;

    public bool IsBusy => Volatile.Read(ref _isBusy) != 0;

    public async Task<bool> TryRunAsync(Func<Task> operation)
    {
        if (Interlocked.CompareExchange(ref _isBusy, 1, 0) != 0) return false;
        try
        {
            await operation();
            return true;
        }
        finally { Volatile.Write(ref _isBusy, 0); }
    }
}
