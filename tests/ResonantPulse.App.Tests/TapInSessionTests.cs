using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class TapInSessionTests
{
    [TestMethod]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(16)]
    public void OnlyChosenNumberOfValidTapsCreatesOnePendingStart(int required)
    {
        var session = new TapInSession();
        session.Arm(required);
        for (var tap = 0; tap < required; tap++)
        {
            Assert.AreEqual(tap == required - 1, session.Tap(tap * 0.75));
            Assert.AreEqual(tap + 1, session.ValidTaps);
        }
        Assert.AreEqual(80d, session.Bpm);
        Assert.IsFalse(session.IsArmed);
        Assert.IsTrue(session.IsCurrent(session.Generation));
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Tap(required * 0.75));
        var generation = session.Generation;
        session.Finish(generation);
        Assert.IsFalse(session.IsPending);
        Assert.IsFalse(session.IsCurrent(generation));
    }

    [TestMethod]
    public void BounceAndEarlyOutlierBreakConsecutiveRunAndNeverStart()
    {
        var session = new TapInSession();
        session.Arm(4);
        Assert.IsFalse(session.Tap(0));
        Assert.IsFalse(session.Tap(0.5));
        Assert.IsFalse(session.Tap(0.5));
        Assert.IsTrue(session.LastTapRejected);
        Assert.AreEqual(0, session.ValidTaps);
        Assert.IsFalse(session.Tap(1));
        Assert.AreEqual(1, session.ValidTaps);
        Assert.IsFalse(session.Tap(1.125));
        Assert.IsTrue(session.LastTapRejected);
        Assert.AreEqual(0, session.ValidTaps);
        foreach (var time in new[] { 1.625, 2.125, 2.625 }) Assert.IsFalse(session.Tap(time));
        Assert.IsTrue(session.Tap(3.125));
        Assert.AreEqual(120d, session.Bpm);
    }

    [TestMethod]
    public void IdleStartsNewRunRatherThanFinishingOldIntent()
    {
        var session = new TapInSession();
        session.Arm(4);
        session.Tap(0); session.Tap(0.5); session.Tap(1);
        Assert.IsFalse(session.Tap(6));
        Assert.AreEqual(1, session.ValidTaps);
        Assert.IsNull(session.Bpm);
        Assert.IsFalse(session.Tap(6.5));
        Assert.IsFalse(session.Tap(7));
        Assert.IsTrue(session.Tap(7.5));
    }

    [TestMethod]
    public async Task CancelOrRearmInvalidatesDelayedStartAndOldCompletion()
    {
        var session = new TapInSession();
        session.Arm(2); session.Tap(0); session.Tap(0.5);
        var old = session.Generation;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> DelayedStart()
        {
            await release.Task;
            return session.IsCurrent(old);
        }
        var start = DelayedStart();
        session.Cancel();
        session.Arm(4);
        session.Tap(1);
        session.Finish(old);
        release.SetResult();
        Assert.IsFalse(await start);
        Assert.IsTrue(session.IsArmed);
        Assert.AreEqual(1, session.ValidTaps);
        Assert.AreEqual(4, session.RequiredTaps);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(17)]
    public void InvalidTargetDoesNotDestroyArmedSession(int required)
    {
        var session = new TapInSession();
        session.Arm(4); session.Tap(0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => session.Arm(required));
        Assert.IsTrue(session.IsArmed);
        Assert.AreEqual(1, session.ValidTaps);
    }

    [TestMethod]
    public void IntentionalNewTempoStillRequiresConsecutiveAcceptedTaps()
    {
        var session = new TapInSession();
        session.Arm(4);
        session.Tap(0); session.Tap(0.5);
        Assert.IsFalse(session.Tap(1.25));
        Assert.IsFalse(session.Tap(2));
        Assert.AreEqual(0, session.ValidTaps);
        Assert.IsFalse(session.Tap(2.75));
        Assert.AreEqual(1, session.ValidTaps);
        Assert.IsFalse(session.Tap(3.5));
        Assert.IsFalse(session.Tap(4.25));
        Assert.IsTrue(session.Tap(5));
        Assert.AreEqual(80d, session.Bpm);
    }

    [TestMethod]
    public void LostOutputIntentCannotReturnWithReconnectedEndpoint()
    {
        var session = new TapInSession();
        session.Arm(2); session.Tap(0); session.Tap(0.5);
        var pending = session.Generation;
        session.Cancel();
        Assert.IsFalse(session.IsCurrent(pending));
        Assert.IsFalse(session.IsArmed);
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Tap(1));
        session.Arm(2);
        Assert.IsFalse(session.Tap(2));
        Assert.IsTrue(session.Tap(2.5));
        Assert.IsTrue(session.IsCurrent(session.Generation));
        Assert.IsFalse(session.IsCurrent(pending));
    }
}
