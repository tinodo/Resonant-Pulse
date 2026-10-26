using System.Numerics;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App.Audio;

/// <summary>Immutable, prepared off the real-time callback. No UI document is
/// shared with the renderer. Initial count-in is never repeated by the loop.</summary>
public sealed class PlaybackRevision
{
    public PulseDocument Document { get; }
    public RenderPlan Initial { get; }
    public RenderPlan Body { get; }
    public bool IsFinite => Document.Program.Count > 0;
    public long CountInFrames => Initial.CountInFrames;
    public long? EndFrame { get; }
    public bool HasPracticeLimit => PlaybackDeadline.IsEnabled(Document.Settings);

    public PlaybackRevision(PulseDocument source, int sampleRate)
    {
        Document = LocalWorkspace.Clone(source);
        var bars = LoopBars(Document);
        Initial = ScheduleCompiler.Compile(Document, sampleRate, bars, true);
        Body = ScheduleCompiler.Compile(Document, sampleRate, bars, false);
        if (Initial.TotalFrames <= 0 || Body.TotalFrames <= 0)
            throw new ArgumentException("The compiled rhythm has no duration.");
        EndFrame = PlaybackDeadline.Resolve(Initial, Body, Document.Settings, IsFinite);
        // Warm any lazily initialized renderer caches here, never in QuantumStarted.
        Span<float> warmup = stackalloc float[2];
        PcmRenderer.Fill(warmup, Initial, 0, 2, 0, false, false);
        PcmRenderer.Fill(warmup, Body, 0, 2, 0, false, false);
    }

    private static int LoopBars(PulseDocument document)
    {
        if (document.Program.Count > 0) return 16;
        var bar = new Rational(4 * document.Settings.BeatsPerBar, document.Settings.BeatDenominator);
        BigInteger multiple = 1;
        void Include(Rational cycle)
        {
            var required = (cycle / bar).Numerator;
            multiple = multiple / BigInteger.GreatestCommonDivisor(multiple, required) * required;
            if (multiple > 4096) throw new ArgumentException(Text.Get("CycleLimit"));
        }
        Include(Rational.FromDouble(document.Settings.BeatUnitQuarters));
        foreach (var lane in document.Lanes) Include(Rational.FromDouble(lane.CycleQuarters));
        var gaps = document.Settings.AudibleBars + document.Settings.SilentBars;
        if (gaps > 0) Include(bar * gaps);
        // A complete supercycle prevents a 3-quarter lane being restarted at
        // an arbitrary 16-bar seam. Deterministic realization repeats per loop.
        var count = (int)multiple;
        return count * Math.Max(1, (16 + count - 1) / count);
    }
}

internal sealed record RetainedPlayback(PlaybackRevision Active, PlaybackRevision? Previous,
    PlaybackRevision? Pending, long SwitchFrame, long ProcessedFrame)
{
    public RetainedPlayback Rebuild(int sampleRate)
    {
        var previousRate = Active.Initial.SampleRate;
        if (sampleRate == previousRate) return this;
        long ConvertFrame(long frame) => checked((long)Math.Round((double)frame * sampleRate / previousRate));
        return new(new PlaybackRevision(Active.Document, sampleRate),
            Previous is null ? null : new PlaybackRevision(Previous.Document, sampleRate),
            Pending is null ? null : new PlaybackRevision(Pending.Document, sampleRate),
            ConvertFrame(SwitchFrame), ConvertFrame(ProcessedFrame));
    }
}
