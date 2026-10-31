using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class RenderPlanTests
{
    [TestMethod]
    public void Models_DefaultsAndLeafRecords_ExposeRecordedValues()
    {
        var d = new PulseDocument(); var s = d.Settings; var l = new RhythmLane(); var e = new RhythmEvent();
        Assert.AreEqual("Open practice", d.Name); Assert.AreEqual(1, d.SchemaVersion); Assert.AreEqual(1, d.Seed);
        Assert.AreEqual("event-hash-v1", d.ProbabilityAlgorithm);
        Assert.AreEqual(0, d.Lanes.Count + d.Program.Count + d.Setlist.Count);
        Assert.AreEqual(120d, s.Tempo); Assert.AreEqual(4, s.BeatsPerBar); Assert.AreEqual(4, s.BeatDenominator);
        Assert.AreEqual(1d, s.BeatUnitQuarters); Assert.AreEqual(1, s.Subdivision); Assert.AreEqual(0.5, s.Swing);
        Assert.AreEqual(1, s.CountInBars); Assert.AreEqual(0.5f, s.MasterGain); Assert.IsTrue(s.BlindGaps);
        Assert.AreEqual("Wood", s.Sound); Assert.AreEqual(0, s.Groups.Length + s.AudibleBars + s.SilentBars);
        Check.Sequence(new[] { Accent.Strong, Accent.Normal, Accent.Normal, Accent.Normal }, s.Accents);
        Assert.AreEqual("Lane", l.Name); Assert.AreEqual(4, l.Pulses); Assert.AreEqual(4d, l.CycleQuarters);
        Assert.AreEqual(0d, l.OffsetQuarters); Assert.AreEqual(0.7f, l.Volume); Assert.AreEqual(0f, l.Pan);
        Assert.IsFalse(l.Muted || l.Solo); Assert.AreEqual(1d, l.Probability); Assert.AreEqual("Glass", l.Sound);
        Assert.AreEqual(0, l.Pattern.Length + l.Events.Count);
        Assert.AreEqual(Rational.Zero, e.PositionQuarters); Assert.AreEqual(Accent.Normal, e.Accent);
        Assert.AreEqual(1d, e.Probability); Assert.AreEqual(1f, e.Gain); Assert.IsFalse(e.Tie);
        var step = new ProgramStep();
        Assert.AreEqual("Section", step.Name); Assert.AreEqual(80d, step.StartTempo); Assert.AreEqual(80d, step.EndTempo);
        Assert.AreEqual(8, step.Bars); Assert.AreEqual(1, step.Repeats); Assert.AreEqual("Constant", step.Ramp);
        var song = new SongEntry(); Assert.AreEqual("", song.Name); Assert.AreEqual("", song.Notes); Assert.IsNotNull(song.Document);
        Check.Sequence(new[] { 0, 1, 2, 3 }, Enum.GetValues<Accent>().Select(a => (int)a));
        var o = new ScheduledOnset { Frame = 9, LaneIndex = 2, Accent = Accent.Soft, Gain = 0.2f, Pan = -1, Sound = "Kick" };
        Assert.AreEqual((9L, 2, Accent.Soft, 0.2f, -1f, "Kick"), (o.Frame, o.LaneIndex, o.Accent, o.Gain, o.Pan, o.Sound));
        var target = new TargetOnset(4, 1, Accent.Normal, false);
        Assert.AreEqual((4L, 1, Accent.Normal, false), (target.Frame, target.LaneIndex, target.Accent, target.Audible));
        var tempo = new TempoSegment { StartFrame = 5, EndFrame = 10, StartTempo = 90, EndTempo = 100 };
        Assert.AreEqual((5L, 10L, 90d, 100d), (tempo.StartFrame, tempo.EndFrame, tempo.StartTempo, tempo.EndTempo));
    }

    [TestMethod]
    public void Compile_SourceMutation_DoesNotChangePlanSnapshot()
    {
        var d = Check.LaneDocument(); d.Program = [new() { Bars = 1, StartTempo = 120, EndTempo = 120 }];
        var p = Check.Plan(d); var samples = Check.Render(p, 1000);
        var onsets = p.Onsets.ToArray(); var targets = p.Targets.ToArray(); var sections = p.SectionBoundaries.ToArray();
        d.Settings.Tempo = 30; d.Settings.Accents[0] = Accent.Strong; d.Settings.MasterGain = 0;
        d.Lanes[0].Events[0].PositionQuarters = 1; d.Lanes[0].Events[0].Gain = 0;
        d.Lanes[0].Pattern = [Accent.Silent]; d.Lanes.Clear(); d.Program[0].Bars = 9; d.Program.Clear();
        Check.Sequence(onsets, p.Onsets); Check.Sequence(targets, p.Targets); Check.Sequence(sections, p.SectionBoundaries);
        Check.Sequence(samples, Check.Render(p, 1000)); Assert.AreEqual(16000L, p.TotalFrames);
        Assert.AreEqual(0.3f, p.DefaultMasterGain); Assert.AreEqual(4000L, p.FrameAtQuarter(1));
    }

    [TestMethod]
    public void RenderPlan_MetadataAndCollections_DescribeCompiledDocument()
    {
        var d = Check.Document(); d.Seed = -7; d.Settings.MasterGain = 0.4f;
        d.Settings.AudibleBars = d.Settings.SilentBars = 1;
        var p = Check.Plan(d, 2);
        Assert.AreEqual(8000, p.SampleRate); Assert.AreEqual(32000L, p.TotalFrames);
        Assert.AreEqual(-7, p.Seed); Assert.AreEqual("event-hash-v1", p.ProbabilityAlgorithm);
        Assert.AreEqual(0.4f, p.DefaultMasterGain); Assert.AreEqual(1d, p.BeatUnitQuarters);
        Assert.AreEqual(8, p.Targets.Count); Assert.AreEqual(4, p.Onsets.Count);
        Assert.AreEqual((16000L, 32000L), p.SilentRanges.Single());
        Assert.AreEqual(0L, p.TempoSegments.Single().StartFrame);
        Assert.AreEqual(32000L, p.TempoSegments.Single().EndFrame);
    }

    [TestMethod]
    public void FrameAtQuarter_CountInBodyInteriorAndFinalEndpoint_ReturnsExactFrames()
    {
        var p = ScheduleCompiler.Compile(new(), 48000, 1);
        Assert.AreEqual(0L, p.FrameAtQuarter(-4)); Assert.AreEqual(96000L, p.FrameAtQuarter(0));
        Assert.AreEqual(120000L, p.FrameAtQuarter(1)); Assert.AreEqual(192000L, p.FrameAtQuarter(4));
        Assert.AreEqual(0L, Check.Plan().FrameAtQuarter(0));
        Check.Throws<ArgumentOutOfRangeException>(() => p.FrameAtQuarter(new Rational(-401, 100)));
        Check.Throws<ArgumentOutOfRangeException>(() => p.FrameAtQuarter(new Rational(401, 100)));
    }

    [TestMethod]
    public void PositionAtFrame_CountInBodyAndEndpoint_ReturnsExplicitMusicalCoordinates()
    {
        var p = ScheduleCompiler.Compile(new(), 48000, 1);
        var start = p.PositionAtFrame(0);
        Assert.IsTrue(start.IsCountIn); Assert.AreEqual(-1, start.BarIndex); Assert.AreEqual(-4d, start.QuarterPosition);
        Assert.AreEqual(-1, start.ProgramStepIndex);
        var interior = p.PositionAtFrame(132000);
        Assert.AreEqual(1.5, interior.QuarterPosition); Assert.AreEqual(1, interior.BeatIndex);
        Assert.AreEqual(0.5, interior.BeatPhase); Assert.AreEqual(0.5, interior.PulsePhase);
        Assert.AreEqual(0, interior.SectionBarIndex); Assert.AreEqual(0, interior.RepeatIndex);
        Assert.AreEqual(4d, p.PositionAtFrame(p.TotalFrames).QuarterPosition);
        Check.Throws<ArgumentOutOfRangeException>(() => p.PositionAtFrame(-1));
        Check.Throws<ArgumentOutOfRangeException>(() => p.PositionAtFrame(p.TotalFrames + 1));
    }

    [TestMethod]
    public void PositionAtFrame_BlindGapsAndLaneMute_HaveDifferentSilenceFacts()
    {
        var d = Check.LaneDocument(); d.Lanes[0].Muted = true; d.Settings.BlindGaps = true;
        var mute = Check.Plan(d, 2);
        Assert.IsFalse(mute.PositionAtFrame(0).IsSilent); Assert.AreEqual(2, mute.Targets.Count);
        d.Settings.AudibleBars = d.Settings.SilentBars = 1;
        var gap = Check.Plan(d, 2);
        Assert.IsTrue(gap.PositionAtFrame(16000).IsSilent);
        Assert.AreEqual(4d, gap.PositionAtFrame(16000).QuarterPosition);
        Assert.AreEqual(2, gap.Targets.Count); Assert.IsTrue(gap.Targets.All(t => !t.Audible));
    }

    [TestMethod]
    public void LaneCount_IsTheAuthoredLaneCountEvenForLanesThatNeverSound()
    {
        Assert.AreEqual(0, Check.Plan().LaneCount);
        Assert.AreEqual(1, Check.Plan(Check.LaneDocument()).LaneCount);
        var d = Check.Document();
        d.Settings.Accents = [Accent.Silent];
        d.Lanes = [new() { Muted = true }, new() { Probability = 0 }, new() { Pattern = [Accent.Silent] }, new(), new() { Solo = true }];
        var plan = Check.Plan(d, 2, 8000, true);
        Assert.AreEqual(5, plan.LaneCount);
        Assert.IsTrue(plan.Onsets.All(onset => onset.LaneIndex is < 0 or 4), "Only the soloed lane and the count-in can sound.");
        d.Lanes = Enumerable.Range(0, 8).Select(_ => new RhythmLane { Muted = true }).ToList();
        var eight = Check.Plan(d);
        d.Lanes.Clear();
        Assert.AreEqual(8, eight.LaneCount, "The count is a fact of the plan: editing the document afterwards does not change it.");
        Assert.AreEqual(0, eight.Onsets.Count);
    }
}
