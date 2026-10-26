namespace ResonantPulse.App.Audio;

/// <summary>
/// Single-slot mailbox between the editing thread and the audio callback, plus the callback's arm/fire state machine.
/// <c>Post</c> may be called from any thread; newest request wins, an unapplied older one is simply replaced, so rapid
/// edits (+1 +1 +1, tap tempo) coalesce and never queue. Everything else runs in the callback only
/// (<see cref="Advance"/>, once per quantum): it classifies the request against the REAL active revision, applies
/// sound-only changes at the quantum boundary (same musical clock), and arms structural ones for the next pulse
/// boundary. The boundary is pinned when first computed and kept when a newer request replaces the armed one, so
/// only the cheap comparison against the quantum range repeats each quantum.
/// </summary>
internal sealed class LiveEditQueue
{
    private sealed record Armed(PlaybackRevision Revision, PulseBoundary? Boundary);

    private PlaybackRevision? _posted;
    private Armed? _armed;

    /// <summary>The newest request that has not been applied yet, for carrying it across an endpoint change.</summary>
    internal PlaybackRevision? Pending => Volatile.Read(ref _posted) ?? Volatile.Read(ref _armed)?.Revision;

    internal PulseBoundary? ArmedBoundary => Volatile.Read(ref _armed)?.Boundary;

    internal void Post(PlaybackRevision prepared) => Volatile.Write(ref _posted, prepared);

    internal void Clear()
    {
        Volatile.Write(ref _posted, null);
        Volatile.Write(ref _armed, null);
    }

    /// <summary>
    /// Returns the timeline to render the quantum [cursor, cursor + frameCount) with: retires a finished transition,
    /// takes the newest request, and splices at the armed pulse boundary when it lies inside the quantum.
    /// <paramref name="processed"/> is the completed (not rendered-ahead) absolute frame.
    /// </summary>
    internal LiveTimeline Advance(LiveTimeline timeline, long cursor, int frameCount, long processed)
    {
        if (timeline.Previous is { } previous &&
            RetainedPlayback.HasProcessedTail(processed, timeline.SwitchFrame, previous.SampleRate))
            timeline = timeline.Retire();
        // One transition at a time: requests wait in the mailbox (newest wins) until the old tail window has passed.
        if (timeline.Previous is not null) return timeline;
        if (Interlocked.Exchange(ref _posted, null) is { } posted) timeline = Accept(timeline, posted, cursor);
        return Volatile.Read(ref _armed) is { } armed ? Fire(timeline, armed, cursor, frameCount) : timeline;
    }

    private LiveTimeline Accept(LiveTimeline timeline, PlaybackRevision posted, long cursor)
    {
        var active = timeline.Active;
        switch (LiveSettingsEdits.Classify(active.Document.Settings, posted.Document.Settings))
        {
            case LiveChange.Sound:
                Volatile.Write(ref _armed, null);
                // Same musical clock (also for finite programs): identical Initial/Body geometry, so the switch
                // keeps Shift and deadline.
                return timeline.Switch(posted.Rebased(active.Shift, active.Deadline), cursor);
            case LiveChange.Structural when !active.IsFinite:
                Volatile.Write(ref _armed, new Armed(posted, Volatile.Read(ref _armed)?.Boundary));
                return timeline;
        }
        // Equal to what is playing, or structural for a finite program (never published): a still-armed change is superseded.
        Volatile.Write(ref _armed, null);
        return timeline;
    }

    private LiveTimeline Fire(LiveTimeline timeline, Armed armed, long cursor, int frameCount)
    {
        var active = timeline.Active;
        // The practice stop ends playback before any later boundary: keep the request as it is, nothing to recompute.
        if (active.EndFrameAbsolute is { } stop && cursor >= stop) return timeline;
        var boundary = armed.Boundary is { } pinned && pinned.Frame >= cursor ? pinned :
            LiveSplice.NextPulseBoundary(active, cursor);
        if (boundary is not { } at)
        {
            Volatile.Write(ref _armed, null);
            return timeline;
        }
        if (armed.Boundary != boundary) Volatile.Write(ref _armed, armed with { Boundary = boundary });
        // Not inside this quantum yet, or the practice stop comes first: stay armed.
        if (at.Frame >= cursor + frameCount || active.EndFrameAbsolute is { } end && at.Frame >= end) return timeline;
        var next = LiveSplice.Splice(active, armed.Revision, at);
        Volatile.Write(ref _armed, null);
        return timeline.Switch(next, at.Frame);
    }
}
