using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

internal sealed record LiveStep(long Cursor, int Frames, LiveTimeline Timeline);

/// <summary>
/// Drives the same two calls as <c>AudioTransport.FillQuantum</c> (queue Advance, then LiveSplice.Render) over a whole
/// stream, so splices can be heard in PCM without AudioGraph. The processed cursor lags the render cursor like the
/// graph's completion callback does.
/// </summary>
internal sealed class LiveRun
{
    private readonly float[] _scratch = new float[32768 * 2];
    private readonly float _gain;
    private readonly long _lag;

    internal LiveRun(PlaybackRevision first, long totalFrames, float gain = 0.5f, long lag = 0)
    {
        Timeline = new LiveTimeline(first);
        Pcm = new float[checked((int)(totalFrames * 2))];
        _gain = gain;
        _lag = lag;
    }

    internal LiveEditQueue Queue { get; } = new();
    internal LiveTimeline Timeline { get; private set; }
    internal long Cursor { get; private set; }
    internal List<LiveStep> Steps { get; } = [];
    internal float[] Pcm { get; }

    internal void RunTo(long frame, int quantum)
    {
        while (Cursor < frame)
        {
            var count = (int)Math.Min(quantum, frame - Cursor);
            Timeline = Queue.Advance(Timeline, Cursor, count, Math.Max(0, Cursor - _lag));
            LiveSplice.Render(Pcm.AsSpan(checked((int)(Cursor * 2)), count * 2), Cursor, count, Timeline, _gain, false, _scratch);
            Steps.Add(new(Cursor, count, Timeline));
            Cursor += count;
        }
    }

    /// <summary>The first quantum whose timeline carries a transition, or null.</summary>
    internal LiveStep? FirstTransition() => Steps.FirstOrDefault(step => step.Timeline.Previous is not null);

    /// <summary>The timeline the quantum rendering <paramref name="frame"/> used.</summary>
    internal LiveTimeline TimelineAt(long frame) => Steps.Last(step => step.Cursor <= frame).Timeline;

    internal int TransitionCount()
    {
        var count = 0;
        LiveTimeline? last = null;
        foreach (var step in Steps)
        {
            if (step.Timeline.Previous is not null && !ReferenceEquals(step.Timeline.Active, last?.Active)) count++;
            last = step.Timeline;
        }
        return count;
    }
}

internal static class LiveTestKit
{
    internal static PulseDocument Document(double tempo = 120, int beats = 4, int denominator = 4,
        double unit = 1, int countIn = 1, Action<PulseSettings>? edit = null)
    {
        var document = new PulseDocument
        {
            Settings = new()
            {
                Tempo = tempo, BeatsPerBar = beats, BeatDenominator = denominator, BeatUnitQuarters = unit,
                CountInBars = countIn
            }
        };
        edit?.Invoke(document.Settings);
        return document;
    }

    internal static PlaybackRevision Revision(PulseDocument document, int rate = 48000) => new(document, rate);

    /// <summary>The settings the editor would send: a clone carrying every live setting of <paramref name="document"/>.</summary>
    internal static PulseSettings Requested(PulseDocument document) => document.Settings with
    {
        Accents = document.Settings.Accents.ToArray(),
        Groups = document.Settings.Groups.ToArray(),
        ManualAccentIndices = document.Settings.ManualAccentIndices?.ToArray()
    };

    /// <summary>Primary-pulse attack frames of a plan in its own timeline, restricted to [from, before).</summary>
    internal static long[] PrimaryAttacks(RenderPlan plan, long from = 0, long before = long.MaxValue) =>
        plan.Onsets.Where(onset => onset.LaneIndex == -1 && onset.Frame >= from && onset.Frame < before)
            .Select(onset => onset.Frame).ToArray();

    internal static Accent AccentAt(RenderPlan plan, long ownFrame) =>
        plan.Onsets.Single(onset => onset.LaneIndex == -1 && onset.Frame == ownFrame).Accent;

    /// <summary>
    /// One revision's contribution over absolute frames [from, from + frames): its own timeline is
    /// absolute + shift, rendered with the attack bounds given in own frames. Independent of LiveSplice.
    /// </summary>
    internal static float[] Partition(RenderPlan plan, long shift, long from, long frames, float gain,
        long? attacksFromOwn = null, long? attacksBeforeOwn = null)
    {
        var result = new float[checked((int)(frames * 2))];
        for (long done = 0; done < frames; done += 32768)
        {
            var count = (int)Math.Min(32768, frames - done);
            var own = from + done + shift;
            // Frames before the revision's own timeline begins stay silent.
            var skip = (int)Math.Clamp(-own, 0, count);
            if (skip == count) continue;
            var chunk = new float[(count - skip) * 2];
            PcmRenderer.Fill(chunk, plan, own + skip, 2, gain, false, false, attacksFromOwn, attacksBeforeOwn, false);
            Array.Copy(chunk, 0, result, (done + skip) * 2, chunk.Length);
        }
        return result;
    }

    internal static float[] Sum(float[] first, float[] second)
    {
        var result = new float[first.Length];
        for (var index = 0; index < result.Length; index++) result[index] = Math.Clamp(first[index] + second[index], -1f, 1f);
        return result;
    }

    internal static void AssertSamePcm(float[] expected, float[] actual, long frames, string label)
    {
        for (var index = 0; index < frames * 2; index++)
            if (expected[index] != actual[index])
                Assert.Fail($"{label}: first difference at frame {index / 2} channel {index % 2}: expected {expected[index]}, actual {actual[index]}.");
    }

    /// <summary>What RefreshSnapshot shows: 1-based bar (loop offset included) and beat.</summary>
    internal static (long Bar, int Beat) BarAndBeat(PresentedPosition presented)
    {
        long offset = 0;
        var revision = presented.Revision;
        if (!revision.IsFinite && presented.OwnFrame >= revision.Initial.TotalFrames)
        {
            var barsPerLoop = revision.Body.SectionBoundaries.Sum(section => section.Bars);
            offset = (1 + (presented.OwnFrame - revision.Initial.TotalFrames) / revision.Body.TotalFrames) * barsPerLoop;
        }
        var position = presented.Plan.PositionAtFrame(Math.Clamp(presented.MusicalFrame, 0, presented.Plan.TotalFrames - 1));
        return (offset + position.BarIndex + 1, position.BeatIndex + 1);
    }
}
