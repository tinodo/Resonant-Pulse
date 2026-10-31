using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

/// <summary>The one export of separate tracks that may be going at a time: when it can begin, and how closing the window stops it.</summary>
[TestClass]
public sealed class StemExportRunTests
{
    [TestMethod]
    public void ARunBeginsOnceAtATimeAndEndsWhatBegan()
    {
        var run = new StemExportRun();
        Assert.IsFalse(run.IsRunning);
        Assert.IsTrue(run.TryBegin(out var token));
        Assert.IsTrue(run.IsRunning);
        Assert.IsFalse(token.IsCancellationRequested);
        Assert.IsFalse(run.TryBegin(out var second), "A second run cannot begin while one is going.");
        Assert.IsFalse(second.CanBeCanceled);
        Assert.IsTrue(run.IsRunning, "Refusing a second run does not end the first.");
        run.End();
        Assert.IsFalse(run.IsRunning);
        Assert.IsTrue(run.TryBegin(out var third));
        Assert.IsFalse(third.IsCancellationRequested, "A new run has a token of its own, not the cancelled one of the run before.");
    }

    [TestMethod]
    public void EndingWithoutARunOrTwiceIsHarmless()
    {
        var run = new StemExportRun();
        run.End();
        Assert.IsTrue(run.TryBegin(out _));
        run.End();
        run.End();
        Assert.IsFalse(run.IsRunning);
        Assert.IsTrue(run.TryBegin(out _), "Ending twice did not leave a run behind.");
    }

    [TestMethod]
    public async Task CancellingStopsTheRunAndWaitsUntilItHasEnded()
    {
        var run = new StemExportRun();
        Assert.IsTrue(run.TryBegin(out var token));
        var cancelling = run.CancelAsync(TimeSpan.FromSeconds(30));
        Assert.IsTrue(token.IsCancellationRequested, "The run is told to stop at once.");
        await Task.Delay(100);
        Assert.IsFalse(cancelling.IsCompleted, "Closing waits for the run to clean up (its temporary archive is removed as it stops).");
        run.End();
        Assert.IsTrue(await cancelling.WaitAsync(TimeSpan.FromSeconds(10)), "It reports that nothing is running any more.");
        Assert.IsFalse(run.IsRunning);
    }

    [TestMethod]
    public async Task CancellingGivesUpWhenTheRunDoesNotEndInTimeSoClosingCannotHang()
    {
        var run = new StemExportRun();
        Assert.IsTrue(run.TryBegin(out var token));
        var ended = await run.CancelAsync(TimeSpan.FromMilliseconds(100));
        Assert.IsFalse(ended, "A run that is still going when the limit passes is reported as such.");
        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsTrue(run.IsRunning, "Giving up does not pretend that the run has ended.");
        run.End();
    }

    [TestMethod]
    public async Task CancellingWhenNothingRunsIsAtOnce()
    {
        var run = new StemExportRun();
        Assert.IsTrue(await run.CancelAsync(TimeSpan.FromSeconds(30)).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(run.TryBegin(out var token));
        run.End();
        Assert.IsTrue(await run.CancelAsync(TimeSpan.FromSeconds(30)).WaitAsync(TimeSpan.FromSeconds(5)), "A run that has ended is not waited for.");
        Assert.IsFalse(token.IsCancellationRequested, "A run that had ended was not cancelled afterwards.");
    }

    [TestMethod]
    public async Task ARunThatStopsOnItsTokenEndsBeforeClosingGoesOn()
    {
        var run = new StemExportRun();
        Assert.IsTrue(run.TryBegin(out var token));
        var cleaned = false;
        var export = Task.Run(async () =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { await Task.Delay(50); cleaned = true; }
        });
        var ended = run.CancelAsync(TimeSpan.FromSeconds(30));
        await export.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(ended.IsCompleted, "Closing is not told that the run has ended before the run says so (End).");
        run.End();
        Assert.IsTrue(await ended.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsTrue(cleaned, "The run was told to stop and cleaned up before closing went on.");
    }

    [TestMethod]
    public async Task ACloseThatIsRefusedLeavesTheRunGoing()
    {
        // The window stays when the project could not be saved: the export goes on, and closing is not told it has gone.
        var run = new StemExportRun();
        Assert.IsTrue(run.TryBegin(out var token));
        Assert.IsFalse(await run.StopForCloseAsync(windowWillGo: false, TimeSpan.FromSeconds(30)).WaitAsync(TimeSpan.FromSeconds(5)), "It is still going, and says so at once.");
        Assert.IsFalse(token.IsCancellationRequested, "A refused close does not stop the export.");
        Assert.IsTrue(run.IsRunning);
        run.End();
        Assert.IsTrue(await run.StopForCloseAsync(windowWillGo: false, TimeSpan.FromSeconds(30)).WaitAsync(TimeSpan.FromSeconds(5)), "Nothing is going: it says so.");
    }

    [TestMethod]
    public async Task ACloseThatGoesStopsTheRunAndWaitsForIt()
    {
        var run = new StemExportRun();
        Assert.IsTrue(run.TryBegin(out var token));
        var closing = run.StopForCloseAsync(windowWillGo: true, TimeSpan.FromSeconds(30));
        Assert.IsTrue(token.IsCancellationRequested, "The export is told to stop at once.");
        await Task.Delay(100);
        Assert.IsFalse(closing.IsCompleted, "Closing waits for the export to clean up.");
        run.End();
        Assert.IsTrue(await closing.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [TestMethod]
    public async Task ACloseThatGoesGivesUpWaitingAfterTheLimit()
    {
        var run = new StemExportRun();
        Assert.IsTrue(run.TryBegin(out var token));
        Assert.IsFalse(await run.StopForCloseAsync(windowWillGo: true, TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(10)), "The window does not wait for ever.");
        Assert.IsTrue(token.IsCancellationRequested);
        run.End();
    }
}
