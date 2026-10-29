using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class TapTempoEstimatorTests
{
    [TestMethod]
    [DataRow(0.5, 120.0)]
    [DataRow(0.75, 80.0)]
    public void Tap_FirstAndRegularIntervals_ReturnsNullThenSelectedBpm(double interval, double bpm)
    {
        var tap = new TapTempoEstimator();
        Assert.IsNull(tap.Tap(0)); Assert.IsTrue(tap.LastTapAccepted);
        Assert.AreEqual(0, tap.AcceptedIntervals);
        for (int i = 1; i <= 4; i++)
        {
            Assert.AreEqual(bpm, tap.Tap(i * interval)!.Value, 1e-10);
            Assert.AreEqual(i, tap.AcceptedIntervals);
            Assert.IsTrue(tap.LastTapAccepted);
        }
        Assert.AreEqual(bpm, tap.CurrentBpm!.Value, 1e-10);
        Assert.AreEqual(0, tap.RejectedTaps);
    }

    [TestMethod]
    public void Tap_NonfiniteNegativeAndNonincreasingInputs_RejectsWithoutCorruptingState()
    {
        var tap = new TapTempoEstimator(); tap.Tap(0); tap.Tap(0.5);
        foreach (var value in new[] { -1d, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            Check.Throws<ArgumentOutOfRangeException>(() => tap.Tap(value));
        Assert.AreEqual(120d, tap.Tap(0.5)!.Value);
        Assert.IsFalse(tap.LastTapAccepted);
        Assert.AreEqual(120d, tap.Tap(0.25)!.Value);
        Assert.AreEqual(2, tap.RejectedTaps);
        Assert.AreEqual(120d, tap.Tap(1)!.Value);
        Assert.AreEqual(2, tap.AcceptedIntervals);
        foreach (var idle in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
            Check.Throws<ArgumentException>(() => new TapTempoEstimator { IdleResetSeconds = idle }.Tap(0));
    }

    [TestMethod]
    public void Tap_BounceBoundary_DoesNotAdvanceReferenceOnRejection()
    {
        var tap = new TapTempoEstimator(); tap.Tap(0);
        Assert.IsNull(tap.Tap(Math.BitDecrement(0.06)));
        Assert.IsFalse(tap.LastTapAccepted); Assert.AreEqual(1, tap.RejectedTaps);
        Assert.AreEqual(120d, tap.Tap(0.5)!.Value); Assert.AreEqual(1, tap.AcceptedIntervals);
        foreach (var value in new[] { 0.06, Math.BitIncrement(0.06) })
        {
            var boundary = new TapTempoEstimator(); boundary.Tap(0);
            Assert.AreEqual(60 / value, boundary.Tap(value)!.Value, 1e-10);
            Assert.IsTrue(boundary.LastTapAccepted); Assert.AreEqual(0, boundary.RejectedTaps);
        }
    }

    [TestMethod]
    public void Tap_IdleBoundary_ResetsOnlyBeyondEffectiveThreshold()
    {
        var exact = new TapTempoEstimator(); exact.Tap(0);
        Assert.AreEqual(15d, exact.Tap(4)!.Value); Assert.AreEqual(1, exact.AcceptedIntervals);
        var beyond = new TapTempoEstimator(); beyond.Tap(0);
        Assert.IsNull(beyond.Tap(Math.BitIncrement(4)));
        Assert.AreEqual(0, beyond.AcceptedIntervals); Assert.IsTrue(beyond.LastTapAccepted);
        var slow = new TapTempoEstimator { IdleResetSeconds = 20 };
        slow.Tap(0); Assert.AreEqual(6d, slow.Tap(10)!.Value);
        Assert.AreEqual(3d, slow.Tap(40)!.Value); // delta30 == three times previous median.
        Assert.IsNull(slow.Tap(101)); Assert.AreEqual(0, slow.AcceptedIntervals);
        var longIdle = new TapTempoEstimator { IdleResetSeconds = 100 };
        longIdle.Tap(0); Assert.IsNull(longIdle.Tap(61));
        Assert.IsTrue(longIdle.LastTapAccepted);
    }

    [TestMethod]
    public void Tap_IsolatedOutlierAndThreeConsistentChanges_RejectsThenAdapts()
    {
        var tap = new TapTempoEstimator();
        foreach (var t in new[] { 0d, 0.5, 1, 1.5 }) tap.Tap(t);
        Assert.AreEqual(120d, tap.Tap(2.5)!.Value); Assert.IsFalse(tap.LastTapAccepted);
        Assert.AreEqual(120d, tap.Tap(3)!.Value); Assert.IsTrue(tap.LastTapAccepted);
        Assert.AreEqual(4, tap.AcceptedIntervals); // missed tap advanced the reference.
        tap.Tap(3.75); tap.Tap(4.5);
        Assert.IsFalse(tap.LastTapAccepted);
        Assert.AreEqual(80d, tap.Tap(5.25)!.Value); Assert.IsTrue(tap.LastTapAccepted);
        Assert.AreEqual(3, tap.AcceptedIntervals); Assert.AreEqual(3, tap.RejectedTaps);
    }

    [TestMethod]
    public void Tap_ThirteenIntervals_EvictsOldestFromTwelveIntervalMedian()
    {
        var tap = new TapTempoEstimator(); tap.Tap(0);
        // .5 and .625 are binary-exact and within the 30% outlier gate.
        double time = 0;
        for (int i = 0; i < 6; i++) { time += 0.5; tap.Tap(time); }
        for (int i = 0; i < 7; i++) { time += 0.625; tap.Tap(time); }
        Assert.AreEqual(12, tap.AcceptedIntervals);
        Assert.AreEqual(96d, tap.CurrentBpm!.Value);
        Assert.AreEqual(0, tap.RejectedTaps);
    }

    [TestMethod]
    public void Reset_PopulatedEstimator_ClearsAllState()
    {
        var tap = new TapTempoEstimator(); tap.Tap(0); tap.Tap(0.5); tap.Tap(0.5); tap.Reset();
        Assert.IsNull(tap.CurrentBpm); Assert.AreEqual(0, tap.AcceptedIntervals);
        Assert.AreEqual(0, tap.RejectedTaps); Assert.IsFalse(tap.LastTapAccepted);
        Assert.IsNull(tap.Tap(0)); Assert.IsTrue(tap.LastTapAccepted);
        Assert.AreEqual(80d, tap.Tap(0.75)!.Value);
    }

    [TestMethod]
    public void Tap_IntervalsOnEitherSideOfOutlierGate_PreserveRecentMedian()
    {
        var inside = new TapTempoEstimator(); var outside = new TapTempoEstimator();
        foreach (double t in new[] { 0d, 1, 2, 3 }) { inside.Tap(t); outside.Tap(t); }
        Assert.AreEqual(60d, inside.Tap(4.25)!.Value);
        Assert.IsTrue(inside.LastTapAccepted); Assert.AreEqual(4, inside.AcceptedIntervals);
        Assert.AreEqual(60d, outside.Tap(4.5)!.Value);
        Assert.IsFalse(outside.LastTapAccepted); Assert.AreEqual(3, outside.AcceptedIntervals);
        Assert.AreEqual(1, outside.RejectedTaps);
        Assert.AreEqual(60d, outside.Tap(5.5)!.Value); Assert.IsTrue(outside.LastTapAccepted);
    }

    [TestMethod]
    public void Tap_ExactOutlierGateAndAdjacentTimestamps_AcceptsEqualityOnBothSides()
    {
        // Median 1.25 s and its 30% gate .375 s are binary-exact.
        // The lower/upper accepted intervals are .875 and 1.625 seconds.
        foreach (double boundary in new[] { 4.625, 5.375 })
        {
            foreach (bool beyond in new[] { false, true })
            {
                var tap = new TapTempoEstimator();
                foreach (double t in new[] { 0d, 1.25, 2.5, 3.75 }) tap.Tap(t);
                double timestamp = !beyond ? boundary :
                    boundary < 5 ? Math.BitDecrement(boundary) : Math.BitIncrement(boundary);
                Assert.AreEqual(48d, tap.Tap(timestamp)!.Value);
                Assert.AreEqual(!beyond, tap.LastTapAccepted);
                Assert.AreEqual(beyond ? 3 : 4, tap.AcceptedIntervals);
                Assert.AreEqual(beyond ? 1 : 0, tap.RejectedTaps);
            }
        }
    }

    [TestMethod]
    public void RejectEarlyOutliers_AppliesTheOutlierGateAfterTheFirstInterval()
    {
        // 0.5 s then 1.0 s. The regular estimator needs three intervals before it distrusts one, so it takes the second
        // interval and its median moves; an armed tap-in distrusts it at once, because one wrong tap must never start playback.
        var regular = new TapTempoEstimator();
        var strict = new TapTempoEstimator { RejectEarlyOutliers = true };
        foreach (var time in new[] { 0d, 0.5, 1.5 }) { regular.Tap(time); strict.Tap(time); }
        Assert.IsTrue(regular.LastTapAccepted);
        Assert.AreEqual(2, regular.AcceptedIntervals);
        Assert.AreEqual(80d, regular.CurrentBpm!.Value, 1e-10);
        Assert.IsFalse(strict.LastTapAccepted);
        Assert.AreEqual(1, strict.AcceptedIntervals);
        Assert.AreEqual(1, strict.RejectedTaps);
        Assert.AreEqual(120d, strict.CurrentBpm!.Value, 1e-10);
    }

    [TestMethod]
    public void RejectEarlyOutliers_IsOffByDefaultAndChangesNothingForSteadyTaps()
    {
        Assert.IsFalse(new TapTempoEstimator().RejectEarlyOutliers);
        var regular = new TapTempoEstimator();
        var strict = new TapTempoEstimator { RejectEarlyOutliers = true };
        for (var tap = 0; tap < 8; tap++)
        {
            regular.Tap(tap * 0.625);
            strict.Tap(tap * 0.625);
            Assert.IsTrue(strict.LastTapAccepted, $"tap {tap}");
            Assert.AreEqual(regular.CurrentBpm, strict.CurrentBpm, $"tap {tap}");
        }
        Assert.AreEqual(96d, strict.CurrentBpm!.Value, 1e-10);
        Assert.AreEqual(0, strict.RejectedTaps);
    }

    [TestMethod]
    public void RejectEarlyOutliers_StillFollowsThreeConsistentChangesOfTempo()
    {
        // The strict gate must not make an intentional tempo change impossible: three consistent new intervals replace the old ones.
        var tap = new TapTempoEstimator { RejectEarlyOutliers = true };
        tap.Tap(0); tap.Tap(0.5);
        Assert.AreEqual(120d, tap.CurrentBpm!.Value, 1e-10);
        tap.Tap(1.25); Assert.IsFalse(tap.LastTapAccepted);
        tap.Tap(2); Assert.IsFalse(tap.LastTapAccepted);
        Assert.AreEqual(80d, tap.Tap(2.75)!.Value, 1e-10);
        Assert.IsTrue(tap.LastTapAccepted);
        Assert.AreEqual(3, tap.AcceptedIntervals);
    }
}
