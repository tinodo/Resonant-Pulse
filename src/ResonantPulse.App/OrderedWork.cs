namespace ResonantPulse.App;

/// <summary>
/// Runs asynchronous work one item at a time, in the order it was queued. A failure belongs to its own item: it never blocks
/// the work queued after it. Used so a second edit can never overtake or be undone by the first, however slowly that one saves.
/// Call it from one thread (the UI thread); each item resumes on the context that queued it.
/// </summary>
internal sealed class OrderedWork
{
    private Task _tail = Task.CompletedTask;

    internal Task Run(Func<Task> work)
    {
        var current = RunAfter(_tail, work);
        _tail = current;
        return current;
    }

    private static async Task RunAfter(Task previous, Func<Task> work)
    {
        try { await previous; }
        catch { /* the earlier item reports its own failure */ }
        await work();
    }
}
