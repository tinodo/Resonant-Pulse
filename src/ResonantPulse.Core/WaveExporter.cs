using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace ResonantPulse.Core;

/// <summary>Finite stereo 16-bit PCM RIFF export. Uses document master gain and the exact seeded audible schedule, without looping.</summary>
public static class WaveExporter
{
    /// <summary>
    /// Prepares both files before committing. Ordinary commit failures roll back prior replacements.
    /// Cancellation is honored before commit, not during commit/rollback. This is not crash-atomic
    /// across two files; callers must not concurrently export to or modify the same destinations.
    /// </summary>
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
        string stagingPrefix = Path.Combine(Path.GetDirectoryName(full)!, ".wave-export-" + Guid.NewGuid().ToString("N"));
        string waveStage = stagingPrefix + ".wav.tmp", manifestStage = stagingPrefix + ".json.tmp";
        string waveBackup = stagingPrefix + ".wav.bak", manifestBackup = stagingPrefix + ".json.bak";
        bool preserveBackups = false, committed = false;
        Exception? failure = null;
        try
        {
            await AtomicFile.WriteAsync(waveStage, async (stream, token) =>
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
            await AtomicFile.WriteAsync(manifestStage, async (stream, token) =>
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

            // Last cancellable point: neither destination has been changed. Once commit starts,
            // finish it or roll it back without allowing cancellation to interrupt recovery.
            cancellationToken.ThrowIfCancellationRequested();
            bool manifestInstalled = false;
            bool hadManifest = File.Exists(manifestPath), hadWave = File.Exists(full);
            try
            {
                // Commit metadata first: a blocked manifest must never replace the existing WAV.
                Install(manifestStage, manifestPath, manifestBackup, hadManifest);
                manifestInstalled = true;
                Install(waveStage, full, waveBackup, hadWave);
                committed = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                var rollbackErrors = new List<Exception>();
                if (manifestInstalled) Restore(manifestPath, manifestBackup, hadManifest, rollbackErrors);
                if (rollbackErrors.Count != 0)
                {
                    preserveBackups = true;
                    throw new IOException(
                        $"WAV export commit and rollback failed. The pair '{full}' and '{manifestPath}' may not match. " +
                        $"Any original backups are retained at '{waveBackup}' and '{manifestBackup}'; recover them before retrying.",
                        new AggregateException(new[] { error }.Concat(rollbackErrors)));
                }
                throw new IOException($"WAV export commit failed; prior destination contents/absence were restored for '{full}' and '{manifestPath}'.", error);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException or JsonException or ArgumentException or NotSupportedException)
        {
            failure = error;
            throw;
        }
        finally
        {
            var cleanupErrors = new List<Exception>();
            DeleteTemporary(waveStage, cleanupErrors);
            DeleteTemporary(manifestStage, cleanupErrors);
            if (!preserveBackups)
            {
                DeleteTemporary(waveBackup, cleanupErrors);
                DeleteTemporary(manifestBackup, cleanupErrors);
            }
            if (cleanupErrors.Count != 0)
            {
                if (failure is not null) cleanupErrors.Insert(0, failure);
                throw new IOException(
                    committed
                        ? $"Both export outputs were committed, but temporary/backup cleanup failed for '{full}'. The committed pair is complete."
                        : $"WAV export did not complete and temporary/backup cleanup also failed for '{full}'. See inner errors for recovery details.",
                    new AggregateException(cleanupErrors));
            }
        }
    }

    private static void Install(string staged, string target, string backup, bool existed)
    {
        if (existed) File.Replace(staged, target, backup);
        else File.Move(staged, target);
    }

    private static void Restore(string target, string backup, bool existed, List<Exception> errors)
    {
        try
        {
            if (!existed) File.Delete(target);
            else if (File.Exists(target)) File.Replace(backup, target, null);
            else File.Move(backup, target);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { errors.Add(new IOException($"Could not restore '{target}' from '{backup}'.", error)); }
    }

    private static void DeleteTemporary(string path, List<Exception> errors)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { errors.Add(new IOException($"Could not remove export temporary/backup file '{path}'.", error)); }
    }
}
