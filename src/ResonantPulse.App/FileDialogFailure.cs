namespace ResonantPulse.App;

/// <summary>
/// What the page reports when a file dialog (the Windows window that asks where to save a file, or which one to open) cannot be
/// opened or fails. Every such dialog goes through one helper of the window (<c>ShowFileDialogAsync</c>), which hands the failure to
/// <see cref="ForPage"/>; the exception it returns is the one the page shows.
/// </summary>
internal static class FileDialogFailure
{
    /// <summary>
    /// The failure of a file dialog as the page reports it. Not decided yet: the failure reaches the page as it is, so the page
    /// explains it by its type, as it did before this was written down (a failure of the operating system's window handling then reads
    /// as a problem with the audio output).
    /// </summary>
    internal static Exception ForPage(Exception error) => error;
}
