using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class EditorOperationGateTests
{
    [TestMethod]
    public async Task HeldAddAndSortRejectsApplyAndRemoveBeforeTheirCapturedIndicesCanRun()
    {
        var gate = new EditorOperationGate();
        var validating = Signal();
        var finishValidation = Signal();
        var revision = 0;
        var events = new[] { "A", "B" };
        var staleCallbacks = 0;
        var add = gate.TryRunAsync(0, () => revision, async () =>
        {
            validating.SetResult(true);
            await finishValidation.Task;
            events = ["A", "New", "B"];
            revision++;
        });
        await validating.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var accepted = new List<bool>();
        foreach (var request in new[] { "ApplyB", "RemoveB", "RemoveLane", "MoveProgramStep", "Undo", "Redo" })
        {
            accepted.Add(await gate.TryRunAsync(0, () => revision, () =>
            {
                staleCallbacks++;
                events[1] = "Wrong captured event";
                return Task.CompletedTask;
            }));
        }
        finishValidation.SetResult(true);
        Assert.IsTrue(await add.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.IsTrue(accepted.All(value => !value));
        Assert.AreEqual(0, staleCallbacks);
        CollectionAssert.AreEqual(new[] { "A", "New", "B" }, events);
        Assert.IsFalse(gate.IsBusy);

        // An already-queued old-page event arriving after the gate releases is
        // still stale: revision protection must reject it rather than edit New.
        var wasStale = false;
        try
        {
            await gate.TryRunAsync(0, () => revision, () =>
            {
                staleCallbacks++;
                events[1] = "Wrong captured event";
                return Task.CompletedTask;
            });
        }
        catch (EditorRevisionChangedException) { wasStale = true; }
        Assert.IsTrue(wasStale);
        Assert.AreEqual(0, staleCallbacks);
        CollectionAssert.AreEqual(new[] { "A", "New", "B" }, events);
    }

    [TestMethod]
    public async Task RemovingARejectsOldApplyBInsteadOfEditingC()
    {
        var gate = new EditorOperationGate();
        var revision = 0;
        var events = new List<string> { "A", "B", "C" };
        Assert.IsTrue(await gate.TryRunAsync(0, () => revision, () =>
        {
            events.RemoveAt(0);
            revision++;
            return Task.CompletedTask;
        }));

        var rejected = false;
        try
        {
            await gate.TryRunAsync(0, () => revision, () =>
            {
                events[1] = "Old B values";
                return Task.CompletedTask;
            });
        }
        catch (EditorRevisionChangedException) { rejected = true; }
        Assert.IsTrue(rejected);
        CollectionAssert.AreEqual(new[] { "B", "C" }, events);
        Assert.IsFalse(gate.IsBusy);
        Assert.IsTrue(await gate.TryRunAsync(1, () => revision, () => Task.CompletedTask));
    }

    [TestMethod]
    public async Task ForeignPublicationWhileWaitingForEditLockIsDetectedInsideTransaction()
    {
        var gate = new EditorOperationGate();
        using var edits = new SemaphoreSlim(0, 1);
        var waitingForEdits = Signal();
        var revision = 0;
        var didEdit = false;
        var operation = gate.TryRunAsync(0, () => revision, async () =>
        {
            waitingForEdits.SetResult(true);
            await edits.WaitAsync();
            try
            {
                // Same second check used by CommitAsync/UndoAsync after they
                // acquire the edit lock: the entry check alone is insufficient.
                EditorOperationGate.EnsureRevision(0, revision);
                didEdit = true;
            }
            finally { edits.Release(); }
        });
        await waitingForEdits.Task.WaitAsync(TimeSpan.FromSeconds(10));
        revision++;
        edits.Release();
        var rejected = false;
        try { await operation.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (EditorRevisionChangedException) { rejected = true; }

        Assert.IsTrue(rejected);
        Assert.IsFalse(didEdit);
        Assert.IsFalse(gate.IsBusy);
        Assert.AreEqual(1, edits.CurrentCount);
    }

    [TestMethod]
    public async Task ExceptionDuringHeldConfirmationReleasesEditorGate()
    {
        var gate = new EditorOperationGate();
        var confirming = Signal();
        var finishConfirmation = Signal();
        var failure = new InvalidOperationException("Dialog failed");
        var operation = gate.TryRunAsync(0, () => 0, async () =>
        {
            confirming.SetResult(true);
            await finishConfirmation.Task;
            throw failure;
        });
        await confirming.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondRan = false;
        var accepted = await gate.TryRunAsync(0, () => 0, () =>
        {
            secondRan = true;
            return Task.CompletedTask;
        });
        finishConfirmation.SetResult(true);
        Exception? observed = null;
        try { await operation.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception exception) { observed = exception; }

        Assert.IsFalse(accepted);
        Assert.IsFalse(secondRan);
        Assert.AreSame(failure, observed);
        Assert.IsFalse(gate.IsBusy);
        Assert.IsTrue(await gate.TryRunAsync(0, () => 0, () => Task.CompletedTask));
    }

    [TestMethod]
    [DataRow((int)EditorChangeKind.MasterGainOnly)]
    [DataRow((int)EditorChangeKind.GlobalTempoOnly)]
    [DataRow((int)EditorChangeKind.PracticeTimerOnly)]
    [DataRow((int)EditorChangeKind.SoundAndAccentsOnly)]
    [DataRow((int)EditorChangeKind.InPlaceField)]
    public async Task GlobalScalarPublicationWhileWaitingForEditsKeepsExistingEditorTokenValid(int changeValue)
    {
        var change = (EditorChangeKind)changeValue;
        var gate = new EditorOperationGate();
        using var edits = new SemaphoreSlim(0, 1);
        var waiting = Signal();
        var documentRevision = 12;
        var editorRevision = 5;
        var capturedEditorRevision = editorRevision;
        var didApplyDraft = false;
        var operation = gate.TryRunAsync(capturedEditorRevision, () => editorRevision, async () =>
        {
            waiting.SetResult(true);
            await edits.WaitAsync();
            try
            {
                // Production's second check under _edits must also tolerate
                // a benign publication occurring after command admission.
                EditorOperationGate.EnsureRevision(capturedEditorRevision, editorRevision);
                didApplyDraft = true;
            }
            finally { edits.Release(); }
        });
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        documentRevision++;
        editorRevision = EditorRevisionPolicy.Advance(editorRevision, change);
        edits.Release();
        Assert.IsTrue(await operation.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.AreEqual(13, documentRevision);
        Assert.AreEqual(capturedEditorRevision, editorRevision);
        Assert.IsTrue(didApplyDraft);
        Assert.IsFalse(gate.IsBusy);
        // The same actual policy is used by CommitAsync's render callback.
        // Neither successful persistence nor recovery failure rebuilds Studio
        // for these changes; its existing controls/drafts/focus stay in place.
        Assert.IsFalse(EditorRevisionPolicy.ShouldRebuild(true, change));
        Assert.IsFalse(EditorRevisionPolicy.ShouldRebuild(false, change));
    }

    [TestMethod]
    public async Task ConflictingPublicationStillInvalidatesEditorAfterBenignChanges()
    {
        var gate = new EditorOperationGate();
        var revision = 5;
        var capturedEditorRevision = revision;
        revision = EditorRevisionPolicy.Advance(revision, EditorChangeKind.MasterGainOnly);
        // Default covers lane/event/program edits (including their gain/tempo
        // fields), reordering, add/remove, replacement, Next and undo/redo.
        revision = EditorRevisionPolicy.Advance(revision);
        revision = EditorRevisionPolicy.Advance(revision, EditorChangeKind.GlobalTempoOnly);
        var applied = false;
        var rejected = false;
        try
        {
            await gate.TryRunAsync(capturedEditorRevision, () => revision, () =>
            {
                applied = true;
                return Task.CompletedTask;
            });
        }
        catch (EditorRevisionChangedException) { rejected = true; }

        Assert.AreEqual(6, revision);
        Assert.IsTrue(rejected);
        Assert.IsFalse(applied);
        Assert.IsFalse(gate.IsBusy);
        Assert.IsTrue(EditorRevisionPolicy.ShouldRebuild(false, EditorChangeKind.Document));
        Assert.IsFalse(EditorRevisionPolicy.ShouldRebuild(true, EditorChangeKind.Document));
    }

    private static TaskCompletionSource<bool> Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
