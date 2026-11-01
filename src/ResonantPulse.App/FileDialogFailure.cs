namespace ResonantPulse.App;

/// <summary>
/// What the page reports when a file dialog (the Windows window that asks where to save a file, or which one to open) cannot be
/// opened or fails. Every such dialog goes through one helper of the window (<c>ShowFileDialogAsync</c>), which hands the failure to
/// <see cref="ForPage"/>; the exception it returns is the one the page shows.
/// </summary>
internal static class FileDialogFailure
{
    /// <summary>
    /// The failure of a file dialog as the page reports it: a <see cref="FileDialogException"/>, which the page explains in words about
    /// the dialog, whatever the dialog failed with. Without this a failure of the operating system's window handling (a
    /// <see cref="System.Runtime.InteropServices.COMException"/>, which is what the audio code reports its failures as too) read as a
    /// problem with the audio output. A cancellation that the dialog reports (an <see cref="OperationCanceledException"/>) is no failure
    /// of the dialog and stays as it is.
    /// </summary>
    internal static Exception ForPage(Exception error) =>
        error is OperationCanceledException ? error : new FileDialogException(error);
}

/// <summary>
/// A file dialog could not be opened or failed. Nothing was read or written. The exception that made it fail is the inner exception,
/// so that the log keeps its type, its message and its HRESULT; the person is told in plain words (<c>ErrorFileDialog</c>).
/// </summary>
internal sealed class FileDialogException(Exception cause) : Exception("A file dialog could not be opened or failed.", cause);
