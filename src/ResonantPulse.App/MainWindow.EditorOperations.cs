using Microsoft.UI.Xaml.Controls;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private readonly EditorOperationGate _editorOperations = new();
    private readonly List<Button> _editorOperationControls = [];
    private int? _editorOperationRevision;

    private Button EditorActionButton(string key, string id, Func<Task> action)
    {
        var revision = _editorContentRevision;
        return EditorControl(ActionButton(key, id, () => RunEditorOperationAsync(revision, action)));
    }

    private Button EditorControl(Button control)
    {
        _editorOperationControls.Add(control);
        control.IsEnabled = !_editorOperations.IsBusy;
        return control;
    }

    private void RefreshEditorControlState()
    {
        if (_isClosed) return;
        foreach (var control in _editorOperationControls)
            control.IsEnabled = !_editorOperations.IsBusy;
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
