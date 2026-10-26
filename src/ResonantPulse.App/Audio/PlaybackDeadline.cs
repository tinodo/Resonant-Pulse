namespace ResonantPulse.App.Audio;

internal static class PlaybackDeadline
{
    internal static long? Resolve(int sampleRate, long countInFrames, int practiceSeconds, long? programEnd)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegative(countInFrames);
        if (practiceSeconds is < 0 or > 86400)
            throw new ArgumentOutOfRangeException(nameof(practiceSeconds));
        if (programEnd is { } end && end <= countInFrames)
            throw new ArgumentOutOfRangeException(nameof(programEnd));
        long? timerEnd = practiceSeconds == 0 ? null :
            checked(countInFrames + (long)practiceSeconds * sampleRate);
        return timerEnd is { } timer && programEnd is { } finite ? Math.Min(timer, finite) :
            timerEnd ?? programEnd;
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
