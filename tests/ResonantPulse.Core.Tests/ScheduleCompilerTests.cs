using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class ScheduleCompilerTests
{
    [TestMethod]
    public void Compile_OneBarAt120_ReturnsExactFramesWithAndWithoutCountIn()
    {
        var d = new PulseDocument();
        var body = ScheduleCompiler.Compile(d, 48000, 1, false);
        Check.Sequence(new long[] { 0, 24000, 48000, 72000 }, body.Onsets.Select(o => o.Frame));
        Check.Sequence(new[] { Accent.Strong, Accent.Normal, Accent.Normal, Accent.Normal }, body.Onsets.Select(o => o.Accent));
        Assert.AreEqual(96000L, body.TotalFrames); Assert.AreEqual(0L, body.CountInFrames);
        d.Settings.Subdivision = 2; d.Lanes.Add(new());
        var with = ScheduleCompiler.Compile(d, 48000, 1);
        Assert.AreEqual(192000L, with.TotalFrames); Assert.AreEqual(96000L, with.CountInFrames);
        Assert.AreEqual(96000L, with.FrameAtQuarter(0));
        Check.Sequence(new long[] { 0, 24000, 48000, 72000 }, with.Onsets.Where(o => o.Frame < 96000).Select(o => o.Frame));
        Assert.IsTrue(with.Onsets.Where(o => o.Frame < 96000).All(o => o.LaneIndex == -1));
        Assert.AreEqual(12, with.Targets.Count); Assert.IsTrue(with.Targets.All(t => t.Frame >= 96000));
    }

    [TestMethod]
    [DataRow(2, 0.5, "0,12000,24000,36000,48000,60000,72000,84000")]
    [DataRow(3, 0.5, "0,8000,16000,24000,32000,40000,48000,56000,64000,72000,80000,88000")]
    [DataRow(5, 0.5, "0,4800,9600,14400,19200,24000,28800,33600,38400,43200,48000,52800,57600,62400,67200,72000,76800,81600,86400,91200")]
    [DataRow(7, 0.5, "0,3429,6857,10286,13714,17143,20571,24000,27429,30857,34286,37714,41143,44571,48000,51429,54857,58286,61714,65143,68571,72000,75429,78857,82286,85714,89143,92571")]
    [DataRow(2, 0.6666666666666666, "0,16000,24000,40000,48000,64000,72000,88000")]
    public void Compile_StraightTupletsAndSwing_UsesExactMusicalGrid(int subdivision, double swing, string frames)
    {
        var d = Check.Document(); d.Settings.Subdivision = subdivision; d.Settings.Swing = swing;
        var plan = Check.Plan(d, rate: 48000);
        Assert.AreEqual(frames, string.Join(",", plan.Onsets.Select(o => o.Frame)));
        Assert.AreEqual(96000L, plan.TotalFrames); Assert.IsTrue(plan.Onsets.All(o => o.Frame < plan.TotalFrames));
        d.Settings.Subdivision = 3; d.Settings.Swing = 0.6;
        Check.Throws<ArgumentException>(() => Check.Plan(d));
    }

    [TestMethod]
    [DataRow(0.75, 3, 4, 33140L)]
    [DataRow(0.6666666666666666, 2, 3, 29703L)]
    public void Compile_SwingBeforeRampIntegration_UsesWarpedQuarterPosition(double swing, int n, int denominator, long expected)
    {
        var d = Check.Document(); d.Settings.Subdivision = 2; d.Settings.Swing = swing;
        d.Program = [new() { Bars = 1, BeatsPerBar = 6, StartTempo = 60, EndTempo = 120, Ramp = "LinearTime" }];
        var plan = Check.Plan(d, rate: 48000);
        // Independent goldens: q=t+t²/8 gives t=sqrt(22)-4 for q=3/4,
        // and t=8/sqrt(3)-4 for q=2/3. Round the corresponding 48 kHz times once.
        Assert.AreEqual(expected, plan.Onsets.First(o => o.LaneIndex == -2).Frame);
        Assert.AreEqual(plan.FrameAtQuarter(new Rational(n, denominator)), expected);
        Assert.AreNotEqual((long)Math.Round(plan.FrameAtQuarter(1) * swing), expected);
    }

    [TestMethod]
    public void Compile_AccentsGroupsAndGaps_SeparatesTargetsFromAudibility()
    {
        var d = Check.Document(); d.Settings.Accents = [Accent.Strong, Accent.Soft, Accent.Normal, Accent.Silent];
        d.Settings.AudibleBars = 1; d.Settings.SilentBars = 2;
        var p = Check.Plan(d, 3, 48000);
        Check.Sequence(new long[] { 0, 24000, 48000 }, p.Onsets.Select(o => o.Frame));
        Assert.AreEqual(9, p.Targets.Count); Assert.AreEqual(3, p.Targets.Count(t => t.Audible));
        Assert.AreEqual((96000L, 288000L), p.SilentRanges.Single());
        Assert.IsTrue(p.PositionAtFrame(96000).IsSilent); Assert.IsFalse(p.PositionAtFrame(288000).IsSilent);
        var additive = Check.Document();
        additive.Settings = new() { CountInBars = 0, Tempo = 180, BeatsPerBar = 7, BeatDenominator = 8,
            BeatUnitQuarters = 0.5, Groups = [2, 2, 3], Accents = [Accent.Normal] };
        Check.Sequence(new[] { Accent.Strong, Accent.Normal, Accent.Strong, Accent.Normal, Accent.Strong, Accent.Normal, Accent.Normal },
            Check.Plan(additive).Onsets.Select(o => o.Accent));
        d.Settings.CountInBars = 1; d.Settings.Accents = [Accent.Silent];
        var countIn = Check.Plan(d, 3, countIn: true);
        Assert.AreEqual(4, countIn.Onsets.Count); Assert.AreEqual(0, countIn.Targets.Count);
    }

    [TestMethod]
    public void Compile_IndependentCyclesAndSharedThreeAgainstFour_UseDifferentGoldens()
    {
        var d = Check.Document(); d.Settings.Accents = [Accent.Silent];
        d.Lanes = [new() { Pulses = 3, CycleQuarters = 3 }, new() { Pulses = 4, CycleQuarters = 4 }];
        var p = Check.Plan(d, 4, 48000);
        // Both meter downbeats coincide at q=12; these lanes attack each quarter independently.
        Assert.AreEqual(Accent.Strong, p.Onsets.Single(o => o.LaneIndex == 0 && o.Frame == 288000).Accent);
        Assert.AreEqual(Accent.Strong, p.Onsets.Single(o => o.LaneIndex == 1 && o.Frame == 288000).Accent);
        Assert.AreEqual(Accent.Strong, p.Onsets.Single(o => o.LaneIndex == 0 && o.Frame == 72000).Accent);
        Assert.AreEqual(Accent.Normal, p.Onsets.Single(o => o.LaneIndex == 1 && o.Frame == 72000).Accent);
        d.Lanes = [new() { Pulses = 3, CycleQuarters = 4 }];
        Check.Sequence(new long[] { 0, 32000, 64000 }, Check.Plan(d, rate: 48000).Onsets.Select(o => o.Frame));
    }

    [TestMethod]
    [DataRow(0.0, "0,72000,144000")]
    [DataRow(0.5, "12000,84000,156000")]
    [DataRow(-0.5, "60000,132000")]
    public void Compile_OffsetsSectionsAndRepeats_PreserveAbsoluteLanePhase(double offset, string frames)
    {
        var d = Check.Document(); d.Settings.Accents = [Accent.Silent];
        d.Lanes = [new() { Pulses = 1, CycleQuarters = 3, OffsetQuarters = offset }];
        d.Program = [new() { StartTempo = 120, EndTempo = 120, Bars = 1, Repeats = 2 }];
        var p = Check.Plan(d, rate: 48000);
        Assert.AreEqual(frames, string.Join(",", p.Onsets.Select(o => o.Frame)));
        Assert.AreEqual(192000L, p.TotalFrames); Assert.AreEqual(2, p.SectionBoundaries.Count);
    }

    [TestMethod]
    public void Compile_GainMuteAndSolo_ApplyDocumentedLaneRules()
    {
        var d = Check.LaneDocument(pan: -0.4f, volume: 0.5f, eventGain: 0.4f);
        var onset = Check.Plan(d).Onsets.Single();
        Assert.AreEqual(0.2f, onset.Gain); Assert.AreEqual(-0.4f, onset.Pan); Assert.AreEqual("Glass", onset.Sound);
        d.Settings.Accents = [Accent.Normal]; d.Settings.Subdivision = 2; d.Settings.CountInBars = 1;
        d.Lanes[0].Solo = true; d.Lanes.Add(new());
        var solo = Check.Plan(d, countIn: true);
        Assert.IsTrue(solo.Onsets.Where(o => o.Frame >= solo.CountInFrames).All(o => o.LaneIndex == 0));
        Assert.AreEqual(4, solo.Onsets.Count(o => o.Frame < solo.CountInFrames));
        Assert.IsTrue(solo.Targets.Any(t => t.LaneIndex == -1 && !t.Audible));
        d.Lanes[0].Muted = true;
        var mutedSolo = Check.Plan(d);
        Assert.IsTrue(mutedSolo.Onsets.Any(o => o.LaneIndex == -1));
        Assert.IsFalse(mutedSolo.Onsets.Any(o => o.LaneIndex == 0));
    }

    [TestMethod]
    public void Compile_ProgramRepeatsAndBarsOverride_ProducesFiniteBoundaries()
    {
        var d = Check.Document();
        d.Program = [new() { Name = "A", Bars = 1, Repeats = 2, StartTempo = 120, EndTempo = 120 },
            new() { Name = "B", Bars = 1, BeatsPerBar = 3, StartTempo = 60, EndTempo = 60 }];
        d.Setlist = [new() { Document = new() { Settings = new() { Tempo = 1 } } }];
        var p = Check.Plan(d, 99, 48000);
        Assert.AreEqual(336000L, p.TotalFrames);
        Check.Sequence(new long[] { 0, 96000, 192000 }, p.SectionBoundaries.Select(b => b.StartFrame));
        Check.Sequence(new[] { 0, 1, 0 }, p.SectionBoundaries.Select(b => b.RepeatIndex));
        Assert.AreEqual("B", p.PositionAtFrame(192000).SectionName);
        Assert.AreEqual(2, p.PositionAtFrame(192000).BarIndex);
    }

    [TestMethod]
    public void Compile_AdjacentEndpointsAndFrameRounding_RejectsOrOwnsOnce()
    {
        var d = Check.Document(); d.Program = [new() { StartTempo = 120, EndTempo = 120, Bars = 1, Repeats = 2 }];
        d.Lanes = [new(), new()];
        var p = Check.Plan(d);
        Assert.AreEqual(3, p.Onsets.Count(o => o.Frame == 16000));
        Assert.IsFalse(p.Onsets.Any(o => o.Frame == p.TotalFrames));
        Assert.AreEqual(p.TotalFrames, p.FrameAtQuarter(8));
        var rounded = Check.LaneDocument(position: new Rational(3999999, 1000000));
        var e = Check.Throws<ArgumentException>(() => Check.Plan(rounded));
        Assert.IsTrue(e.Message.Contains("endpoint", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Probability_ReducedCoordinatesAndSignedIdentity_AreStableAndBounded()
    {
        // No independently reviewed SHA digest was supplied. This pins canonical identity, not a cross-version hash golden.
        double a = ScheduleCompiler.Probability(-23, 2, 3, 1, new Rational(-6, 8));
        double b = ScheduleCompiler.Probability(-23, 2, 3, 1, new Rational(-3, 4));
        Assert.AreEqual(a, b); Assert.IsTrue(a >= 0 && a < 1);
        Assert.AreNotEqual(a, ScheduleCompiler.Probability(-23, 1, 3, 1, new Rational(-3, 4)));
        Assert.AreNotEqual(a, ScheduleCompiler.Probability(-23, 2, 4, 1, new Rational(-3, 4)));
        Assert.AreNotEqual(a, ScheduleCompiler.Probability(-23, 2, 3, 0, new Rational(-3, 4)));
        Assert.AreNotEqual(a, ScheduleCompiler.Probability(23, 2, 3, 1, new Rational(-3, 4)));
    }

    [TestMethod]
    public void Compile_ProbabilityEndpointsTiesAndSeededReplays_AreDeterministic()
    {
        var d = Check.LaneDocument();
        d.Lanes[0].Events = [new() { PositionQuarters = 0, Probability = 0 },
            new() { PositionQuarters = 1, Probability = 1 }, new() { PositionQuarters = 2, Tie = true },
            new() { PositionQuarters = 3, Accent = Accent.Silent }];
        var p = Check.Plan(d);
        Assert.AreEqual(1, p.Onsets.Count); Assert.AreEqual(4000L, p.Onsets.Single().Frame);
        Assert.AreEqual(2, p.Targets.Count); Assert.IsFalse(p.Targets.Single(t => t.Frame == 0).Audible);
        d.Lanes[0].Probability = 0;
        Assert.AreEqual(0, Check.Plan(d).Onsets.Count); Assert.AreEqual(2, Check.Plan(d).Targets.Count);
        d.Lanes[0].Events = []; d.Lanes[0].Probability = 0.5; d.Lanes[0].Pulses = 16; d.Seed = -2026;
        var first = Check.Plan(d); var second = Check.Plan(d);
        Check.Sequence(first.Onsets, second.Onsets); Assert.AreEqual(16, first.Targets.Count);
        Check.Sequence(Check.Render(first, 16000), Check.Render(second, 16000));
    }

    [TestMethod]
    public void Compile_RateBarsAndDurationBoundaries_RejectsUnsupportedPlans()
    {
        Assert.AreEqual(8000, Check.Plan(rate: 8000).SampleRate);
        Assert.AreEqual(192000, Check.Plan(rate: 192000).SampleRate);
        foreach (var rate in new[] { 7999, 192001 }) Check.Throws<ArgumentException>(() => Check.Plan(rate: rate));
        foreach (var bars in new[] { 0, 100001 }) Check.Throws<ArgumentException>(() => Check.Plan(bars: bars));
        var minimum = Check.Document(); minimum.Settings.BeatsPerBar = 1; minimum.Settings.Tempo = 600;
        Assert.AreEqual(800L, Check.Plan(minimum).TotalFrames);
        minimum.Settings.Tempo = Math.BitIncrement(600);
        Check.Throws<ArgumentException>(() => Check.Plan(minimum));
        var maximum = Check.Document(); maximum.Settings.Tempo = 1; maximum.Settings.BeatUnitQuarters = 16;
        Assert.AreEqual(691200000L, Check.Plan(maximum, 5760).TotalFrames);
        Check.Throws<ArgumentException>(() => Check.Plan(maximum, 5761));
        maximum.Program = [new() { Bars = 1, StartTempo = 120, EndTempo = 120 }];
        Assert.AreEqual(1000L, Check.Plan(maximum, 100000).TotalFrames);
    }

    [TestMethod]
    public void Compile_AttackDensityAndCircularWindows_Enforces4096PerSecond()
    {
        var d = Check.Document(); d.Settings.Accents = [Accent.Silent];
        d.Lanes = Enumerable.Range(0, 8).Select(_ => new RhythmLane
        { Pulses = 256, CycleQuarters = 1, Muted = true }).ToList();
        var accepted = Check.Plan(d);
        Assert.AreEqual(8192, accepted.Targets.Count); Assert.AreEqual(0, accepted.Onsets.Count);
        d.Settings.Accents = [Accent.Normal];
        var e = Check.Throws<ArgumentException>(() => Check.Plan(d));
        Assert.IsTrue(e.Message.Contains("4096", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Compile_VoiceWindowAndCircularSeam_Enforces256PerFiftyMilliseconds(bool seam)
    {
        var d = Check.LaneDocument();
        d.Lanes[0].Events = Enumerable.Range(0, 256).Select(i => new RhythmEvent
        { PositionQuarters = seam && i >= 128 ? new Rational(39800 + i - 128, 10000) : new Rational(i, 10000) }).ToList();
        var p = Check.Plan(d);
        Assert.AreEqual(256, p.Onsets.Count); Assert.AreEqual(256, p.Targets.Count);
        d.Lanes.Add(new() { Events = [new() { PositionQuarters = seam ? new Rational(399, 100) : Rational.Zero }] });
        var e = Check.Throws<ArgumentException>(() => Check.Plan(d));
        Assert.IsTrue(e.Message.Contains("256", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(44100, 22050L, 88200L)]
    [DataRow(96000, 48000L, 192000L)]
    public void Compile_AlternateSampleRates_PreserveAbsoluteQuarterFrames(int rate, long quarter, long total)
    {
        var p = Check.Plan(rate: rate);
        Check.Sequence(new[] { 0L, quarter, quarter * 2, quarter * 3 }, p.Onsets.Select(o => o.Frame));
        Assert.AreEqual(total, p.TotalFrames); Assert.AreEqual(quarter, p.FrameAtQuarter(1));
    }

    [TestMethod]
    public void Compile_SelectedUnitCrossingMasterBar_DoesNotRestartPrimaryPulse()
    {
        var d = Check.Document(); d.Settings.BeatUnitQuarters = 1.5; d.Settings.Tempo = 80;
        var p = Check.Plan(d, 2, 48000);
        Check.Sequence(new long[] { 0, 36000, 72000, 108000, 144000, 180000 }, p.Onsets.Select(o => o.Frame));
        Assert.AreEqual(192000L, p.TotalFrames);
        Assert.IsFalse(p.Onsets.Any(o => o.Frame == 96000));
        Assert.AreEqual(1, p.PositionAtFrame(96000).BarIndex);
        Assert.AreEqual(2d / 3, p.PositionAtFrame(96000).PulsePhase, 1e-12);
    }

    [TestMethod]
    public void Compile_AuthoredTuplet_IsNotWarpedBySubdivisionSwing()
    {
        var d = Check.LaneDocument(position: new Rational(1, 3));
        d.Settings.Subdivision = 2; d.Settings.Swing = 0.75; d.Settings.Accents = [Accent.Normal];
        var p = Check.Plan(d, rate: 48000);
        Assert.AreEqual(8000L, p.Onsets.Single(o => o.LaneIndex == 0).Frame);
        Assert.AreEqual(18000L, p.Onsets.First(o => o.LaneIndex == -2).Frame);
        Assert.AreEqual(new Rational(1, 3), d.Lanes[0].Events.Single().PositionQuarters);
    }

    [TestMethod]
    public void Compile_RepeatedRamp_RestartsTempoButNotAbsoluteCoordinates()
    {
        var d = Check.Document();
        d.Program = [new() { Bars = 1, BeatsPerBar = 6, Repeats = 2, StartTempo = 60, EndTempo = 120, Ramp = "LinearTime" }];
        var p = Check.Plan(d, rate: 48000);
        Assert.AreEqual(384000L, p.TotalFrames);
        Assert.AreEqual(60d, p.PositionAtFrame(192000).Tempo); Assert.AreEqual(1, p.PositionAtFrame(192000).RepeatIndex);
        Assert.AreEqual(288000L, p.FrameAtQuarter(new Rational(17, 2)));
        Assert.AreEqual(1, p.Onsets.Count(o => o.Frame == 192000));
        Assert.AreEqual(6d, p.PositionAtFrame(192000).QuarterPosition);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Compile_OpportunityBudget_CountsSilentAndTiedCandidates(bool tied)
    {
        // Compact definition and empty output: no million-element fixture/voice array.
        // 1000 bars * (4 silent main pulses + 4 cycles * 249 silent/tied events) = exactly 1,000,000.
        // This intentionally exercises the finite opportunity loop in CI; it is not a realtime benchmark.
        var d = Check.LaneDocument();
        d.Lanes[0].CycleQuarters = 1;
        d.Lanes[0].Events = Enumerable.Range(0, 249).Select(i => new RhythmEvent
        { PositionQuarters = new Rational(i, 249), Tie = tied, Accent = tied ? Accent.Normal : Accent.Silent }).ToList();
        var exact = Check.Plan(d, 1000);
        Assert.AreEqual(0, exact.Onsets.Count); Assert.AreEqual(0, exact.Targets.Count);
        Assert.AreEqual(16000000L, exact.TotalFrames);
        var error = Check.Throws<ArgumentException>(() => Check.Plan(d, 1001));
        Assert.IsTrue(error.Message.Contains("million", StringComparison.OrdinalIgnoreCase));
    }
}
