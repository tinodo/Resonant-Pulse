using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class TempoMapTests
{
    private static RenderPlan Ramp(string ramp, double start = 60, double end = 120)
    {
        var d = Check.Document();
        d.Program = [new() { Name = "Six", Bars = 1, BeatsPerBar = 6, StartTempo = start, EndTempo = end, Ramp = ramp }];
        return Check.Plan(d, rate: 48000);
    }

    [TestMethod]
    public void LinearTime_SixQuarterSixtyToOneTwenty_Preserves192000FrameGolden()
    {
        var p = Ramp("LinearTime");
        Assert.AreEqual(192000L, p.TotalFrames);
        Assert.AreEqual(96000L, p.FrameAtQuarter(new Rational(5, 2)));
        var at = p.PositionAtFrame(96000);
        Assert.AreEqual(2.5, at.QuarterPosition, 1e-12); Assert.AreEqual(90d, at.Tempo, 1e-10);
        Assert.AreEqual(60d, p.TempoSegments.Single().StartTempo); Assert.AreEqual(120d, p.TempoSegments.Single().EndTempo);
        Assert.AreEqual("LinearTime", p.SectionBoundaries.Single().Ramp);
    }

    [TestMethod]
    public void LinearBeat_SixQuarterSixtyToOneTwenty_Preserves199626FrameGolden()
    {
        var p = Ramp("LinearBeat");
        Assert.AreEqual(199626L, p.TotalFrames);
        Assert.AreEqual(116774L, p.FrameAtQuarter(3));
        var at = p.PositionAtFrame(116774);
        Assert.AreEqual(3d, at.QuarterPosition, 1.6e-5);
        Assert.AreEqual(90d, at.Tempo, 0.00016);
        Assert.AreEqual(6d, p.PositionAtFrame(p.TotalFrames).QuarterPosition);
        Assert.AreEqual(p.TotalFrames, p.FrameAtQuarter(6));
    }

    [TestMethod]
    [DataRow("LinearTime", 192000L)]
    [DataRow("LinearBeat", 199626L)]
    public void TempoMapping_DecelerationAndConstantLimit_PreservesInverseAndFiniteDuration(string ramp, long duration)
    {
        var reverse = Ramp(ramp, 120, 60);
        Assert.AreEqual(duration, reverse.TotalFrames);
        Assert.AreEqual(120d, reverse.PositionAtFrame(0).Tempo);
        Assert.AreEqual(60d, reverse.PositionAtFrame(duration).Tempo);
        foreach (var q in new Rational[] { 0, new(1, 3), 1, 3, new(23, 4), 6 })
            Assert.AreEqual(q.ToDouble(), reverse.PositionAtFrame(reverse.FrameAtQuarter(q)).QuarterPosition, 2.1e-5);
        var constant = Ramp(ramp, 60, 60);
        Assert.AreEqual(288000L, constant.TotalFrames); Assert.AreEqual(144000L, constant.FrameAtQuarter(3));
        var tiny = Ramp(ramp, 60, 60.000000001);
        Assert.AreEqual(288000L, tiny.TotalFrames);
        Assert.AreEqual(3d, tiny.PositionAtFrame(144000).QuarterPosition, 1e-9);
    }

    [TestMethod]
    public void CompoundMeter_DottedQuarterAndEighthUnits_PreserveGoldenFrames()
    {
        var d = Check.Document(); d.Settings.Tempo = 80; d.Settings.BeatsPerBar = 6;
        d.Settings.BeatDenominator = 8; d.Settings.BeatUnitQuarters = 1.5;
        var dotted = Check.Plan(d, rate: 48000);
        Check.Sequence(new long[] { 0, 36000 }, dotted.Onsets.Select(o => o.Frame));
        Assert.AreEqual(72000L, dotted.TotalFrames);
        Assert.AreEqual(80d, dotted.PositionAtFrame(12000).Tempo);
        Assert.AreEqual(1, dotted.PositionAtFrame(12000).BeatIndex);
        Assert.AreEqual(1d / 3, dotted.PositionAtFrame(12000).PulsePhase, 1e-12);
        d.Settings.Subdivision = 3;
        Check.Sequence(new long[] { 0, 12000, 24000, 36000, 48000, 60000 }, Check.Plan(d, rate: 48000).Onsets.Select(o => o.Frame));
        d.Settings.Subdivision = 1; d.Settings.BeatUnitQuarters = 0.5;
        var eighth = Check.Plan(d, rate: 48000);
        Assert.AreEqual(216000L, eighth.TotalFrames);
        Check.Sequence(new long[] { 0, 36000, 72000, 108000, 144000, 180000 }, eighth.Onsets.Select(o => o.Frame));
    }

    [TestMethod]
    public void SectionBoundary_AdjacentRepeatsAndFinalPosition_ExposeExactOwnership()
    {
        var d = Check.Document(); d.Settings.CountInBars = 1;
        d.Program = [new() { Name = "Repeated", Bars = 1, Repeats = 2, StartTempo = 120, EndTempo = 120 }];
        var p = Check.Plan(d, rate: 48000, countIn: true);
        Check.Sequence(new[] { -1, 0, 0 }, p.SectionBoundaries.Select(b => b.StepIndex));
        Check.Sequence(new[] { 0, 0, 1 }, p.SectionBoundaries.Select(b => b.RepeatIndex));
        Assert.AreEqual(new SectionBoundary("Repeated", 0, 1, 192000, 288000, 4, 4, 1, 4, 4, "Constant"),
            p.SectionBoundaries[2]);
        Assert.IsTrue(p.PositionAtFrame(95999).IsCountIn);
        Assert.IsFalse(p.PositionAtFrame(96000).IsCountIn);
        Assert.AreEqual(1, p.PositionAtFrame(192000).RepeatIndex);
        Assert.AreEqual(8d, p.PositionAtFrame(288000).QuarterPosition);
        Assert.AreEqual(1, p.PositionAtFrame(288000).BarIndex);
    }
}
