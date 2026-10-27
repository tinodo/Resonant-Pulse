using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class OrderedCommitsTests
{
    // The review finding: a change from 16 to 17 was still saving when the field was cleared. The revert showed 16, the save
    // then succeeded, and the field disagreed with the saved value.
    [TestMethod]
    public async Task AClearedFieldGoesBackToTheValueThatWasSavedNotTheOneBeforeIt()
    {
        var commits = new OrderedCommits(16);
        var shown = 16d;
        var gate = new TaskCompletionSource();
        var save = commits.Propose(17, async _ => { await gate.Task; return true; }, () => shown = commits.Committed);
        var cleared = commits.Propose(double.NaN, _ => Task.FromResult(true), () => shown = commits.Committed);
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
        var gate = new TaskCompletionSource();
        var first = commits.Propose(17, async _ => { await gate.Task; return false; }, () => reverts++);
        var second = commits.Propose(20, _ => Task.FromResult(true), () => reverts++);
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
        await commits.Propose(17, _ => Task.FromResult(true), () => shown = commits.Committed);
        await commits.Propose(20, _ => Task.FromResult(false), () => shown = commits.Committed);
        Assert.AreEqual(17, commits.Committed);
        Assert.AreEqual(17, shown);
    }

    [TestMethod]
    public async Task AFailureOfTheOnlyPendingEditsLeavesTheOriginalValue()
    {
        var commits = new OrderedCommits(16);
        var gate = new TaskCompletionSource();
        var reverts = new List<double>();
        var first = commits.Propose(17, async _ => { await gate.Task; return false; }, () => reverts.Add(commits.Committed));
        var second = commits.Propose(18, _ => Task.FromResult(false), () => reverts.Add(commits.Committed));
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
        var reverted = false;
        await commits.Propose(draft, _ => { committed = true; return Task.FromResult(true); }, () => reverted = true);
        Assert.IsFalse(committed);
        Assert.IsTrue(reverted);
        Assert.AreEqual(16, commits.Committed);
    }

    [TestMethod]
    public async Task ACommitThatThrowsStillRevertsAndReportsTheError()
    {
        var commits = new OrderedCommits(16);
        var reverted = false;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            commits.Propose(17, _ => throw new InvalidOperationException("save failed"), () => reverted = true));
        Assert.IsTrue(reverted);
        Assert.AreEqual(16, commits.Committed);
    }

    [TestMethod]
    public async Task EditsAreCommittedInTheOrderTheyWereMade()
    {
        var commits = new OrderedCommits(1);
        var order = new List<double>();
        async Task<bool> Slow(double value) { await Task.Delay(40); order.Add(value); return true; }
        Task<bool> Fast(double value) { order.Add(value); return Task.FromResult(true); }
        var first = commits.Propose(2, Slow, () => { });
        var second = commits.Propose(3, Fast, () => { });
        await Task.WhenAll(first, second);
        CollectionAssert.AreEqual(new[] { 2d, 3d }, order);
        Assert.AreEqual(3, commits.Committed);
    }
}
