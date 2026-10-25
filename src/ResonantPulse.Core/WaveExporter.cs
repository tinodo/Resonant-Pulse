using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace ResonantPulse.Core;

/// <summary>Finite stereo 16-bit PCM RIFF export. Uses document master gain and the exact seeded audible schedule, without looping.</summary>
public static class WaveExporter
{
    public static async Task ExportAsync(string path, RenderPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        long byteCount = checked(plan.TotalFrames * 4);
        if (byteCount > uint.MaxValue - 36)
            throw new ArgumentException("This render exceeds RIFF's 4 GiB limit. Export shorter sections; RF64 is not supported.");
        string full = AtomicFile.LocalPath(path);
        string manifestPath = full + ".manifest.json";
        AtomicFile.LocalPath(manifestPath);
        await AtomicFile.WriteAsync(full, async (stream, token) =>
        {
            var header = new byte[44];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)(36 + byteCount));
            Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(header, 8);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 16);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(22), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), (uint)plan.SampleRate);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), (uint)(plan.SampleRate * 4));
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), 4);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(34), 16);
            Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), (uint)byteCount);
            await stream.WriteAsync(header, token).ConfigureAwait(false);
            var floats = new float[4096 * 2];
            var bytes = new byte[4096 * 4];
            for (long frame = 0; frame < plan.TotalFrames; frame += 4096)
            {
                token.ThrowIfCancellationRequested();
                int frames = (int)Math.Min(4096, plan.TotalFrames - frame);
                PcmRenderer.Fill(floats.AsSpan(0, frames * 2), plan, frame, masterGain: plan.DefaultMasterGain, loop: false);
                for (int i = 0; i < frames * 2; i++)
                {
                    int pcm = (int)Math.Round(floats[i] * 32767f, MidpointRounding.AwayFromZero);
                    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), (short)Math.Clamp(pcm, -32768, 32767));
                }
                await stream.WriteAsync(bytes.AsMemory(0, frames * 4), token).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
        // Two local files cannot form one atomic filesystem transaction. WAV is already committed if this second write fails.
        try
        {
            await AtomicFile.WriteAsync(manifestPath, async (stream, token) =>
            {
                await JsonSerializer.SerializeAsync(stream, new
                {
                    SchemaVersion = 1, Format = "PCM16 stereo", plan.SampleRate, plan.TotalFrames, plan.CountInFrames,
                    plan.BeatUnitQuarters, plan.Seed, plan.ProbabilityAlgorithm,
                    MasterGain = plan.DefaultMasterGain, PeakProtection = "hard clamp [-1,1]",
                    Loop = false, TailPolicy = "truncate at finite duration",
                    plan.TempoSegments, plan.SectionBoundaries,
                    SilentRanges = plan.SilentRanges.Select(r => new { r.StartFrame, r.EndFrame }),
                    AudibleOnsets = plan.Onsets
                }, DocumentStore.JsonOptions, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        { throw new IOException($"WAV was written to '{full}', but companion manifest '{manifestPath}' was not completed. Retry export.", e); }
    }
}
