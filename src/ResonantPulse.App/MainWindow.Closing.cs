using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    /// <summary>How long closing waits for what is being saved before it stops waiting.</summary>
    private static readonly TimeSpan CloseSaveLimit = TimeSpan.FromSeconds(4);

    private bool _closeChecked;

    private void InstallCloseFlush()
    {
        AppWindow.Closing += (sender, args) =>
        {
            if (_closeChecked || !_isReady || _isClosed) return;
            args.Cancel = true;
            _ = CloseAfterSavingAsync();
        };
    }

    /// <summary>
    /// Closing first commits what is being typed and waits for every queued edit and notes save, so closing the window never
    /// loses a change. If a change cannot be saved the window stays open with the reason shown, and closing it again closes it.
    /// </summary>
    private async Task CloseAfterSavingAsync()
    {
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
        // Whatever the outcome, the next request to close is not held up again.
        _closeChecked = true;
        if (saved)
        {
            Close();
            return;
        }
        // A save that failed has shown its own error; one that is only slow has shown nothing yet.
        if (timedOut && !_isClosed) ShowNotice("CloseUnsaved");
    }

    /// <summary>Commits a field that is being edited, then waits until every queued edit and notes save has finished.</summary>
    private async Task<bool> FlushPendingEditsAsync()
    {
        // A field commits when it loses focus, so the focus moves to something that is not a field. A number box keeps its
        // text in an inner text box, which is the element that has the focus.
        if (FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox or ComboBox or Slider)
            Navigation.Focus(FocusState.Programmatic);
        var notesSaved = await FlushStageNotesAsync();
        // Edits are applied one at a time in the order they were asked for: once this wait is over, every earlier one is saved.
        await _edits.WaitAsync();
        _edits.Release();
        return notesSaved;
    }
}
