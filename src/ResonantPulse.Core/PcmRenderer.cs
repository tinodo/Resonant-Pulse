namespace ResonantPulse.Core;

internal readonly record struct RenderVoice(long Frame, float[] Samples, float Mono, float Left, float Right);

internal static class ClickCache
{
    internal static (RenderVoice[], int) Prepare(ScheduledOnset[] onsets, int rate)
    {
        var cache = new Dictionary<(string, Accent), float[]>();
        var voices = new RenderVoice[onsets.Length];
        int tail = 0;
        for (int i = 0; i < onsets.Length; i++)
        {
            var e = onsets[i];
            if (!cache.TryGetValue((e.Sound, e.Accent), out var sample))
            {
                sample = Synthesize(e.Sound, e.Accent, rate);
                cache.Add((e.Sound, e.Accent), sample);
                tail = Math.Max(tail, sample.Length);
            }
            double angle = (e.Pan + 1) * Math.PI / 4;
            voices[i] = new(e.Frame, sample, e.Gain, e.Gain * (float)Math.Cos(angle), e.Gain * (float)Math.Sin(angle));
        }
        return (voices, tail);
    }
    private static float[] Synthesize(string sound, Accent accent, int rate)
    {
        int length = (int)Math.Ceiling(rate * 0.05);
        var data = new float[length];
        double strength = accent switch { Accent.Strong => 0.85, Accent.Normal => 0.60, Accent.Soft => 0.32, _ => 0 };
        double pitch = accent == Accent.Strong ? 1.18 : accent == Accent.Soft ? 0.88 : 1;
        uint noise = 0x12345678;
        for (int i = 0; i < length; i++)
        {
            double t = (double)i / rate;
            noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
            double n = noise / (double)uint.MaxValue * 2 - 1;
            double attack = Math.Min(1, t / 0.0005);
            double end = Math.Min(1, (length - 1 - i) / (rate * 0.003));
            double x = sound switch
            {
                "Wood" => (Math.Sin(2 * Math.PI * 880 * pitch * t) + 0.4 * Math.Sin(2 * Math.PI * 1430 * pitch * t)) * Math.Exp(-170 * t) / 1.4,
                "Glass" => (Math.Sin(2 * Math.PI * 1850 * pitch * t) + 0.35 * Math.Sin(2 * Math.PI * 2933 * pitch * t)) * Math.Exp(-90 * t) / 1.35,
                "Kick" => Math.Sin(2 * Math.PI * (75 * t + 1.5 * (1 - Math.Exp(-60 * t)))) * Math.Exp(-85 * t),
                "HiHat" => (n * 0.75 + 0.25 * Math.Sin(2 * Math.PI * Math.Min(6000, rate * 0.42) * t)) * Math.Exp(-180 * t),
                _ => (0.65 * n + 0.35 * Math.Sin(2 * Math.PI * 3200 * pitch * t)) * Math.Exp(-350 * t)
            };
            data[i] = (float)(strength * attack * Math.Max(0, end) * x);
        }
        return data;
    }
}

/// <summary>Sample-aligned, stateless mixing over compiler-owned immutable caches. Valid calls allocate nothing and take no locks.</summary>
public static class PcmRenderer
{
    /// <summary>
    /// Writes mono or stereo. Loop repeats the entire plan including count-in; compile without count-in for body-only looping.
    /// Tails overlap loop boundaries; non-looping output is truncated at TotalFrames. Linear gain then hard peak protection [-1,1].
    /// Maximum callback buffer is 32768 frames; split larger buffers. MasterGain is explicit, not multiplied by document gain again.
    /// Attack bounds use the absolute render coordinate, including loop cycles. When combining partitions,
    /// disable peak limiting on each partition and clamp the final sum once.
    /// </summary>
    public static void Fill(Span<float> interleaved, RenderPlan plan, long startFrame, int channels = 2,
        float masterGain = 0.5f, bool mute = false, bool loop = true,
        long? attacksFromFrame = null, long? attacksBeforeFrame = null, bool limitPeaks = true)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (channels is not (1 or 2)) throw new ArgumentException("Core supports mono/stereo only; multichannel routing requires an adapter.", nameof(channels));
        if (interleaved.Length % channels != 0 || interleaved.Length / channels > 32768)
            throw new ArgumentException("Buffer must contain whole frames and at most 32768 frames.", nameof(interleaved));
        if (startFrame < 0 || startFrame > long.MaxValue - 32768 - plan.SampleRate)
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        if (!float.IsFinite(masterGain) || masterGain is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(masterGain));
        interleaved.Clear();
        if (mute || masterGain == 0 || interleaved.IsEmpty || plan.Voices.Length == 0) return;
        int frames = interleaved.Length / channels;
        long end = startFrame + frames;
        long firstCycle = loop ? Math.Max(0, (startFrame - plan.TailFrames) / plan.TotalFrames) : 0;
        long lastCycle = loop ? (end - 1) / plan.TotalFrames : 0;
        for (long cycle = firstCycle; cycle <= lastCycle; cycle++)
        {
            long origin = cycle * plan.TotalFrames;
            long localStart = startFrame - origin;
            long localEnd = Math.Min(end - origin, loop ? long.MaxValue : plan.TotalFrames);
            var voices = plan.Voices;
            int lo = 0, hi = voices.Length;
            long earliest = localStart - plan.TailFrames + 1;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (voices[mid].Frame < earliest) lo = mid + 1; else hi = mid;
            }
            for (int v = lo; v < voices.Length && voices[v].Frame < localEnd; v++)
            {
                ref readonly var voice = ref voices[v];
                long attack = origin + voice.Frame;
                if (attacksFromFrame is { } first && attack < first ||
                    attacksBeforeFrame is { } exclusive && attack >= exclusive) continue;
                int sampleStart = (int)Math.Max(0, localStart - voice.Frame);
                int sampleEnd = (int)Math.Min(voice.Samples.Length, localEnd - voice.Frame);
                for (int j = sampleStart; j < sampleEnd; j++)
                {
                    int output = checked((int)(origin + voice.Frame + j - startFrame)) * channels;
                    float x = voice.Samples[j] * masterGain;
                    if (channels == 1) interleaved[output] += x * voice.Mono;
                    else { interleaved[output] += x * voice.Left; interleaved[output + 1] += x * voice.Right; }
                }
            }
        }
        if (limitPeaks)
            for (int i = 0; i < interleaved.Length; i++) interleaved[i] = Math.Clamp(interleaved[i], -1f, 1f);
        ApplyGapMask(interleaved, plan, startFrame, channels, loop);
    }

    /// <summary>
    /// Clears the frames of <paramref name="interleaved"/> (output starting at plan frame <paramref name="startFrame"/>)
    /// that the plan's gap mask marks silent. A gap is an output mask, not just omission of new attacks: prior click
    /// tails must not leak into blind silence. <see cref="Fill"/> applies it itself; callers that sum a partition
    /// rendered from another plan (a live splice's ringing tails) apply the plan that governs the output to it.
    /// Allocates nothing and takes no locks.
    /// </summary>
    public static void ApplyGapMask(Span<float> interleaved, RenderPlan plan, long startFrame, int channels = 2, bool loop = true)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (channels is not (1 or 2)) throw new ArgumentException("Core supports mono/stereo only; multichannel routing requires an adapter.", nameof(channels));
        if (interleaved.Length % channels != 0) throw new ArgumentException("Buffer must contain whole frames.", nameof(interleaved));
        if (startFrame < 0 || startFrame > long.MaxValue - interleaved.Length) throw new ArgumentOutOfRangeException(nameof(startFrame));
        if (interleaved.IsEmpty || plan.SilentRanges.Count == 0) return;
        long end = startFrame + interleaved.Length / channels;
        long firstOutputCycle = loop ? startFrame / plan.TotalFrames : 0;
        long lastCycle = loop ? (end - 1) / plan.TotalFrames : 0;
        for (long cycle = firstOutputCycle; cycle <= lastCycle; cycle++)
        {
            long origin = cycle * plan.TotalFrames;
            long localStart = startFrame - origin, localEnd = Math.Min(end - origin, plan.TotalFrames);
            int lo = 0, hi = plan.SilentRanges.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (plan.SilentRanges[mid].EndFrame <= localStart) lo = mid + 1; else hi = mid;
            }
            for (int i = lo; i < plan.SilentRanges.Count && plan.SilentRanges[i].StartFrame < localEnd; i++)
            {
                var range = plan.SilentRanges[i];
                long a = Math.Max(localStart, range.StartFrame), b = Math.Min(localEnd, range.EndFrame);
                if (b > a) interleaved.Slice((int)(origin + a - startFrame) * channels, (int)(b - a) * channels).Clear();
            }
        }
    }
}
