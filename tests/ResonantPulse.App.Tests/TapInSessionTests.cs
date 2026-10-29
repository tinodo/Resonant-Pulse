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
        session.Arm(required, "output");
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
        session.Arm(4, "output");
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
        session.Arm(4, "output");
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
        session.Arm(2, "output"); session.Tap(0); session.Tap(0.5);
        var old = session.Generation;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> DelayedStart()
        {
            await release.Task;
            return session.IsCurrent(old);
        }
        var start = DelayedStart();
        session.Cancel();
        session.Arm(4, "output");
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
        session.Arm(4, "output"); session.Tap(0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => session.Arm(required, "output"));
        Assert.IsTrue(session.IsArmed);
        Assert.AreEqual(1, session.ValidTaps);
    }

    [TestMethod]
    public void IntentionalNewTempoStillRequiresConsecutiveAcceptedTaps()
    {
        var session = new TapInSession();
        session.Arm(4, "output");
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
    [DataRow(false)]
    [DataRow(true)]
    public void LostOutputIntentCannotReturnWithReconnectedEndpoint(bool pendingStart)
    {
        var session = new TapInSession();
        session.Arm(2, "output"); session.Tap(0);
        if (pendingStart) session.Tap(0.5);
        var pending = session.Generation;
        void DeviceChanged(string id)
        {
            if (session.IsAffectedByOutput(id)) session.Cancel();
        }
        DeviceChanged("other-output");
        Assert.AreEqual(pendingStart, session.IsPending);
        Assert.AreEqual(!pendingStart, session.IsArmed);
        DeviceChanged("OUTPUT");
        Assert.IsFalse(session.IsCurrent(pending));
        Assert.IsFalse(session.IsArmed);
        Assert.IsFalse(session.IsPending);
        Assert.IsNull(session.Bpm);
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Tap(1));
        Assert.IsFalse(session.IsAffectedByOutput("output"));
        session.Arm(2, "output");
        Assert.IsFalse(session.Tap(2));
        Assert.IsTrue(session.Tap(2.5));
        Assert.IsTrue(session.IsCurrent(session.Generation));
        Assert.IsFalse(session.IsCurrent(pending));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public void InvalidOutputDoesNotDestroyArmedSession(string outputId)
    {
        var session = new TapInSession();
        session.Arm(4, "output"); session.Tap(0);
        var generation = session.Generation;
        Assert.ThrowsExactly<ArgumentException>(() => session.Arm(2, outputId));
        Assert.AreEqual(generation, session.Generation);
        Assert.IsTrue(session.IsArmed);
        Assert.IsTrue(session.IsAffectedByOutput("output"));
        Assert.AreEqual(4, session.RequiredTaps);
        Assert.AreEqual(1, session.ValidTaps);
    }

    [TestMethod]
    public void TheStartTempoIsRoundedToTenthsLikeAPlainTap()
    {
        // 60 / 0.623 = 96.308..., 60 / 0.62 = 96.774...: the tempo that starts is the one the dial shows after a plain tap.
        foreach (var (interval, tempo) in new[] { (0.623, 96.3), (0.62, 96.8), (0.75, 80d), (0.5, 120d) })
        {
            var session = new TapInSession();
            session.Arm(2, "output");
            session.Tap(0);
            Assert.IsTrue(session.Tap(interval));
            Assert.AreEqual(tempo, session.Tempo!.Value, 1e-9, $"{interval} s");
            Assert.AreEqual(60 / interval, session.Bpm!.Value, 1e-9, "the estimate itself is not rounded");
        }
    }

    [TestMethod]
    public void ThereIsNoTempoUntilTheEstimatorHasAnInterval()
    {
        var session = new TapInSession();
        Assert.IsNull(session.Tempo);
        session.Arm(4, "output");
        Assert.IsNull(session.Tempo);
        session.Tap(0);
        Assert.IsNull(session.Tempo, "one tap is not an interval");
        session.Tap(0.5);
        Assert.AreEqual(120d, session.Tempo);
        session.Cancel();
        Assert.IsNull(session.Tempo, "a cancelled run keeps no estimate");
    }

    [TestMethod]
    public void TheDialShowsTheEstimateOnlyForARunThatIsNotBroken()
    {
        var session = new TapInSession();
        Assert.IsNull(session.ShownTempo, "not armed");
        session.Arm(4, "output");
        Assert.IsNull(session.ShownTempo, "nothing tapped yet");
        session.Tap(0);
        Assert.IsNull(session.ShownTempo, "one tap is not an interval");
        session.Tap(0.5);
        Assert.AreEqual(120d, session.ShownTempo);
        // An uneven tap ends the run: the estimate that is left over is not what a start would use, so it must not stay on the dial.
        session.Tap(3.5);
        Assert.IsTrue(session.LastTapRejected);
        Assert.IsNull(session.ShownTempo);
        session.Tap(4);
        Assert.IsNull(session.ShownTempo, "one accepted tap since the run ended");
        session.Tap(4.5);
        Assert.AreEqual(120d, session.ShownTempo);
        session.Cancel();
        Assert.IsNull(session.ShownTempo);
    }

    [TestMethod]
    public void TheDialKeepsTheEstimateWhileTheStartIsOnItsWay()
    {
        var session = new TapInSession();
        session.Arm(2, "output");
        session.Tap(0);
        Assert.IsTrue(session.Tap(0.5));
        Assert.IsTrue(session.IsPending);
        Assert.AreEqual(120d, session.ShownTempo, "the tempo being started with, not the old one for a moment");
        session.Finish(session.Generation);
        Assert.IsNull(session.ShownTempo);
    }

    [TestMethod]
    public void APauseOfMoreThanFourSecondsBeginsANewCountHoweverSlowTheTapsWere()
    {
        // The estimator starts again only after the longer of four seconds and three times the beat, which for slow taps is
        // more than four seconds. Tap to start promises four, so that someone who stopped tapping does not find the metronome
        // started by a tap made much later: these taps are 3.5, 3.5 and 4.25 seconds apart.
        var session = new TapInSession();
        session.Arm(4, "output");
        Assert.IsFalse(session.Tap(0));
        Assert.IsFalse(session.Tap(3.5));
        Assert.IsFalse(session.Tap(7));
        Assert.IsFalse(session.Tap(11.25), "4.25 seconds after the last tap is the first tap of a new count");
        Assert.IsTrue(session.IsArmed);
        Assert.IsFalse(session.IsPending);
        Assert.AreEqual(1, session.ValidTaps);
        Assert.IsFalse(session.LastTapRejected, "a pause is not an uneven tap");
        Assert.IsNull(session.Tempo, "one tap is not a tempo");
    }

    [TestMethod]
    public void TapsExactlyFourSecondsApartAreOneCountAndAGapJustOverFourIsNot()
    {
        var steady = new TapInSession();
        steady.Arm(4, "output");
        steady.Tap(0); steady.Tap(4); steady.Tap(8);
        Assert.IsTrue(steady.Tap(12));
        Assert.AreEqual(15d, steady.Tempo);

        var late = new TapInSession();
        late.Arm(2, "output");
        late.Tap(0);
        Assert.IsFalse(late.Tap(4.01), "a gap of more than four seconds starts the count again");
        Assert.AreEqual(1, late.ValidTaps);
        Assert.IsTrue(late.Tap(4.51), "and the taps after it count from there");
    }

    [TestMethod]
    public void ArmingAgainStartsFromScratchWithTheNewRequiredCount()
    {
        var session = new TapInSession();
        session.Arm(3, "output");
        session.Tap(0); session.Tap(0.5);
        var generation = session.Generation;
        session.Arm(5, "output");
        Assert.AreNotEqual(generation, session.Generation);
        Assert.AreEqual(5, session.RequiredTaps);
        Assert.AreEqual(0, session.ValidTaps);
        Assert.IsNull(session.Bpm);
        Assert.IsFalse(session.LastTapRejected);
    }

    [TestMethod]
    public void AFinishedSessionCannotBeTappedOrFinishedTwice()
    {
        var session = new TapInSession();
        session.Arm(2, "output");
        session.Tap(0);
        Assert.IsTrue(session.Tap(0.5));
        var generation = session.Generation;
        session.Finish(generation);
        var afterFinish = session.Generation;
        session.Finish(generation);
        Assert.AreEqual(afterFinish, session.Generation, "a second Finish of the same run changes nothing");
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Tap(1));
    }
}
