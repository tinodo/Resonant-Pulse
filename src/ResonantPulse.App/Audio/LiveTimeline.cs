namespace ResonantPulse.App.Audio;

/// <summary>
/// What is audible, published atomically by the audio callback and read by Snapshot/OnFrameCompleted.
/// <c>Active</c> renders every frame from <c>SwitchFrame</c> on. While <c>Previous</c> is set, it renders everything
/// before <c>SwitchFrame</c> and its attacks from before that frame keep ringing (their tails) underneath Active.
/// <c>SwitchFrame</c> is an ABSOLUTE device frame; each revision converts it with its own Shift.
/// At most one transition is in flight: the next one starts only after Previous retired, i.e. once the
/// processed cursor passed <c>SwitchFrame</c> plus the click tail (<see cref="RetainedPlayback.HasProcessedTail"/>).
/// </summary>
internal sealed record LiveTimeline(PlaybackRevision Active, PlaybackRevision? Previous = null, long SwitchFrame = 0)
{
    internal LiveTimeline Switch(PlaybackRevision next, long absoluteFrame) => new(next, Active, absoluteFrame);

    internal LiveTimeline Retire() => Previous is null ? this : this with { Previous = null };

    /// <summary>The revision whose musical clock is audible at a processed (absolute) frame.</summary>
    internal PlaybackRevision PresentedAt(long processedFrame) =>
        Previous is not null && processedFrame < SwitchFrame ? Previous : Active;

    /// <summary>The absolute deadline governing <paramref name="completedFrames"/> frames having been processed.</summary>
    internal long? EndFrameAt(long completedFrames) =>
        (Previous is not null && completedFrames <= SwitchFrame ? Previous : Active).EndFrameAbsolute;
}

/// <summary>
/// A primary-pulse boundary of a revision. <c>Loop</c> 0 is the first body loop (inside Initial), loop n >= 1 is
/// Body cycle n; <c>Pulse</c> counts pulses (multiples of BeatUnitQuarters) from the loop start. <c>OwnFrame</c> is the
/// frame in the revision's own timeline, <c>Frame</c> the same instant as an absolute device frame.
/// </summary>
internal readonly record struct PulseBoundary(long Loop, int Pulse, long OwnFrame, long Frame);
