using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    /// <summary>How long closing waits for what is being saved before it stops waiting.</summary>
    private static readonly TimeSpan CloseSaveLimit = TimeSpan.FromSeconds(4);

    // Closing happens in at most two attempts when something cannot be saved: the first stays open and says why, the second tries
    // again and, if it still cannot save, leaves anyway.
    private bool _closeNow;
    private bool _closeInProgress;
    private bool _closeAttemptFailed;

    /// <summary>True while the window is being closed: nothing new starts producing edits.</summary>
    private bool _closeFlushing;

    private void InstallCloseFlush()
    {
        AppWindow.Closing += (sender, args) =>
        {
            if (_closeNow || !_isReady || _isClosed) return;
            args.Cancel = true;
            if (_closeInProgress) return;
            _ = CloseAfterSavingAsync();
        };
    }

    /// <summary>
    /// Closing first commits what is being typed, waits for every edit that is still being produced or queued, and then makes
    /// sure the project and the settings are on disk. Closing the window never loses a change silently: if a change cannot be
    /// saved the window stays open with the reason shown. Closing it again tries once more, and leaves if that fails too.
    /// </summary>
    private async Task CloseAfterSavingAsync()
    {
        _closeInProgress = true;
        _closeFlushing = true;
        var saved = false;
        var timedOut = false;
        try
        {
            var flush = FlushPendingEditsAsync();
            var finished = await Task.WhenAny(flush, Task.Delay(CloseSaveLimit));
            timedOut = finished != flush;
            saved = !timedOut && await flush;
        }
        catch (Exception exception) { Diagnostics.Write("Saving before the window closed", exception); }
        finally
        {
            _closeFlushing = false;
            _closeInProgress = false;
        }
        if (saved || _closeAttemptFailed)
        {
            _closeNow = true;
            Close();
            return;
        }
        _closeAttemptFailed = true;
        // A save that failed has shown its own error; one that is only slow has shown nothing yet.
        if (timedOut && !_isClosed) ShowNotice("CloseUnsaved");
    }

    /// <summary>
    /// Commits a field that is being edited, waits for the tempo being submitted and for every notes save, and then makes sure the
    /// project and the settings are on disk as they are now: a write that failed earlier is repeated. False when something could
    /// not be saved (the error has been shown).
    /// </summary>
    private async Task<bool> FlushPendingEditsAsync()
    {
        // A field commits when it loses focus, so the focus moves to something that is not a field. A number box keeps its
        // text in an inner text box, which is the element that has the focus.
        if (FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox or ComboBox or Slider)
            Navigation.Focus(FocusState.Programmatic);
        // The tempo is submitted one step at a time; it is done when the newest value has been committed.
        await _tempoPump;
        var notesSaved = await FlushStageNotesAsync();
        var projectSaved = false;
        await GuardAsync(async () =>
        {
            // Under the edit lock every edit asked for before this point has been applied and none is half published, so what
            // is written is the project as the person left it. The file is written again only if it is behind the project (a
            // write failed earlier); an untouched project is left as it is.
            await _edits.WaitAsync();
            try { if (_recoveryUnsaved) await SaveRecoveryAsync(); }
            finally { _edits.Release(); }
            projectSaved = true;
        });
        var settingsSaved = false;
        await GuardAsync(async () =>
        {
            // A settings file that is behind the settings (a write failed earlier) is written again. One that is not is left
            // alone, so a settings file that could not be read at start is never overwritten with defaults.
            if (_workspace.PreferencesBehind) await _workspace.SavePreferencesAsync(_preferences);
            settingsSaved = true;
        });
        return notesSaved && projectSaved && settingsSaved;
    }
}
