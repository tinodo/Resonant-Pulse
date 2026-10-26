using ResonantPulse.Core;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Audio;

/// <summary>
/// Independent stop conditions of one revision, all in that revision's own timeline
/// (see <see cref="PlaybackRevision"/>). The earliest present one ends playback.
/// <see cref="Timer"/> is the only wall-clock condition: a live splice re-places it so its
/// absolute device frame is unchanged. The musical conditions (<see cref="Beats"/>,
/// <see cref="Bars"/>, <see cref="Program"/>) always belong to the revision's own plans.
/// </summary>
internal readonly record struct DeadlineParts(long? Timer, long? Beats, long? Bars, long? Program)
{
    internal long? End => PlaybackDeadline.Earliest(PlaybackDeadline.Earliest(Timer, Program),
        PlaybackDeadline.Earliest(Beats, Bars));

    internal DeadlineParts WithTimer(long? timer) => this with { Timer = timer };
}

internal static class PlaybackDeadline
{
    internal static bool IsEnabled(PulseSettings settings) =>
        settings.PracticeSeconds > 0 || settings.PracticeBeats > 0 || settings.PracticeBars > 0;

    internal static long? Resolve(RenderPlan initial, RenderPlan body, PulseSettings settings, bool finite) =>
        ResolveParts(initial, body, settings, finite).End;

    /// <summary>
    /// Resolves every stop condition separately, in the plans' own timeline: the timer counts from the end
    /// of the count-in, counted beats/bars from the first body bar (the count-in is never counted).
    /// </summary>
    internal static DeadlineParts ResolveParts(RenderPlan initial, RenderPlan body, PulseSettings settings, bool finite)
    {
        if (settings.PracticeBeats is < 0 or > 6_400_000)
            throw new ArgumentOutOfRangeException(nameof(settings.PracticeBeats));
        if (settings.PracticeBars is < 0 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(settings.PracticeBars));
        var (timer, program) = TimerAndProgram(initial.SampleRate, initial.CountInFrames, settings.PracticeSeconds,
            finite ? initial.TotalFrames : null);
        return new(timer, CountedEnd(initial, body, settings.PracticeBeats, false, finite),
            CountedEnd(initial, body, settings.PracticeBars, true, finite), program);
    }

    internal static long? Earliest(long? first, long? second) =>
        first is { } a && second is { } b ? Math.Min(a, b) : first ?? second;

    private static long? CountedEnd(RenderPlan initial, RenderPlan body, int count, bool bars, bool finite)
    {
        if (count == 0) return null;
        long remaining = count;
        if (Boundary(initial, ref remaining, bars) is { } first) return first;
        if (finite) return initial.TotalFrames;
        var perLoop = body.SectionBoundaries.Where(section => section.StepIndex >= 0)
            .Sum(section => (long)section.Bars * (bars ? 1 : section.BeatsPerBar));
        var loops = (remaining - 1) / perLoop;
        remaining -= loops * perLoop;
        var offset = Boundary(body, ref remaining, bars) ??
            throw new InvalidOperationException(Text.Get("StopBoundaryError"));
        return checked(initial.TotalFrames + loops * body.TotalFrames + offset);
    }

    private static long? Boundary(RenderPlan plan, ref long remaining, bool bars)
    {
        foreach (var section in plan.SectionBoundaries)
        {
            if (section.StepIndex < 0) continue;
            var available = (long)section.Bars * (bars ? 1 : section.BeatsPerBar);
            if (remaining <= available)
            {
                // Written meter beats remain independent of BPM units and audible onsets.
                var unit = new Rational(4 * (bars ? section.BeatsPerBar : 1), section.BeatDenominator);
                return plan.FrameAtQuarter(section.StartQuarter + unit * remaining);
            }
            remaining -= available;
        }
        return null;
    }

    internal static long? Resolve(int sampleRate, long countInFrames, int practiceSeconds, long? programEnd)
    {
        var (timer, program) = TimerAndProgram(sampleRate, countInFrames, practiceSeconds, programEnd);
        return Earliest(timer, program);
    }

    private static (long? Timer, long? Program) TimerAndProgram(int sampleRate, long countInFrames,
        int practiceSeconds, long? programEnd)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegative(countInFrames);
        if (practiceSeconds is < 0 or > 86400)
            throw new ArgumentOutOfRangeException(nameof(practiceSeconds));
        if (programEnd is { } end && end <= countInFrames)
            throw new ArgumentOutOfRangeException(nameof(programEnd));
        long? timerEnd = practiceSeconds == 0 ? null :
            checked(countInFrames + (long)practiceSeconds * sampleRate);
        return (timerEnd, programEnd);
    }

    internal static int RenderCount(long startFrame, int requested, long? endFrame) =>
        endFrame is { } end ? (int)Math.Clamp(end - startFrame, 0L, requested) : requested;

    internal static long PositionFrame(long processed, long? endFrame, bool completed) =>
        completed && endFrame is { } end ? Math.Min(processed, end - 1) : processed;

    internal static void FadeAtEnd(Span<float> samples, long startFrame, int sampleRate, long endFrame)
    {
        var fadeFrames = Math.Max(2, sampleRate / 200);
        var first = (int)Math.Clamp(endFrame - fadeFrames - startFrame, 0L, samples.Length / 2);
        for (var frame = first; frame < samples.Length / 2; frame++)
        {
            var gain = (float)Math.Clamp((double)(endFrame - 1 - startFrame - frame) / (fadeFrames - 1), 0, 1);
            samples[frame * 2] *= gain;
            samples[frame * 2 + 1] *= gain;
        }
    }
}
