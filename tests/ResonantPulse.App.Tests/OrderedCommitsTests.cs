using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class OrderedCommitsTests
{
    // A change from 16 to 17 was still saving when the field was cleared. The revert showed 16, the save then succeeded, and
    // the field disagreed with the saved value.
    [TestMethod]
    public async Task AClearedFieldGoesBackToTheValueThatWasSavedNotTheOneBeforeIt()
    {
        var commits = new OrderedCommits(16);
        var shown = 16d;
        commits.Attach(value => shown = value);
        var gate = new TaskCompletionSource();
        var save = commits.Propose(17, async _ => { await gate.Task; return true; });
        var cleared = commits.Propose(double.NaN, _ => Task.FromResult(true));
        gate.SetResult();
        await Task.WhenAll(save, cleared);
        Assert.AreEqual(17, commits.Committed);
        Assert.AreEqual(17, shown);
    }

    [TestMethod]
    public async Task AnOlderFailureNeverUndoesANewerSuccess()
    {
        var commits = new OrderedCommits(16);
        var reverts = 0;
        commits.Attach(_ => reverts++);
        var gate = new TaskCompletionSource();
        var first = commits.Propose(17, async _ => { await gate.Task; return false; });
        var second = commits.Propose(20, _ => Task.FromResult(true));
        gate.SetResult();
        await Task.WhenAll(first, second);
        Assert.AreEqual(20, commits.Committed);
        Assert.AreEqual(0, reverts, "The newer edit decides what the field shows, so the older failure leaves it alone.");
    }

    [TestMethod]
    public async Task AFailureOfTheLatestEditPutsTheFieldBackToTheLastCommittedValue()
    {
        var commits = new OrderedCommits(16);
        var shown = 99d;
        commits.Attach(value => shown = value);
        await commits.Propose(17, _ => Task.FromResult(true));
        await commits.Propose(20, _ => Task.FromResult(false));
        Assert.AreEqual(17, commits.Committed);
        Assert.AreEqual(17, shown);
    }

    [TestMethod]
    public async Task AFailureOfTheOnlyPendingEditsLeavesTheOriginalValue()
    {
        var commits = new OrderedCommits(16);
        var gate = new TaskCompletionSource();
        var reverts = new List<double>();
        commits.Attach(reverts.Add);
        var first = commits.Propose(17, async _ => { await gate.Task; return false; });
        var second = commits.Propose(18, _ => Task.FromResult(false));
        gate.SetResult();
        await Task.WhenAll(first, second);
        CollectionAssert.AreEqual(new[] { 16d }, reverts, "Only the latest failure reverts, once, to the original value.");
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    public async Task AnEmptyOrInvalidDraftNeverReachesTheCommit(double draft)
    {
        var commits = new OrderedCommits(16);
        var committed = false;
        var shown = 0d;
        commits.Attach(value => shown = value);
        await commits.Propose(draft, _ => { committed = true; return Task.FromResult(true); });
        Assert.IsFalse(committed);
        Assert.AreEqual(16, shown);
        Assert.AreEqual(16, commits.Committed);
    }

    [TestMethod]
    public async Task ACommitThatThrowsStillRevertsAndReportsTheError()
    {
        var commits = new OrderedCommits(16);
        var shown = 0d;
        commits.Attach(value => shown = value);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            commits.Propose(17, _ => throw new InvalidOperationException("save failed")));
        Assert.AreEqual(16, shown);
        Assert.AreEqual(16, commits.Committed);
    }

    [TestMethod]
    public async Task EditsAreCommittedInTheOrderTheyWereMade()
    {
        var commits = new OrderedCommits(1);
        var order = new List<double>();
        async Task<bool> Slow(double value) { await Task.Delay(40); order.Add(value); return true; }
        Task<bool> Fast(double value) { order.Add(value); return Task.FromResult(true); }
        var first = commits.Propose(2, Slow);
        var second = commits.Propose(3, Fast);
        await Task.WhenAll(first, second);
        CollectionAssert.AreEqual(new[] { 2d, 3d }, order);
        Assert.AreEqual(3, commits.Committed);
    }

    // The review finding: the page is rebuilt while a save is pending (navigate away and back). The replacement field must
    // show what the save finally did, and the field that no longer exists must not be touched.
    [TestMethod]
    public async Task ARebuiltFieldShowsTheOutcomeOfASaveThatWasPendingWhenItWasBuilt()
    {
        var commits = new OrderedCommits(16);
        var discarded = 99d;
        var rebuilt = 32d;
        commits.Attach(value => discarded = value);
        var gate = new TaskCompletionSource();
        var save = commits.Propose(32, async _ => { await gate.Task; return false; });
        commits.Attach(value => rebuilt = value);
        gate.SetResult();
        await save;
        Assert.AreEqual(16, rebuilt, "The replacement field is put back to the value that was really saved.");
        Assert.AreEqual(99, discarded, "The field that is gone is forgotten.");
    }

    [TestMethod]
    public async Task AnEditOnARebuiltFieldWaitsForTheOldSaveAndIsNotUndoneByItsFailure()
    {
        var commits = new OrderedCommits(16);
        var events = new List<string>();
        commits.Attach(_ => events.Add("revert old field"));
        var gate = new TaskCompletionSource();
        var first = commits.Propose(32, async _ => { await gate.Task; events.Add("old save fails"); return false; });
        commits.Attach(_ => events.Add("revert new field"));
        var second = commits.Propose(40, _ => { events.Add("new save"); return Task.FromResult(true); });
        gate.SetResult();
        await Task.WhenAll(first, second);
        CollectionAssert.AreEqual(new[] { "old save fails", "new save" }, events);
        Assert.AreEqual(40, commits.Committed);
    }
}
