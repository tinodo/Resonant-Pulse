using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class TimingAnalyzerTests
{
    [TestMethod]
    public void Analyze_EmptyNoMatchAndExactInputs_ReturnsExpectedCountsAndCoverage()
    {
        foreach (var pair in new[]
        {
            (Array.Empty<double>(), Array.Empty<double>(), 0, 0, 0),
            (new[] { 1d }, Array.Empty<double>(), 0, 1, 0),
            (Array.Empty<double>(), new[] { 1d }, 0, 0, 1),
            (new[] { 1d }, new[] { 2d }, 0, 1, 1),
            (new[] { 1d, 2d }, new[] { 1d, 2d }, 2, 0, 0)
        })
        {
            var r = TimingAnalyzer.Analyze(pair.Item1, pair.Item2);
            Assert.AreEqual(pair.Item3, r.Matched); Assert.AreEqual(pair.Item4, r.Missed);
            Assert.AreEqual(pair.Item5, r.Extra); Assert.AreEqual(pair.Item3, r.Matches.Count);
            Assert.AreEqual(pair.Item3 == 0 ? 0d : 1d, r.Coverage);
            Assert.AreEqual(0d, r.MedianOffsetMilliseconds); Assert.AreEqual(0d, r.SpreadMilliseconds);
            Assert.AreEqual(0d, r.DriftMillisecondsPerSecond);
            Assert.IsFalse(r.HasDriftEstimate);
        }
        Assert.AreEqual(0.5, TimingAnalyzer.Analyze(new[] { 1d, 2d }, new[] { 1d }).Coverage);
    }

    [TestMethod]
    public void Analyze_SignedOffsets_ReturnsMedianMadAndDrift()
    {
        var r = TimingAnalyzer.Analyze(new[] { 1d, 2, 3 }, new[] { 0.98, 2, 3.01 });
        Assert.AreEqual(0d, r.MedianOffsetMilliseconds, 1e-9);
        Assert.AreEqual(14.826, r.SpreadMilliseconds, 1e-9);
        Check.Sequence(new[] { -20d, 0, 10 }, r.Matches.Select(m => Math.Round(m.OffsetMilliseconds, 8)));
        var even = TimingAnalyzer.Analyze(new[] { 1d, 2 }, new[] { 0.99, 2.01 });
        Assert.AreEqual(0d, even.MedianOffsetMilliseconds, 1e-9);
        Assert.AreEqual(14.826, even.SpreadMilliseconds, 1e-9);
        var drift = TimingAnalyzer.Analyze(new[] { 0d, 1, 2, 3 }, new[] { 0d, 1.01, 2.02, 3.03 });
        Assert.AreEqual(10d, drift.DriftMillisecondsPerSecond, 1e-9);
        Assert.AreEqual(15d, drift.MedianOffsetMilliseconds, 1e-9);
        Assert.AreEqual(4, drift.Matched);
        Assert.IsTrue(drift.HasDriftEstimate);
    }

    [TestMethod]
    public void Analyze_DriftIsAnEstimateOnlyForFourMatchesAtDifferentMoments()
    {
        // Fewer than four matches, however they are spread: no estimate, and a drift of zero.
        for (var count = 0; count < 4; count++)
        {
            var times = Enumerable.Range(0, count).Select(i => (double)i).ToArray();
            var few = TimingAnalyzer.Analyze(times, times.Select(time => time + 0.01).ToArray());
            Assert.AreEqual(count, few.Matched);
            Assert.IsFalse(few.HasDriftEstimate, $"{count} matches");
            Assert.AreEqual(0d, few.DriftMillisecondsPerSecond);
        }
        // Four matches at one moment: nothing changes over time for the drift to be measured against.
        var coincident = TimingAnalyzer.Analyze([1, 1, 1, 1], [1, 1, 1, 1], 0);
        Assert.AreEqual(4, coincident.Matched);
        Assert.IsFalse(coincident.HasDriftEstimate);
        Assert.AreEqual(0d, coincident.DriftMillisecondsPerSecond);
        // The same when the two halves of the matches are centred on one moment although the matches are not all at it.
        var centred = TimingAnalyzer.Analyze([0, 5, 5, 5, 5, 10], [0, 5, 5, 5, 5, 10], 0);
        Assert.AreEqual(6, centred.Matched);
        Assert.IsFalse(centred.HasDriftEstimate);
        // Four matches whose offsets do not change: the drift is measured, and it is zero.
        var steady = TimingAnalyzer.Analyze([0, 1, 2, 3], [0, 1, 2, 3], 0);
        Assert.IsTrue(steady.HasDriftEstimate);
        Assert.AreEqual(0d, steady.DriftMillisecondsPerSecond);
        // Unmatched presses and clicks do not make four matches.
        var unmatched = TimingAnalyzer.Analyze([0, 1, 2, 3, 4, 5], [0, 1, 2, 3.5, 9], 0.1);
        Assert.AreEqual(3, unmatched.Matched);
        Assert.IsFalse(unmatched.HasDriftEstimate);
    }

    [TestMethod]
    public void Analyze_ADriftTooLargeForANumberIsNoEstimateAndNotInfinity()
    {
        // The four targets are about 1e-320 seconds apart, so the change in offset per second overflows a double.
        var overflow = TimingAnalyzer.Analyze([0, 1e-320, 2e-320, 3e-320], [1, 1.01, 1.02, 1.03], 2);
        Assert.AreEqual(4, overflow.Matched);
        Assert.IsFalse(overflow.HasDriftEstimate);
        Assert.AreEqual(0d, overflow.DriftMillisecondsPerSecond);
        Assert.IsTrue(double.IsFinite(overflow.MedianOffsetMilliseconds) && double.IsFinite(overflow.SpreadMilliseconds));
    }

    [TestMethod]
    public void Analyze_MaximumCardinalityBeforeCost_SelectsOptimalOneToOneMatches()
    {
        var r = TimingAnalyzer.Analyze(new[] { 0d, 0.2 }, new[] { 0.14, 0.21 });
        Assert.AreEqual(2, r.Matched); Assert.AreEqual(0, r.Extra);
        Check.Sequence(new[] { (0, 0), (1, 1) }, r.Matches.Select(m => (m.ExpectedIndex, m.ObservedIndex)));
        var cost = TimingAnalyzer.Analyze(new[] { 0d, 1 }, new[] { 0.01, 0.2, 1.01 }, 0.25);
        Check.Sequence(new[] { 0, 2 }, cost.Matches.Select(m => m.ObservedIndex));
        Assert.AreEqual(1, cost.Extra); Assert.AreEqual(10d, cost.MedianOffsetMilliseconds, 1e-9);
    }

    [TestMethod]
    public void Analyze_UnsortedAndDuplicateInputs_PreservesOriginalIndicesAndInputs()
    {
        double[] expected = [2, 0, 1]; double[] observed = [1, 2, 0];
        var r = TimingAnalyzer.Analyze(expected, observed, 0);
        Check.Sequence(new[] { (1, 2), (2, 0), (0, 1) }, r.Matches.Select(m => (m.ExpectedIndex, m.ObservedIndex)));
        Check.Sequence(new[] { 2d, 0, 1 }, expected); Check.Sequence(new[] { 1d, 2, 0 }, observed);
        var duplicates = TimingAnalyzer.Analyze(new[] { 1d, 1 }, new[] { 1d, 1, 1 }, 0);
        Assert.AreEqual(2, duplicates.Matched); Assert.AreEqual(1, duplicates.Extra);
        Assert.AreEqual(2, duplicates.AmbiguousExpected);
        Assert.AreEqual(2, duplicates.Matches.Select(m => m.ObservedIndex).Distinct().Count());
    }

    [TestMethod]
    public void Analyze_ToleranceZeroDefaultAndInclusiveBoundary_UsesInclusiveCandidates()
    {
        Assert.AreEqual(1, TimingAnalyzer.Analyze(new[] { 0d }, new[] { 0.125 }, 0.125).Matched);
        Assert.AreEqual(0, TimingAnalyzer.Analyze(new[] { 0d }, new[] { Math.BitIncrement(0.125) }, 0.125).Matched);
        Assert.AreEqual(1, TimingAnalyzer.Analyze(new[] { 0d }, new[] { 0.15 }).Matched);
        Assert.AreEqual(0, TimingAnalyzer.Analyze(new[] { 0d }, new[] { 0.001 }, 0).Matched);
        Assert.AreEqual(1, TimingAnalyzer.Analyze(new[] { 0d }, new[] { 10d }, 10).Matched);
        foreach (double tolerance in new[] { -1d, Math.BitIncrement(10), double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Check.Throws<ArgumentException>(() => TimingAnalyzer.Analyze(Array.Empty<double>(), Array.Empty<double>(), tolerance));
    }

    [TestMethod]
    [DataRow(1d, 0.85, 0)]
    [DataRow(1d, 1.15, 1)]
    [DataRow(0.85, 1d, 0)]
    [DataRow(1.15, 1d, 1)]
    public void Analyze_DecimalToleranceBoundary_AmbiguityUsesMatchingDistance(
        double expected, double boundaryObservation, int ambiguity)
    {
        var report = TimingAnalyzer.Analyze([expected], [boundaryObservation, expected]);
        Assert.AreEqual(1, report.Matched);
        Assert.AreEqual(1, report.Extra);
        Assert.AreEqual(1, report.Matches[0].ObservedIndex);
        Assert.AreEqual(ambiguity, report.AmbiguousExpected);
    }

    [TestMethod]
    public void Analyze_InvalidTimestampsAndNullInputs_Rejects()
    {
        Check.Throws<ArgumentNullException>(() => TimingAnalyzer.Analyze(null!, Array.Empty<double>()));
        Check.Throws<ArgumentNullException>(() => TimingAnalyzer.Analyze(Array.Empty<double>(), null!));
        foreach (double bad in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Check.Throws<ArgumentException>(() => TimingAnalyzer.Analyze(new[] { bad }, new[] { 0d }));
            Check.Throws<ArgumentException>(() => TimingAnalyzer.Analyze(new[] { 0d }, new[] { bad }));
        }
    }

    [TestMethod]
    public void Analyze_InputAndComparisonBudgets_RejectsBeforeExcessiveWork()
    {
        // Budget includes the extra DP boundary row/column: 2000x2000 inputs are NOT admissible.
        var accepted = TimingAnalyzer.Analyze(new double[100000], Array.Empty<double>());
        Assert.AreEqual(100000, accepted.Missed); Assert.AreEqual(0, accepted.Matched);
        Check.Throws<ArgumentException>(() => TimingAnalyzer.Analyze(new double[100001], Array.Empty<double>()));
        Check.Throws<ArgumentException>(() => TimingAnalyzer.Analyze(Array.Empty<double>(), new double[100001]));
        Check.Throws<ArgumentException>(() => TimingAnalyzer.Analyze(new double[2000], new double[2000]));
    }

    [TestMethod]
    public void TimingMatch_Offsets_ExposeSignedMilliseconds()
    {
        var match = new TimingMatch(4, 7, 1, 1.025);
        Assert.AreEqual(25d, match.OffsetMilliseconds, 1e-9);
        Assert.AreEqual(4, match.ExpectedIndex); Assert.AreEqual(7, match.ObservedIndex);
        Assert.AreEqual(-25d, new TimingMatch(0, 1, 1.025, 1).OffsetMilliseconds, 1e-9);
        Assert.AreEqual(0, new TimingReport().Matches.Count);
    }

    [TestMethod]
    public void Analyze_ExactFourMillionCells_AcceptsBoundaryAndRejectsNextColumn()
    {
        // 2000*2000 cells including boundaries; decisions consume four MB, not a giant alignment harness.
        double[] times = Enumerable.Range(0, 1999).Select(i => (double)i).ToArray();
        var exact = TimingAnalyzer.Analyze(times, times, 0);
        Assert.AreEqual(1999, exact.Matched); Assert.AreEqual(1d, exact.Coverage);
        Assert.AreEqual(0, exact.AmbiguousExpected); Assert.AreEqual(0d, exact.SpreadMilliseconds);
        Check.Throws<ArgumentException>(() => TimingAnalyzer.Analyze(times, new double[2000]));
    }
}
