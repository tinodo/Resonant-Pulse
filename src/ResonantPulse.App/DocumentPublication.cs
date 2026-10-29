namespace ResonantPulse.App;

/// <summary>
/// Once a transaction publishes, its UI must follow it even if recovery I/O
/// fails. Rendering runs after releasing the edit lock, before propagating errors.
/// </summary>
internal static class DocumentPublication
{
    /// <param name="cancel">
    /// Ends the wait for the edit lock without taking it, for something that waits there and is no longer wanted. Once the lock
    /// is held the transaction runs to its end: what it publishes is decided inside it.
    /// </param>
    public static async Task RunAsync(SemaphoreSlim edits, Func<Action, Task> transaction, Action<bool> render,
        CancellationToken cancel = default)
    {
        var isPublished = false;
        Exception? failure = null;
        await edits.WaitAsync(cancel);
        try { await transaction(() => isPublished = true); }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            edits.Release();
            if (isPublished)
            {
                try { render(failure is null); }
                catch (Exception renderingFailure) when (failure is not null)
                {
                    // Keep the original persistence failure first for both
                    // localized error reporting and full technical diagnostics.
                    throw new AggregateException(failure, renderingFailure);
                }
            }
        }
    }
}
