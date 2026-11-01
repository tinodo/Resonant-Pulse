using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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

    /// <summary>True once the window will go (the close has been decided) or has gone: what ends after that has nobody to tell.</summary>
    private bool IsWindowGoing => _closeNow || _isClosed;

    // Lets the local verification build make the last save slow, so a driver can look at the window meanwhile
    // (MainWindow.UiTest.cs); shipped builds have no body.
    partial void HoldCloseForUiTest(ref Task? hold);

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
    /// Closing first commits what is being typed, stops everything that could change the project, waits for every edit that is
    /// still being produced or queued, and then makes sure the project and the settings are on disk. Closing the window never
    /// loses a change silently: if a change cannot be saved the window stays open with the reason shown. Closing it again tries
    /// once more, and leaves if that fails too.
    /// </summary>
    private async Task CloseAfterSavingAsync()
    {
        _closeInProgress = true;
        _closeFlushing = true;
        // Nothing new may start while the last save is made, so a Tap to start that is waiting for its taps or for its start ends.
        CancelTapIn();
        var saved = false;
        var timedOut = false;
        try
        {
            // A field commits when it loses focus, so the focus moves to something that is not a field. Only then is editing
            // switched off: what was being typed is in the queues the flush waits for, and nothing new can be typed after it.
            CommitFocusedField();
            BlockEditing(true);
            Task? hold = null;
            HoldCloseForUiTest(ref hold);
            if (hold is not null) await hold;
            var flush = FlushPendingEditsAsync();
            var finished = await Task.WhenAny(flush, Task.Delay(CloseSaveLimit));
            timedOut = finished != flush;
            saved = !timedOut && await flush;
            // Every write of a file that is going (Save, Save to library, Export WAV, Export MIDI, the tracks, the timing CSV) is stopped and
            // waited for (at most the same limit again, after the one for the save), so that, when a write stops, no temporary file is left
            // beside its destination. Only when the window will go: a close that is refused leaves a write that is being written going.
            // One that waits at its question was answered No by BlockEditing above, and one that is still being prepared (a plan is compiled) is
            // told that its preparation is void (FileWrites.StopForCloseAsync): the page looks at that before it asks, so after the preparation
            // it ends without a question. If a write has not stopped when the limit passes the window goes anyway, and its temporary file may
            // stay (said in the log, with the destinations).
            var windowWillGo = saved || _closeAttemptFailed;
            if (!await _fileWrites.StopForCloseAsync(windowWillGo, CloseSaveLimit) && windowWillGo)
            {
                var named = _fileWrites.Destinations;
                Diagnostics.Write($"The window closed while {_fileWrites.Count} write(s) of files had not stopped" + (named.Count == 0
                    ? "; none of them had begun writing a file, so none has left a temporary file"
                    : $"; a temporary file may be left beside the destination: {string.Join(", ", named)}"));
            }
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
        // Closing is refused for now: the window is usable again.
        BlockEditing(false);
        _closeAttemptFailed = true;
        // A save that failed has shown its own error; one that is only slow has shown nothing yet.
        if (timedOut && !_isClosed) ShowNotice("CloseUnsaved");
    }

    private readonly List<Control> _blockedPanels = [];

    /// <summary>
    /// While the last save is under way nothing may change the project or the settings: a change made now would come after what
    /// that save wrote and be lost when the window goes. The page area, the audio options and every open panel are switched off
    /// (a panel is a popup outside the page, so each is switched off by itself), and on again if closing is refused.
    /// </summary>
    private void BlockEditing(bool blocked)
    {
        Navigation.IsEnabled = !blocked;
        AudioOptionsButton.IsEnabled = !blocked;
        // Start follows closing too (it is outside the page area): it is off while the last save is under way.
        RefreshSnapshot();
        if (!blocked)
        {
            foreach (var panel in _blockedPanels) panel.IsEnabled = true;
            _blockedPanels.Clear();
            return;
        }
        // A question that is open (remove this song?) is answered No: nothing can be confirmed while the project is saved.
        _confirmation?.Hide();
        if (Root.XamlRoot is not { } xamlRoot) return;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
        {
            if (popup.Child is not Control panel || !panel.IsEnabled) continue;
            panel.IsEnabled = false;
            _blockedPanels.Add(panel);
        }
    }

    /// <summary>
    /// A field commits when it loses focus, so the focus moves to something that is not a field. A number box keeps its text in
    /// an inner text box, which is the element that has the focus.
    /// </summary>
    private void CommitFocusedField()
    {
        if (FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox or ComboBox or Slider)
            Navigation.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Waits for the tempo being submitted and for every notes save, and then makes sure the project and the settings are on
    /// disk as they are now: a write that failed earlier is repeated. False when something could not be saved (the error has
    /// been shown). Nothing can be edited meanwhile (see <see cref="BlockEditing"/>), so what is written is final.
    /// </summary>
    private async Task<bool> FlushPendingEditsAsync()
    {
        // The tempo is submitted one step at a time; it is done when the newest value has been committed.
        await _tempoNudges.Settled;
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
