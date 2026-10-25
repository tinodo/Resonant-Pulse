namespace ResonantPulse.Core;

/// <summary>Distinguishes export recovery requirements from a complete pair needing cleanup.</summary>
public enum WaveExportFailure
{
    RollbackFailed,
    CommittedCleanupFailed,
    FailedCleanup
}

/// <summary>Preserves machine-readable export outcomes and local recovery paths for localized callers.</summary>
public sealed class WaveExportException : IOException
{
    public WaveExportFailure Failure { get; }
    public string WavePath { get; }
    public string ManifestPath { get; }
    /// <summary>Candidate original backups for rollback failure, or cleanup paths otherwise; not all need still exist.</summary>
    public IReadOnlyList<string> RecoveryPaths { get; }

    public WaveExportException(WaveExportFailure failure, string wavePath, string manifestPath,
        IReadOnlyList<string> recoveryPaths, string message, Exception innerException)
        : base(message, innerException)
    {
        if (!Enum.IsDefined(failure)) throw new ArgumentOutOfRangeException(nameof(failure));
        ArgumentException.ThrowIfNullOrWhiteSpace(wavePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(recoveryPaths);
        Failure = failure;
        WavePath = wavePath;
        ManifestPath = manifestPath;
        RecoveryPaths = Array.AsReadOnly(recoveryPaths.ToArray());
    }
}
