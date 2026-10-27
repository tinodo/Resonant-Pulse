using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class OrderedWorkTests
{
    [TestMethod]
    public async Task WorkRunsInTheOrderItWasQueuedEvenWhenTheFirstIsSlow()
    {
        var queue = new OrderedWork();
        var log = new List<string>();
        var first = queue.Run(async () => { log.Add("first start"); await Task.Delay(80); log.Add("first end"); });
        var second = queue.Run(() => { log.Add("second"); return Task.CompletedTask; });
        var third = queue.Run(() => { log.Add("third"); return Task.CompletedTask; });
        await Task.WhenAll(first, second, third);
        CollectionAssert.AreEqual(new[] { "first start", "first end", "second", "third" }, log);
    }

    [TestMethod]
    public async Task OneItemNeverStartsBeforeThePreviousHasFinished()
    {
        var queue = new OrderedWork();
        var running = 0;
        var overlapped = false;
        async Task Item()
        {
            if (Interlocked.Increment(ref running) > 1) overlapped = true;
            await Task.Delay(20);
            Interlocked.Decrement(ref running);
        }
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => queue.Run(Item)));
        Assert.IsFalse(overlapped);
    }

    [TestMethod]
    public async Task AFailureIsReportedToItsOwnCallerAndNeverBlocksTheNextItem()
    {
        var queue = new OrderedWork();
        var failing = queue.Run(() => throw new InvalidOperationException("save failed"));
        var ran = false;
        var next = queue.Run(() => { ran = true; return Task.CompletedTask; });
        await next;
        Assert.IsTrue(ran);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => failing);
    }

    [TestMethod]
    public async Task AnItemQueuedAfterEverythingHasFinishedRunsAtOnce()
    {
        var queue = new OrderedWork();
        await queue.Run(() => Task.CompletedTask);
        var ran = false;
        await queue.Run(() => { ran = true; return Task.CompletedTask; });
        Assert.IsTrue(ran);
    }
}
