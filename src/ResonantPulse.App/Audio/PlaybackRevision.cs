using System.Numerics;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App.Audio;

/// <summary>
/// Immutable, prepared off the real-time callback. No UI document is shared with the renderer.
/// Initial count-in is never repeated by the loop.
/// <para>
/// <b>Two timelines.</b> ABSOLUTE frames are device frames since the stream started: the transport cursors
/// (<c>RenderFrame</c>, <c>ProcessedFrame</c>), <c>AudioFrame</c> times and elapsed time. They never rewind, and a
/// live edit never changes them. A revision's OWN frames are the timeline of its compiled plans:
/// <see cref="Initial"/> = [count-in][first body loop], then <see cref="Body"/> repeats (body loop n >= 1 starts at
/// <c>Initial.TotalFrames + (n - 1) * Body.TotalFrames</c>). <see cref="CountInFrames"/>, <see cref="EndFrame"/>,
/// every plan frame and <c>PlaybackPosition</c> are in own frames. The two are related by
/// <c>own = absolute + <see cref="Shift"/></c>.
/// </para>
/// <para>
/// The first revision of a session has <c>Shift == 0</c>. A live structural edit (tempo, meter, ...) compiles a new
/// revision and splices it in at the pulse boundary B (absolute): the new revision is a cheap copy
/// (<see cref="Rebased"/>) sharing Document/Initial/Body whose Shift maps B onto the musical position that
/// continues the old rhythm (same bar structure) or restarts it at bar 1 beat 1 (different meter, unit or loop
/// length); see <c>LiveSplice</c>. Anything that mixes device frames with a revision's plans or
/// <see cref="EndFrame"/> must convert with <see cref="ToOwn"/>/<see cref="ToAbsolute"/> (or use
/// <see cref="EndFrameAbsolute"/>); elapsed time is always derived from absolute frames.
/// </para>
/// </summary>
public sealed class PlaybackRevision
{
    private readonly long? _nativeTimer;

    public PulseDocument Document { get; }
    public RenderPlan Initial { get; }
    public RenderPlan Body { get; }
    public bool IsFinite => Document.Program.Count > 0;
    public int SampleRate => Initial.SampleRate;
    /// <summary>End of the count-in in OWN frames, which is where bar 1 beat 1 of the body starts.</summary>
    public long CountInFrames => Initial.CountInFrames;
    /// <summary>Practice/program deadline in OWN frames, or null when playback never ends by itself.</summary>
    public long? EndFrame { get; }
    /// <summary>Own frame = absolute device frame + Shift. Zero for the first revision of a session.</summary>
    public long Shift { get; }
    public bool HasPracticeLimit => PlaybackDeadline.IsEnabled(Document.Settings);
    /// <summary>The deadline as an absolute device frame (<see cref="EndFrame"/> - <see cref="Shift"/>).</summary>
    public long? EndFrameAbsolute => EndFrame is { } end ? end - Shift : null;
    internal DeadlineParts Deadline { get; }

    public PlaybackRevision(PulseDocument source, int sampleRate)
    {
        Document = LocalWorkspace.Clone(source);
        var bars = LoopBars(Document);
        Initial = ScheduleCompiler.Compile(Document, sampleRate, bars, true);
        Body = ScheduleCompiler.Compile(Document, sampleRate, bars, false);
        if (Initial.TotalFrames <= 0 || Body.TotalFrames <= 0)
            throw new ArgumentException("The compiled rhythm has no duration.");
        Deadline = PlaybackDeadline.ResolveParts(Initial, Body, Document.Settings, IsFinite);
        EndFrame = Deadline.End;
        _nativeTimer = Deadline.Timer;
        // Warm any lazily initialized renderer caches here, never in QuantumStarted.
        Span<float> warmup = stackalloc float[2];
        PcmRenderer.Fill(warmup, Initial, 0, 2, 0, false, false);
        PcmRenderer.Fill(warmup, Body, 0, 2, 0, false, false);
    }

    private PlaybackRevision(PlaybackRevision source, long shift, DeadlineParts deadline)
    {
        Document = source.Document;
        Initial = source.Initial;
        Body = source.Body;
        Shift = shift;
        Deadline = deadline;
        EndFrame = deadline.End;
        _nativeTimer = source._nativeTimer;
    }

    public long ToOwn(long absoluteFrame) => absoluteFrame + Shift;
    public long ToAbsolute(long ownFrame) => ownFrame - Shift;

    /// <summary>
    /// Practice/program time left, in frames, once the device has processed the ABSOLUTE <paramref name="processedFrame"/>
    /// (the count-in is never part of it); null when playback never ends by itself.
    /// </summary>
    public long? FramesRemainingAt(long processedFrame) =>
        EndFrame is { } end ? Math.Max(0, end - Math.Max(ToOwn(processedFrame), CountInFrames)) : null;

    /// <summary>
    /// Seconds after <paramref name="startFrame"/> of the targets of one lane that fall in the ABSOLUTE device-frame window
    /// [<paramref name="startFrame"/>, <paramref name="endFrame"/>): what a keyboard-timing run is compared against.
    /// </summary>
    internal List<double> TargetSeconds(int lane, bool includeSilent, long startFrame, long endFrame)
    {
        var startOwn = ToOwn(startFrame);
        var endOwn = ToOwn(endFrame);
        var seconds = new List<double>();
        void Append(RenderPlan plan, long origin, long excludeBefore)
        {
            foreach (var onset in plan.Targets)
            {
                if (onset.LaneIndex != lane || onset.Accent == Accent.Silent || (!includeSilent && !onset.Audible)) continue;
                var frame = origin + onset.Frame;
                if (frame < Math.Max(startOwn, excludeBefore) || frame >= endOwn) continue;
                seconds.Add((double)(frame - startOwn) / plan.SampleRate);
            }
        }
        Append(Initial, 0, CountInFrames);
        if (IsFinite) return seconds;
        for (var cycle = Math.Max(0, (startOwn - Initial.TotalFrames) / Body.TotalFrames); ; cycle++)
        {
            var origin = Initial.TotalFrames + cycle * Body.TotalFrames;
            if (origin >= endOwn) break;
            Append(Body, origin, origin);
        }
        return seconds;
    }

    /// <summary>Allocation-light copy that shares the compiled plans; safe to call once per splice in the callback.</summary>
    internal PlaybackRevision Rebased(long shift, DeadlineParts deadline) =>
        shift == Shift && deadline == Deadline ? this : new(this, shift, deadline);

    /// <summary>True when no live splice has placed this revision: its own and absolute timelines coincide.</summary>
    internal bool IsNative => Shift == 0 && Deadline.Timer == _nativeTimer;

    /// <summary>
    /// True when <paramref name="prepared"/>, a freshly compiled (unplaced) sound-only edit of this revision, keeps its
    /// musical clock and stop boundary: equal Initial/Body lengths and equal musical stop frames. The wall-clock timer
    /// is compared only while this revision is unplaced; a placed one keeps its own timer when the edit is switched in.
    /// </summary>
    internal bool HasSameClock(PlaybackRevision prepared) =>
        Initial.TotalFrames == prepared.Initial.TotalFrames && Body.TotalFrames == prepared.Body.TotalFrames &&
        Deadline.WithTimer(null) == prepared.Deadline.WithTimer(null) &&
        (!IsNative || Deadline.Timer == prepared.Deadline.Timer);

    /// <summary>
    /// Recompiles at another sample rate (endpoint change while paused). A spliced revision keeps its placement:
    /// Shift and the wall-clock timer scale with the rate, the musical stop conditions are recomputed natively.
    /// </summary>
    internal PlaybackRevision Resampled(int sampleRate, Func<long, long> convertFrame)
    {
        var native = new PlaybackRevision(Document, sampleRate);
        if (IsNative) return native;
        return native.Rebased(convertFrame(Shift),
            native.Deadline.WithTimer(Deadline.Timer is { } timer ? convertFrame(timer) : null));
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

/// <summary>
/// State carried across a pause/endpoint change. <c>SwitchFrame</c> and <c>ProcessedFrame</c> are ABSOLUTE device
/// frames; each revision carries its own Shift. <c>Pending</c> is a prepared, unplaced (Shift 0) live request.
/// </summary>
internal sealed record RetainedPlayback(PlaybackRevision Active, PlaybackRevision? Previous,
    PlaybackRevision? Pending, long SwitchFrame, long ProcessedFrame)
{
    public static bool HasProcessedTail(long processedFrame, long switchFrame, int sampleRate) =>
        processedFrame >= switchFrame && processedFrame - switchFrame >= (long)Math.Ceiling(sampleRate * 0.05);

    public RetainedPlayback Rebuild(int sampleRate)
    {
        var previousRate = Active.Initial.SampleRate;
        if (sampleRate == previousRate) return this;
        long ConvertFrame(long frame) => checked((long)Math.Round((double)frame * sampleRate / previousRate));
        return new(Active.Resampled(sampleRate, ConvertFrame),
            Previous?.Resampled(sampleRate, ConvertFrame),
            Pending?.Resampled(sampleRate, ConvertFrame),
            ConvertFrame(SwitchFrame), ConvertFrame(ProcessedFrame));
    }
}
