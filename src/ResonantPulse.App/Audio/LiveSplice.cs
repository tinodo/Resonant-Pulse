using ResonantPulse.Core;

namespace ResonantPulse.App.Audio;

/// <summary>Where the UI should place the playhead: the audible revision, its plan and the frame inside that plan.</summary>
internal readonly record struct PresentedPosition(PlaybackRevision Revision, RenderPlan Plan, long OwnFrame, long MusicalFrame);

/// <summary>
/// Pure arithmetic of live structural edits; <c>AudioTransport</c> only calls it. See <see cref="PlaybackRevision"/>
/// for the absolute/own timeline model.
/// <para>
/// <b>Boundary.</b> A splice happens at B = the first primary pulse (a multiple of BeatUnitQuarters from the start of
/// the loop) at or after the render cursor. Inside the count-in B is the start of bar 1 (the count-in is never replayed).
/// The first body loop lives inside <c>Initial</c> and is eligible like every later <c>Body</c> cycle.
/// </para>
/// <para>
/// <b>Phase.</b> With the same bar structure (meter, BPM unit and loop length) the rhythm CONTINUES: B keeps its loop
/// number and pulse index, so bar counter and accent phase carry on at the new tempo. Otherwise it RESTARTS: B becomes
/// bar 1 beat 1 of the new revision (its own <c>CountInFrames</c>), the bar counter starts again, and counted practice
/// limits (beats/bars) count from there. The wall-clock practice timer keeps its absolute frame either way.
/// </para>
/// <para>
/// <b>Mixing.</b> Frames before B come from the old revision, frames from B on from the new one plus the ringing tails
/// of old attacks that started before B; new attacks before B never sound. Both partitions are summed unclipped and
/// clamped once.
/// </para>
/// </summary>
internal static class LiveSplice
{
    private static SectionBoundary BodySection(RenderPlan plan) =>
        plan.SectionBoundaries.First(section => section.StepIndex >= 0);

    internal static Rational LoopQuarters(PlaybackRevision revision) => BodySection(revision.Body).LengthQuarters;

    private static int PulsesPerLoop(RenderPlan body, Rational unit) =>
        checked((int)(BodySection(body).LengthQuarters / unit).Ceiling());

    private static int FirstPulseAtOrAfter(RenderPlan plan, Rational unit, int pulses, long frame)
    {
        int low = 0, high = pulses;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (plan.FrameAtQuarter(unit * middle) < frame) low = middle + 1; else high = middle;
        }
        return low;
    }

    private static PulseBoundary Boundary(PlaybackRevision revision, long loop, int pulse, long ownFrame) =>
        new(loop, pulse, ownFrame, revision.ToAbsolute(ownFrame));

    /// <summary>
    /// The first primary pulse at or after the absolute <paramref name="cursor"/>, or null for finite programs.
    /// Allocates (rational arithmetic) and is meant to run once per queued edit.
    /// </summary>
    internal static PulseBoundary? NextPulseBoundary(PlaybackRevision revision, long cursor)
    {
        if (revision.IsFinite) return null;
        var initial = revision.Initial;
        var body = revision.Body;
        var unit = Rational.FromDouble(body.BeatUnitQuarters);
        var pulses = PulsesPerLoop(body, unit);
        var own = Math.Max(0, revision.ToOwn(cursor));
        if (own < initial.CountInFrames) return Boundary(revision, 0, 0, initial.CountInFrames);
        if (own < initial.TotalFrames)
        {
            var pulse = FirstPulseAtOrAfter(initial, unit, pulses, own);
            return pulse < pulses ? Boundary(revision, 0, pulse, initial.FrameAtQuarter(unit * pulse))
                : Boundary(revision, 1, 0, initial.TotalFrames);
        }
        var sinceBody = own - initial.TotalFrames;
        var cycle = sinceBody / body.TotalFrames;
        var origin = initial.TotalFrames + cycle * body.TotalFrames;
        var first = FirstPulseAtOrAfter(body, unit, pulses, sinceBody % body.TotalFrames);
        return first < pulses ? Boundary(revision, 1 + cycle, first, origin + body.FrameAtQuarter(unit * first))
            : Boundary(revision, 2 + cycle, 0, origin + body.TotalFrames);
    }

    /// <summary>True when bar, pulse grid and loop length are unchanged, so the bar position can carry over.</summary>
    internal static bool PreservesLoop(PlaybackRevision active, PlaybackRevision next)
    {
        var before = active.Document.Settings;
        var after = next.Document.Settings;
        return before.BeatsPerBar == after.BeatsPerBar && before.BeatDenominator == after.BeatDenominator &&
            before.BeatUnitQuarters == after.BeatUnitQuarters && LoopQuarters(active) == LoopQuarters(next);
    }

    /// <summary>Own frame of the same loop number and pulse index in <paramref name="next"/>.</summary>
    internal static long MapContinuity(PlaybackRevision next, PulseBoundary boundary)
    {
        var quarter = Rational.FromDouble(next.Body.BeatUnitQuarters) * boundary.Pulse;
        return boundary.Loop == 0
            ? next.Initial.FrameAtQuarter(quarter)
            : checked(next.Initial.TotalFrames + (boundary.Loop - 1) * next.Body.TotalFrames +
                next.Body.FrameAtQuarter(quarter));
    }

    /// <summary>Own frame of bar 1 beat 1 in <paramref name="next"/>: its count-in is skipped, never replayed.</summary>
    internal static long MapRestart(PlaybackRevision next) => next.CountInFrames;

    /// <summary>
    /// Places the prepared <paramref name="next"/> revision so that the absolute frame of
    /// <paramref name="boundary"/> maps onto the continuing/restarted musical position. The practice timer keeps
    /// its absolute frame; counted limits come from <paramref name="next"/>'s own plans.
    /// </summary>
    internal static PlaybackRevision Splice(PlaybackRevision active, PlaybackRevision next, PulseBoundary boundary)
    {
        var ownNext = PreservesLoop(active, next) ? MapContinuity(next, boundary) : MapRestart(next);
        var shift = checked(ownNext - boundary.Frame);
        long? timer = active.Deadline.Timer is { } current ? checked(current - active.Shift + shift) : null;
        return next.Rebased(shift, next.Deadline.WithTimer(timer));
    }

    /// <summary>
    /// Runs every branch of the structural-edit path (boundary search in the count-in, the first loop and a later loop;
    /// continuing and restarting splices; the queue state machine; the mixed render) on small throw-away revisions, so
    /// the first real edit never pays for JIT compilation or type set-up inside the audio callback. It touches nothing
    /// shared with a running transport and is independent of what the user happened to edit first.
    /// </summary>
    internal static void WarmUp()
    {
        const int rate = 8000, quantum = 512;
        var before = new PlaybackRevision(new PulseDocument(), rate);
        var faster = new PlaybackRevision(new PulseDocument { Settings = { Tempo = 150 } }, rate);
        var waltz = new PlaybackRevision(new PulseDocument { Settings = { BeatsPerBar = 3 } }, rate);
        var destination = new float[quantum * 2];
        var scratch = new float[quantum * 2];
        foreach (var cursor in new[] { 0L, before.CountInFrames + 1, before.Initial.TotalFrames + 1 })
            foreach (var next in new[] { faster, waltz })
            {
                if (NextPulseBoundary(before, cursor) is not { } boundary) continue;
                var start = Math.Max(0, boundary.Frame - quantum / 2);
                var queue = new LiveEditQueue();
                queue.Post(next);
                var timeline = queue.Advance(new LiveTimeline(before), start, quantum, start);
                Render(destination, start, quantum, timeline, 1f, false, scratch);
            }
    }
    /// <summary>
    /// What the UI should show for a processed (absolute) frame: the revision that is audible there, its own-timeline
    /// position (clamped at the deadline once completed) and the plan/frame pair inside that revision.
    /// </summary>
    internal static PresentedPosition Present(LiveTimeline timeline, long processed, bool completed)
    {
        var revision = timeline.PresentedAt(processed);
        var own = Math.Max(0, PlaybackDeadline.PositionFrame(revision.ToOwn(processed), revision.EndFrame, completed));
        if (own < revision.Initial.TotalFrames) return new(revision, revision.Initial, own, own);
        var plan = revision.Body;
        return new(revision, plan, own,
            revision.IsFinite ? plan.TotalFrames : (own - revision.Initial.TotalFrames) % plan.TotalFrames);
    }

    /// <summary>
    /// Fills <paramref name="destination"/> (interleaved stereo) with the absolute frames
    /// [<paramref name="cursor"/>, cursor + <paramref name="frameCount"/>) of <paramref name="timeline"/>; this is the whole
    /// of the callback's rendering. <paramref name="scratch"/> must hold <paramref name="frameCount"/> stereo frames.
    /// </summary>
    internal static void Render(Span<float> destination, long cursor, int frameCount, LiveTimeline timeline,
        float gain, bool muted, Span<float> scratch)
    {
        var samples = destination[..(frameCount * 2)];
        samples.Clear();
        var active = timeline.Active;
        if (timeline.Previous is not { } previous)
        {
            var rendered = RenderActive(samples, active, cursor, frameCount, gain, muted, null, true);
            FadeAtDeadline(samples[..(rendered * 2)], active, cursor);
            return;
        }
        var boundary = timeline.SwitchFrame;
        var before = (int)Math.Clamp(boundary - cursor, 0, frameCount);
        if (before > 0)
        {
            // Everything before the boundary is still the old revision's: it keeps its stop fade too, so a splice
            // never changes audio it had already committed to play. After the boundary the new deadline governs.
            var head = samples[..(before * 2)];
            RenderPieces(head, previous, cursor, before, gain, muted, null, null, true);
            FadeAtDeadline(head, previous, cursor);
        }
        var start = cursor + before;
        var count = RenderActive(samples[(before * 2)..], active, start, frameCount - before, gain, muted,
            boundary + active.Shift, false);
        if (count == 0) return;
        var tail = scratch[..(count * 2)];
        tail.Clear();
        RenderPieces(tail, previous, start, count, gain, muted, null, boundary + previous.Shift, false);
        var mixed = samples.Slice(before * 2, count * 2);
        for (var index = 0; index < mixed.Length; index++)
            mixed[index] = Math.Clamp(mixed[index] + tail[index], -1f, 1f);
        FadeAtDeadline(mixed, active, start);
    }

    private static int RenderActive(Span<float> destination, PlaybackRevision revision, long start, int frames,
        float gain, bool muted, long? attacksFromOwn, bool limitPeaks)
    {
        var count = PlaybackDeadline.RenderCount(revision.ToOwn(start), frames, revision.EndFrame);
        if (count > 0)
            RenderPieces(destination[..(count * 2)], revision, start, count, gain, muted, attacksFromOwn, null, limitPeaks);
        return count;
    }

    private static void FadeAtDeadline(Span<float> samples, PlaybackRevision revision, long start)
    {
        if (revision.HasPracticeLimit && revision.EndFrame is { } end)
            PlaybackDeadline.FadeAtEnd(samples, revision.ToOwn(start), revision.SampleRate, end);
    }

    /// <summary>
    /// Renders absolute frames [start, start + frames) of one revision, piecewise across its Initial/Body seam.
    /// Attack bounds are given as own frames and converted to each plan's render coordinate. Frames that fall before
    /// the revision's own timeline begins stay silent.
    /// </summary>
    private static void RenderPieces(Span<float> destination, PlaybackRevision revision, long start, int frames,
        float gain, bool muted, long? attacksFromOwn, long? attacksBeforeOwn, bool limitPeaks)
    {
        var written = 0;
        while (written < frames)
        {
            var own = revision.ToOwn(start + written);
            if (own < 0)
            {
                written += (int)Math.Min(-own, frames - written);
                continue;
            }
            var initial = own < revision.Initial.TotalFrames;
            if (!initial && revision.IsFinite) break;
            var plan = initial ? revision.Initial : revision.Body;
            var local = initial ? own : (own - revision.Initial.TotalFrames) % plan.TotalFrames;
            var count = (int)Math.Min(Math.Min(frames - written, plan.TotalFrames - local), 32768);
            // Looping body uses the renderer's seam-tail mixing: it is rendered one cycle late so the tails of the
            // previous cycle sound. Initial is a finite intro, rendered once and never recurring.
            var renderAt = initial ? local : own - revision.Initial.TotalFrames + plan.TotalFrames;
            var origin = initial ? 0 : revision.Initial.TotalFrames - plan.TotalFrames;
            PcmRenderer.Fill(destination.Slice(written * 2, count * 2), plan, renderAt, 2, gain, muted, !initial,
                attacksFromOwn is { } lower ? (long?)(lower - origin) : null,
                attacksBeforeOwn is { } upper ? (long?)(upper - origin) : null, limitPeaks);
            written += count;
        }
    }
}
