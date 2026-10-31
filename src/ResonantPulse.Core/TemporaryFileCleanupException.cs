namespace ResonantPulse.Core;

/// <summary>
/// An atomic write failed, and then the temporary file beside the destination could not be removed either. The destination is as it
/// was; <see cref="TemporaryPath"/> is the file that stays, so that a caller can tell the person which one it is.
/// </summary>
public sealed class TemporaryFileCleanupException : IOException
{
    public string TemporaryPath { get; }

    public TemporaryFileCleanupException(string temporaryPath, string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryPath);
        TemporaryPath = temporaryPath;
    }
}
