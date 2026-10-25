using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class TempoCurveStateTests
{
    [TestMethod]
    public void PreviewCompiledDuringPlaybackReturnsAtZeroOnStopAndSubsequentNullRefreshes()
    {
        var state = new TempoCurveState();
        var active = Plan(120);
        var editedPreview = Plan(90);
        Assert.IsTrue(state.TryBegin(2));
        Assert.IsTrue(state.TryComplete(2, 2, editedPreview));

        // The edited preview is retained even while an older audio revision
        // owns the visible curve, cursor and blind-gap state.
        var playing = state.Select(2, active, 24000, true);
        Assert.AreSame(active, playing.Plan);
        Assert.AreEqual(24000L, playing.Frame);
        Assert.IsTrue(playing.IsBlind);
        for (var refresh = 0; refresh < 10; refresh++)
        {
            var stopped = state.Select(2, null, 24000, true);
            Assert.AreSame(editedPreview, stopped.Plan);
            Assert.AreEqual(0L, stopped.Frame);
            Assert.IsFalse(stopped.IsBlind);
            Assert.IsFalse(state.TryBegin(2));
        }
    }

    [TestMethod]
    public void DocumentRevisionChangeHidesOldPreviewAndRejectsStaleCompilation()
    {
        var state = new TempoCurveState();
        var oldPreview = Plan(100);
        var currentPreview = Plan(150);
        Assert.IsTrue(state.TryBegin(1));
        Assert.IsTrue(state.TryComplete(1, 1, oldPreview));
        Assert.IsNull(state.Select(2, null, 8000, false).Plan);
        Assert.IsTrue(state.TryBegin(2));
        // The document changes again while revision 2 compiles.
        Assert.IsFalse(state.TryComplete(2, 3, oldPreview));
        Assert.IsNull(state.Select(3, null, 8000, false).Plan);
        Assert.IsTrue(state.TryBegin(3));
        Assert.IsTrue(state.TryComplete(3, 3, currentPreview));
        Assert.IsFalse(state.TryComplete(2, 3, oldPreview));
        var current = state.Select(3, null, 8000, false);
        Assert.AreSame(currentPreview, current.Plan);
        Assert.AreEqual(0L, current.Frame);
    }

    [TestMethod]
    public void LateCompletionCannotReleaseOrOverwriteNewerPendingRequest()
    {
        var state = new TempoCurveState();
        var currentPreview = Plan(160);
        Assert.IsTrue(state.TryBegin(1));
        Assert.IsFalse(state.TryComplete(1, 2, Plan(80)));
        Assert.IsTrue(state.TryBegin(2));
        Assert.IsFalse(state.TryComplete(1, 2, Plan(90)));
        Assert.IsFalse(state.TryBegin(3));
        Assert.IsTrue(state.TryComplete(2, 2, currentPreview));
        Assert.AreSame(currentPreview, state.Select(2, null, 0, false).Plan);
    }

    [TestMethod]
    public void PreviewCannotReplaceActivePlanEvenWhenCurrentPreviewIsMissingOrStale()
    {
        var state = new TempoCurveState();
        var active = Plan(110);
        Assert.IsTrue(state.TryBegin(1));
        Assert.IsTrue(state.TryComplete(1, 1, Plan(200)));
        var retained = state.Select(2, active, active.TotalFrames + 100, false);
        Assert.AreSame(active, retained.Plan);
        Assert.AreEqual(active.TotalFrames - 1, retained.Frame);
        Assert.IsFalse(retained.IsBlind);
        var stopped = state.Select(2, null, retained.Frame, true);
        Assert.IsNull(stopped.Plan);
        Assert.AreEqual(0L, stopped.Frame);
        Assert.IsFalse(stopped.IsBlind);
    }

    [TestMethod]
    public void CompilationIsSingleFlightAndFailureDoesNotRetryOnEveryRefresh()
    {
        var state = new TempoCurveState();
        Assert.IsTrue(state.TryBegin(1));
        Assert.IsFalse(state.TryBegin(1));
        Assert.IsFalse(state.TryBegin(2));
        Assert.IsFalse(state.TryComplete(1, 1, null));
        for (var refresh = 0; refresh < 10; refresh++)
            Assert.IsFalse(state.TryBegin(1));
        Assert.IsTrue(state.TryBegin(2));
        Assert.IsTrue(state.TryComplete(2, 2, Plan(130)));
        Assert.IsFalse(state.TryBegin(2));
    }

    private static RenderPlan Plan(double tempo)
    {
        var document = new PulseDocument();
        document.Settings.Tempo = tempo;
        return ScheduleCompiler.Compile(document, 48000, 1, false);
    }
}
