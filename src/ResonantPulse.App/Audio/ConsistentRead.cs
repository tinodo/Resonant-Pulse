namespace ResonantPulse.App.Audio;

/// <summary>
/// Pairs the processed-frame cursor with the timeline it belongs to. The audio thread publishes a splice, advances the
/// cursor and later retires the previous revision independently of the UI thread, so a cursor read first can be older
/// than a timeline read after it. The UI would then map a pre-splice frame onto the new revision (its count-in) instead
/// of showing the bar that was audible.
/// </summary>
internal static class ConsistentRead
{
    internal const int MaxAttempts = 8;

    /// <summary>
    /// Returns a timeline that was read while the returned cursor was still current, retrying while the cursor moves.
    /// Each retry follows an audio-thread step (one per quantum), so the bound is not reached in practice; if it is,
    /// the newest cursor is paired with the timeline read before it, which shows a position at most one step stale.
    /// </summary>
    internal static (long Cursor, T Timeline) Read<T>(Func<long> cursor, Func<T> timeline)
    {
        long before, after;
        T value;
        var attempt = 0;
        do
        {
            before = cursor();
            value = timeline();
            after = cursor();
        } while (before != after && ++attempt < MaxAttempts);
        return (after, value);
    }
}
