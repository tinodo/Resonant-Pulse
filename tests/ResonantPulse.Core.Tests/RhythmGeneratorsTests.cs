using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class RhythmGeneratorsTests
{
    [TestMethod]
    public void Euclidean_ZeroSingletonFullAndInteriorPatterns_ReturnsExactSortedEvents()
    {
        var zero = RhythmGenerators.Euclidean(0, 8, 4);
        Assert.AreEqual(8, zero.Events.Count); Assert.IsTrue(zero.Events.All(e => e.Accent == Accent.Silent));
        var singleton = RhythmGenerators.Euclidean(1, 1, 4);
        Assert.AreEqual(Rational.Zero, singleton.Events.Single().PositionQuarters);
        Assert.AreEqual(Accent.Strong, singleton.Events.Single().Accent);
        var full = RhythmGenerators.Euclidean(4, 4, 4);
        Check.Sequence(new[] { Accent.Strong, Accent.Normal, Accent.Normal, Accent.Normal }, full.Events.Select(e => e.Accent));
        var lane = RhythmGenerators.Euclidean(3, 8, 4);
        Check.Sequence(new Rational[] { 0, new(1, 2), 1, new(3, 2), 2, new(5, 2), 3, new(7, 2) },
            lane.Events.Select(e => e.PositionQuarters));
        Check.Sequence(new[] { Accent.Strong, Accent.Silent, Accent.Silent, Accent.Normal, Accent.Silent, Accent.Silent, Accent.Normal, Accent.Silent },
            lane.Events.Select(e => e.Accent));
        Assert.AreEqual(4d, lane.CycleQuarters); Assert.AreEqual(8, lane.Pulses);
    }

    [TestMethod]
    [DataRow(0, "0/1,3/2,3/1")]
    [DataRow(1, "1/2,2/1,7/2")]
    [DataRow(-1, "1/1,5/2,7/2")]
    [DataRow(8, "0/1,3/2,3/1")]
    [DataRow(-8, "0/1,3/2,3/1")]
    [DataRow(9, "1/2,2/1,7/2")]
    public void Euclidean_SignedAndWholeCycleRotations_NormalizesRotation(int rotation, string hits)
    {
        var lane = RhythmGenerators.Euclidean(3, 8, 4, rotation);
        Assert.AreEqual(hits, string.Join(",", lane.Events.Where(e => e.Accent != Accent.Silent).Select(e => e.PositionQuarters)));
        Assert.AreEqual(8, lane.Events.Select(e => e.PositionQuarters).Distinct().Count());
        Assert.AreEqual(1, lane.Events.Count(e => e.Accent == Accent.Strong));
    }

    [TestMethod]
    public void Euclidean_ArgumentAndDecimalContractBoundaries_RejectsUnsupportedValues()
    {
        foreach (var pair in new[] { (-1, 8), (9, 8), (0, 0), (1, 257) })
            Check.Throws<ArgumentException>(() => RhythmGenerators.Euclidean(pair.Item1, pair.Item2, 4));
        foreach (var cycle in new Rational[] { 0, -1, 4097, new(1, 3) })
            Check.Throws<ArgumentException>(() => RhythmGenerators.Euclidean(1, 8, cycle));
        var lane = RhythmGenerators.Euclidean(256, 256, 4096);
        Assert.AreEqual(256, lane.Events.Count); Assert.AreEqual((Rational)4080, lane.Events[^1].PositionQuarters);
    }

    [TestMethod]
    [DataRow(RudimentKind.SingleStroke, "RLRLRLRLRLRLRLRL", 16)]
    [DataRow(RudimentKind.DoubleStroke, "RRLLRRLLRRLLRRLL", 16)]
    [DataRow(RudimentKind.Paradiddle, "RLRRLRLLRLRRLRLL", 16)]
    [DataRow(RudimentKind.Flam, "RLRLRLRLRLRLRLRL", 32)]
    [DataRow(RudimentKind.Drag, "RLRLRLRLRLRLRLRL", 48)]
    public void Rudiment_EachKind_ReturnsExactHandsAccentsAndPositions(RudimentKind kind, string hands, int count)
    {
        var strokes = RhythmGenerators.Rudiment(kind);
        Assert.AreEqual(count, strokes.Count);
        var main = strokes.Where(s => s.Accent != Accent.Soft).ToArray();
        Assert.AreEqual(hands, string.Concat(main.Select(s => s.Hand == StickingHand.Right ? "R" : "L")));
        Check.Sequence(Enumerable.Range(0, 16).Select(i => new Rational(i, 4)), main.Select(s => s.PositionQuarters));
        Check.Sequence(new Rational[] { 0, 1, 2, 3 }, main.Where(s => s.Accent == Accent.Strong).Select(s => s.PositionQuarters));
        if (kind is RudimentKind.Flam or RudimentKind.Drag)
        {
            var wrapped = strokes.Single(s => s.PositionQuarters == new Rational(127, 32));
            Assert.AreEqual(StickingHand.Left, wrapped.Hand); Assert.AreEqual(Accent.Soft, wrapped.Accent);
            if (kind == RudimentKind.Drag)
                Assert.AreEqual(StickingHand.Left, strokes.Single(s => s.PositionQuarters == new Rational(63, 16)).Hand);
        }
    }

    [TestMethod]
    [DataRow(RudimentKind.SingleStroke)]
    [DataRow(RudimentKind.DoubleStroke)]
    [DataRow(RudimentKind.Paradiddle)]
    [DataRow(RudimentKind.Flam)]
    [DataRow(RudimentKind.Drag)]
    public void RudimentLanes_EachKind_MapsBothHandsWithoutLosingGraceNotes(RudimentKind kind)
    {
        var lanes = RhythmGenerators.RudimentLanes(kind);
        Assert.AreEqual(2, lanes.Count); Assert.AreEqual(0.25f, lanes[0].Pan); Assert.AreEqual(-0.25f, lanes[1].Pan);
        Assert.AreEqual("Wood", lanes[0].Sound); Assert.AreEqual("Glass", lanes[1].Sound);
        var strokes = RhythmGenerators.Rudiment(kind);
        for (int i = 0; i < 2; i++)
            Check.Sequence(strokes.Where(s => s.Hand == (StickingHand)i).Select(s => (s.PositionQuarters, s.Accent)),
                lanes[i].Events.Select(e => (e.PositionQuarters, e.Accent)));
        var d = Check.Document(); d.Settings.Accents = [Accent.Silent]; d.Lanes = lanes.ToList();
        DocumentValidator.Validate(d);
        Assert.AreEqual(strokes.Count, Check.Plan(d).Onsets.Count);
    }

    [TestMethod]
    public void Rudiment_UndefinedKind_RejectsInBothPublicMethods()
    {
        Check.Throws<ArgumentException>(() => RhythmGenerators.Rudiment((RudimentKind)99));
        Check.Throws<ArgumentException>(() => RhythmGenerators.RudimentLanes((RudimentKind)(-1)));
    }

    [TestMethod]
    public void Ladder_AscendingDescendingAndOffGridEndpoint_ReturnsExactFiniteSteps()
    {
        foreach (var vector in new[]
        {
            (80d, 100d, 10d, new[] { 80d, 90, 100 }),
            (100d, 80d, -10d, new[] { 100d, 90, 80 }),
            (80d, 95d, 10d, new[] { 80d, 90, 95 }),
            (80d, 80.3, 0.1, new[] { 80d, 80.1, 80.2, 80.3 })
        })
        {
            var steps = TempoPrograms.Ladder(vector.Item1, vector.Item2, vector.Item3, 2);
            Check.Sequence(vector.Item4, steps.Select(s => s.StartTempo));
            Check.Sequence(vector.Item4, steps.Select(s => s.EndTempo));
            Assert.IsTrue(steps.All(s => s.Bars == 2 && s.Repeats == 1 && s.Ramp == "Constant"));
        }
        var first = TempoPrograms.Ladder(80, 90, 10); first[0].Bars = 99;
        Assert.AreEqual(8, TempoPrograms.Ladder(80, 90, 10)[0].Bars);
    }

    [TestMethod]
    public void Ladder_InvalidDirectionAndCapacity_EnforcesLimits()
    {
        foreach (var vector in new[] { (80d, 80d, 1d), (80d, 90d, 0d), (80d, 90d, -1d),
            (90d, 80d, 1d), (double.NaN, 90d, 1d), (80d, double.PositiveInfinity, 1d), (80d, 90d, double.NaN) })
            Check.Throws<ArgumentException>(() => TempoPrograms.Ladder(vector.Item1, vector.Item2, vector.Item3));
        foreach (var bars in new[] { 0, 1001 })
            Check.Throws<ArgumentException>(() => TempoPrograms.Ladder(80, 90, 10, bars));
        Assert.AreEqual(1000, TempoPrograms.Ladder(80, 90, 10, 1000)[1].Bars);
        Assert.AreEqual(1024, TempoPrograms.Ladder(1, 2.023, 0.001, 1).Count);
        Check.Throws<ArgumentException>(() => TempoPrograms.Ladder(1, 2.024, 0.001, 1));
    }
}
