using Microsoft.UI.Xaml.Controls;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private readonly EditorOperationGate _editorOperations = new();
    private readonly List<(Control Control, Func<bool>? Allowed)> _editorOperationControls = [];
    private int? _editorOperationRevision;

    /// <summary>
    /// Registers a control so it is switched off while an editor operation runs: it is enabled when no editor operation is running
    /// and <paramref name="allowed"/> (the control's own condition: Undo has nothing to undo) holds. The fields of a lane, of a
    /// program step and of an exact position use it too. A field applies as it is changed, queued behind the edits before it with
    /// the revision of the page it sits on; an operation that adds, removes, rotates, moves or undoes changes that revision, so an
    /// edit made while one runs would be queued, then refused, and what was typed lost from a field that still looked editable.
    /// </summary>
    private T EditorControl<T>(T control, Func<bool>? allowed = null) where T : Control
    {
        _editorOperationControls.Add((control, allowed));
        control.IsEnabled = !_editorOperations.IsBusy && (allowed?.Invoke() ?? true);
        return control;
    }

    private void RefreshEditorControlState()
    {
        if (_isClosed) return;
        var busy = _editorOperations.IsBusy;
        foreach (var (control, allowed) in _editorOperationControls)
            control.IsEnabled = !busy && (allowed?.Invoke() ?? true);
    }

    /// <summary>A control that is no longer on the page is not switched on and off with the others, and can be released.</summary>
    private void ForgetEditorControl(Control control) =>
        _editorOperationControls.RemoveAll(entry => ReferenceEquals(entry.Control, control));

    private async Task RunEditorOperationAsync(int revision, Func<Task> action)
    {
        try
        {
            var accepted = await _editorOperations.TryRunAsync(revision, () => _editorContentRevision, async () =>
            {
                _editorOperationRevision = revision;
                RefreshEditorControlState();
                try
                {
                    if (_isClosed) return;
                    if (_isLocked) { ShowNotice("Locked"); return; }
                    // Includes confirmation, validation, recovery and the page
                    // rebuild, not only the document-edit semaphore interval.
                    await action();
                }
                finally { _editorOperationRevision = null; }
            });
            if (!accepted && !_isClosed) ShowNotice("EditorBusy");
        }
        catch (EditorRevisionChangedException)
        {
            if (!_isClosed) BuildPage();
            throw;
        }
        finally { RefreshEditorControlState(); }
    }

    private Task CommitEditorAsync(Action<PulseDocument> edit) =>
        CommitAsync(edit, expectedRevision: _editorOperationRevision ?? throw new EditorRevisionChangedException());

    private Task UndoEditorAsync(bool redo) =>
        UndoAsync(redo, _editorOperationRevision ?? throw new EditorRevisionChangedException());
}
