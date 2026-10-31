using System.Text.Json;

namespace ResonantPulse.Core;

/// <summary>Finite stereo 16-bit PCM RIFF export. Uses document master gain and the exact seeded audible schedule, without looping.</summary>
public static class WaveExporter
{
    private static readonly SemaphoreSlim Exports = new(1, 1);

    /// <summary>
    /// Prepares both files before committing. Ordinary commit failures roll back prior replacements.
    /// Calls through this API are serialized through rendering, commit, rollback and cleanup.
    /// Cancellation is honored while queued and before commit, not during commit/rollback.
    /// This is not crash-atomic across two files; external writers must not modify the destinations.
    /// </summary>
    public static async Task ExportAsync(string path, RenderPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await Exports.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await ExportCoreAsync(path, plan, cancellationToken).ConfigureAwait(false); }
        finally { Exports.Release(); }
    }

    private static async Task ExportCoreAsync(string path, RenderPlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = PcmWaveWriter.AudioBytes(plan);
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
            await AtomicFile.WriteAsync(waveStage,
                (stream, token) => PcmWaveWriter.WriteAsync(stream, plan, token), cancellationToken).ConfigureAwait(false);
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
                    throw new WaveExportException(WaveExportFailure.RollbackFailed, full, manifestPath,
                        [waveBackup, manifestBackup],
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
                var outcome = preserveBackups ? WaveExportFailure.RollbackFailed
                    : committed ? WaveExportFailure.CommittedCleanupFailed : WaveExportFailure.FailedCleanup;
                throw new WaveExportException(outcome, full, manifestPath,
                    preserveBackups ? [waveBackup, manifestBackup] : [waveStage, manifestStage, waveBackup, manifestBackup],
                    preserveBackups
                        ? $"WAV export rollback and cleanup failed for '{full}' and '{manifestPath}'. Outputs may not match; recover retained original backups before retrying."
                        : committed
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
