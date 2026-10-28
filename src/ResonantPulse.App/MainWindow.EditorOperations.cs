using Microsoft.UI.Xaml.Controls;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private readonly EditorOperationGate _editorOperations = new();
    private readonly List<(Button Control, Func<bool>? Allowed)> _editorOperationControls = [];
    private int? _editorOperationRevision;

    /// <summary>
    /// A command that runs through the editor gate. <paramref name="allowed"/> adds the command's own condition (Undo has
    /// nothing to undo): the command is enabled when no editor operation is running and the condition holds.
    /// </summary>
    private Button EditorActionButton(string key, string id, Func<Task> action, Func<bool>? allowed = null)
    {
        var revision = _editorContentRevision;
        return EditorControl(ActionButton(key, id, () => RunEditorOperationAsync(revision, action)), allowed);
    }

    private Button EditorControl(Button control, Func<bool>? allowed = null)
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
