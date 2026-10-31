using System.Buffers.Binary;
using System.Text;

namespace ResonantPulse.Core;

/// <summary>
/// The one writer of a finite 16-bit stereo PCM WAV file. The mixed export (<see cref="WaveExporter"/>) and every track of a stem
/// export (<see cref="WaveStemExporter"/>) go through it, so they cannot disagree about the header, the rounding or the peak limit.
/// </summary>
internal static class PcmWaveWriter
{
    /// <summary>The audio bytes of the plan (four bytes a frame). Refused above what a RIFF file can hold.</summary>
    internal static long AudioBytes(RenderPlan plan)
    {
        long bytes = checked(plan.TotalFrames * 4);
        if (bytes > uint.MaxValue - 36)
            throw new ArgumentException("This render exceeds RIFF's 4 GiB limit. Export shorter sections; RF64 is not supported.");
        return bytes;
    }

    /// <summary>The 44-byte header, then the plan rendered in blocks of 4,096 frames (master gain, a hard peak limit, no loop), rounded away from zero.</summary>
    internal static async Task WriteAsync(Stream stream, RenderPlan plan, CancellationToken token)
    {
        long byteCount = AudioBytes(plan);
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
    }
}
