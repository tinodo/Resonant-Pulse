namespace ResonantPulse.App;

/// <summary>
/// Once a transaction publishes, its UI must follow it even if recovery I/O
/// fails. Rendering runs after releasing the edit lock, before propagating errors.
/// </summary>
internal static class DocumentPublication
{
    public static async Task RunAsync(SemaphoreSlim edits, Func<Action, Task> transaction, Action<bool> render)
    {
        var isPublished = false;
        Exception? failure = null;
        await edits.WaitAsync();
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
