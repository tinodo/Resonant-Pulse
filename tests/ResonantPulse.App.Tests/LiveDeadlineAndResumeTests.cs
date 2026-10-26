using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.Core;
using static ResonantPulse.App.Tests.LiveTestKit;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class LiveDeadlineAndResumeTests
{
    // 48 kHz, 120 BPM: 24000-frame beats, count-in until 96000. 90 BPM: 32000-frame beats, count-in until 128000.
    private const long Boundary = 96000 + 2 * 24000;

    private static PlaybackRevision Prepared(PlaybackRevision active, PulseDocument target) =>
        Revision(LiveSettingsEdits.Apply(active.Document, Requested(target)), active.SampleRate);

    private static PlaybackRevision SpliceAt(PlaybackRevision active, PlaybackRevision next, long cursor) =>
        LiveSplice.Splice(active, next, LiveSplice.NextPulseBoundary(active, cursor)!.Value);

    [TestMethod]
    public void HasSameClockComparesTheMusicalClockAndTheStopBoundary()
    {
        var first = Revision(Document(120, edit: s => s.PracticeSeconds = 3), 8000);
        Assert.IsTrue(first.HasSameClock(Prepared(first, Document(120, edit: s => s.Sound = "Glass"))));
        Assert.IsFalse(first.HasSameClock(Prepared(first, Document(90))), "A tempo change moves the musical clock.");
        Assert.IsFalse(first.HasSameClock(Revision(Document(120, edit: s => s.PracticeSeconds = 4), 8000)),
            "A different stop frame is not a sound edit.");
        Assert.IsFalse(first.HasSameClock(Revision(Document(120, edit: s => s.PracticeBars = 1), 8000)),
            "A different musical stop is not a sound edit.");

        // A placed revision keeps its own wall-clock timer; the plans and the musical stops still have to match.
        var placed = SpliceAt(first, Prepared(first, Document(90)), 17000);
        Assert.IsTrue(placed.HasSameClock(Prepared(placed, Document(90, edit: s => s.Sound = "Glass"))));
        Assert.IsFalse(placed.HasSameClock(Prepared(placed, Document(100))));
        Assert.IsFalse(placed.HasSameClock(Revision(Document(90, edit: s => s.PracticeBars = 2), 8000)));

        var program = Document(120);
        program.Program = [new ProgramStep { Bars = 2, StartTempo = 120, EndTempo = 120 }];
        var finite = Revision(program, 8000);
        var soundEdit = LiveSettingsEdits.Decide(finite, null, Requested(Document(120, edit: s => s.Sound = "Click")));
        Assert.IsTrue(finite.HasSameClock(Revision(soundEdit.Merged, 8000)));
        program.Program[0].Bars = 3;
        Assert.IsFalse(finite.HasSameClock(Revision(program, 8000)), "A longer program ends later.");
    }

    [TestMethod]
    public void ResolvePartsSeparatesTheStopConditionsAndKeepsTheCombinedDeadline()
    {
        var document = Document(120, edit: s => { s.PracticeSeconds = 2; s.PracticeBeats = 3; s.PracticeBars = 1; });
        var initial = ScheduleCompiler.Compile(document, 48000, 2, true);
        var body = ScheduleCompiler.Compile(document, 48000, 2, false);
        var parts = PlaybackDeadline.ResolveParts(initial, body, document.Settings, false);
        Assert.AreEqual(new DeadlineParts(192000, 168000, 192000, null), parts);
        Assert.AreEqual(168000L, parts.End);
        Assert.AreEqual(PlaybackDeadline.Resolve(initial, body, document.Settings, false), parts.End);
        Assert.AreEqual(new DeadlineParts(1, 168000, 192000, null), parts.WithTimer(1));
        document.Program = [new ProgramStep { Bars = 1, StartTempo = 120, EndTempo = 120 }];
        var finite = PlaybackDeadline.ResolveParts(initial, body, document.Settings, true);
        Assert.AreEqual(initial.TotalFrames, finite.Program);
        Assert.AreEqual(default(DeadlineParts), PlaybackDeadline.ResolveParts(initial, body, new PulseSettings(), false));
    }

    [TestMethod]
    public void PracticeSecondsKeepTheirAbsoluteFrameThroughEverySplice()
    {
        var first = Revision(Document(120, edit: s => s.PracticeSeconds = 10));
        Assert.AreEqual(576000L, first.EndFrame);
        var second = SpliceAt(first, Prepared(first, Document(90)), 130000);
        Assert.AreEqual(48000L, second.Shift);
        Assert.AreEqual(576000L + 48000, second.EndFrame, "Own deadline = absolute deadline + Shift.");
        Assert.AreEqual(576000L, second.EndFrameAbsolute);
        var third = SpliceAt(second, Prepared(second, Document(100)), 180000);
        Assert.AreEqual(22400L, third.Shift);
        Assert.AreEqual(576000L, third.EndFrameAbsolute);
        var restarted = SpliceAt(third, Prepared(third, Document(100, beats: 3)), 300000);
        Assert.AreEqual(576000L, restarted.EndFrameAbsolute, "A restart does not move the wall-clock timer either.");
        Assert.AreEqual(576000L + restarted.Shift, restarted.EndFrame);
    }

    [TestMethod]
    public void CountedBarsAndBeatsKeepTheirRemainingMusicalDistanceAtTheNewTempo()
    {
        var bars = Revision(Document(120, edit: s => s.PracticeBars = 4));
        Assert.AreEqual(96000L + 4 * 96000, bars.EndFrame);
        var slower = SpliceAt(bars, Prepared(bars, Document(90)), 130000);
        // Two of sixteen beats are behind the splice; the remaining fourteen now last 32000 frames each.
        Assert.AreEqual(Boundary + 14 * 32000L, slower.EndFrameAbsolute);

        var beats = Revision(Document(120, edit: s => s.PracticeBeats = 10));
        var slowerBeats = SpliceAt(beats, Prepared(beats, Document(90)), 130000);
        Assert.AreEqual(Boundary + 8 * 32000L, slowerBeats.EndFrameAbsolute);

        var mixed = Revision(Document(120, edit: s => { s.PracticeBeats = 10; s.PracticeBars = 4; s.PracticeSeconds = 100; }));
        var earliest = SpliceAt(mixed, Prepared(mixed, Document(90)), 130000);
        Assert.AreEqual(Boundary + 8 * 32000L, earliest.EndFrameAbsolute, "The earliest enabled condition still wins.");
        var faster = SpliceAt(bars, Prepared(bars, Document(240)), 130000);
        Assert.AreEqual(Boundary + 14 * 12000L, faster.EndFrameAbsolute);
    }

    [TestMethod]
    public void CountedLimitsAfterARestartCountFromTheRestartedBar()
    {
        var bars = Revision(Document(120, edit: s => s.PracticeBars = 2));
        var threeFour = SpliceAt(bars, Prepared(bars, Document(120, beats: 3)), 130000);
        Assert.AreEqual(72000L - Boundary, threeFour.Shift);
        Assert.AreEqual(Boundary + 2 * 72000L, threeFour.EndFrameAbsolute, "Two bars of 3/4 from the restarted downbeat.");

        var beats = Revision(Document(120, edit: s => s.PracticeBeats = 5));
        var restartedBeats = SpliceAt(beats, Prepared(beats, Document(120, beats: 3)), 130000);
        Assert.AreEqual(Boundary + 5 * 24000L, restartedBeats.EndFrameAbsolute);
    }

    [TestMethod]
    public void WithoutAPracticeLimitASplicedRevisionNeverEnds()
    {
        var first = Revision(Document(120));
        var spliced = SpliceAt(first, Prepared(first, Document(90)), 130000);
        Assert.IsNull(spliced.EndFrame);
        Assert.IsNull(spliced.EndFrameAbsolute);
        Assert.IsFalse(spliced.HasPracticeLimit);
    }

    [TestMethod]
    public void RevisionsConvertBetweenTheAbsoluteAndOwnTimelines()
    {
        var first = Revision(Document(120, edit: s => s.PracticeSeconds = 10));
        Assert.AreEqual(0L, first.Shift);
        Assert.IsTrue(first.IsNative);
        Assert.AreSame(first, first.Rebased(0, first.Deadline));
        var prepared = Prepared(first, Document(90));
        var spliced = SpliceAt(first, prepared, 130000);
        Assert.IsFalse(spliced.IsNative);
        Assert.AreSame(prepared.Initial, spliced.Initial, "A placed revision shares the compiled plans.");
        Assert.AreSame(prepared.Body, spliced.Body);
        Assert.AreSame(prepared.Document, spliced.Document);
        Assert.AreEqual(Boundary + 48000, spliced.ToOwn(Boundary));
        Assert.AreEqual(Boundary, spliced.ToAbsolute(spliced.ToOwn(Boundary)));
        Assert.AreEqual(spliced.EndFrame - 48000, spliced.EndFrameAbsolute);
    }

    [TestMethod]
    public void PresentationFollowsTheAudibleRevisionInItsOwnTimelineAndNeverGoesNegative()
    {
        // 8 kHz: the 4/4 revision's next pulse after 17000 is 20000; the 3/4 restart puts that click at own frame 12000.
        var first = Revision(Document(120, edit: s => s.PracticeSeconds = 2), 8000);
        var second = SpliceAt(first, Prepared(first, Document(120, beats: 3)), 17000);
        var timeline = new LiveTimeline(second, first, 20000);
        Assert.AreEqual(12000L - 20000, second.Shift);
        Assert.AreSame(first, LiveSplice.Present(timeline, 19999, false).Revision);
        Assert.AreSame(second, LiveSplice.Present(timeline, 20000, false).Revision);
        Assert.AreEqual(19999L, LiveSplice.Present(timeline, 19999, false).OwnFrame);
        Assert.AreEqual(12000L, LiveSplice.Present(timeline, 20000, false).OwnFrame,
            "The restarted bar 1 begins at the new revision's CountInFrames.");
        // Resume rounding may leave processed a frame before the placement; the position stays valid.
        Assert.AreEqual(0L, LiveSplice.Present(new LiveTimeline(second), 7999, false).OwnFrame);

        var end = second.EndFrameAbsolute!.Value;
        Assert.AreEqual(32000L, end);
        var completed = LiveSplice.Present(new LiveTimeline(second), end + 500, true);
        Assert.AreEqual(second.EndFrame!.Value - 1, completed.OwnFrame, "Completed holds the last rendered own frame.");
        Assert.AreEqual(second.EndFrameAbsolute, timeline.EndFrameAt(end));
        Assert.AreEqual(first.EndFrameAbsolute, timeline.EndFrameAt(19000), "Before the splice is heard the old deadline governs.");
    }

    [TestMethod]
    [DataRow(48000, 44100, true)]
    [DataRow(44100, 48000, true)]
    [DataRow(8000, 16000, true)]
    [DataRow(48000, 44100, false)]
    [DataRow(44100, 48000, false)]
    [DataRow(8000, 16000, false)]
    public void RebuildKeepsPlacedRevisionsCoherentAtAnotherSampleRate(int oldRate, int newRate, bool timerLimit)
    {
        long Convert(long frame) => (long)Math.Round((double)frame * newRate / oldRate);
        var first = Revision(Document(120, edit: s => { if (timerLimit) s.PracticeSeconds = 12; else s.PracticeBars = 6; }), oldRate);
        var second = Prepared(first, Document(96));
        var boundary = LiveSplice.NextPulseBoundary(first, 3L * oldRate)!.Value;
        var spliced = LiveSplice.Splice(first, second, boundary);
        var processed = boundary.Frame + oldRate / 10;
        var queued = Prepared(spliced, Document(100));
        var state = new RetainedPlayback(spliced, first, queued, boundary.Frame, processed);
        var resumed = state.Rebuild(newRate);

        Assert.AreEqual(newRate, resumed.Active.SampleRate);
        Assert.AreEqual(newRate, resumed.Previous!.SampleRate);
        Assert.AreEqual(newRate, resumed.Pending!.SampleRate);
        Assert.AreEqual(Convert(boundary.Frame), resumed.SwitchFrame);
        Assert.AreEqual(Convert(processed), resumed.ProcessedFrame);
        Assert.AreEqual(Convert(spliced.Shift), resumed.Active.Shift);
        Assert.IsTrue(resumed.Previous.IsNative);
        Assert.IsTrue(resumed.Pending.IsNative);
        Assert.IsTrue(Math.Abs(resumed.Active.EndFrameAbsolute!.Value - Convert(spliced.EndFrameAbsolute!.Value)) <= 3,
            $"Deadline {resumed.Active.EndFrameAbsolute} vs {Convert(spliced.EndFrameAbsolute.Value)}");
        Assert.IsTrue(Math.Abs(resumed.Previous.EndFrame!.Value - Convert(first.EndFrame!.Value)) <= 3);

        // The same audible instant maps to the same bar, beat and (within a frame) quarter position.
        var before = LiveSplice.Present(new LiveTimeline(spliced, first, boundary.Frame), processed, false);
        var after = LiveSplice.Present(new LiveTimeline(resumed.Active, resumed.Previous, resumed.SwitchFrame),
            resumed.ProcessedFrame, false);
        Assert.AreEqual(BarAndBeat(before), BarAndBeat(after));
        var quarterBefore = before.Plan.PositionAtFrame(before.MusicalFrame).QuarterPosition;
        var quarterAfter = after.Plan.PositionAtFrame(after.MusicalFrame).QuarterPosition;
        Assert.AreEqual(quarterBefore, quarterAfter, 0.01);
        // Before the splice is heard the old revision is still the one on screen.
        var earlier = LiveSplice.Present(new LiveTimeline(resumed.Active, resumed.Previous, resumed.SwitchFrame),
            resumed.SwitchFrame - 100, false);
        Assert.AreSame(resumed.Previous, earlier.Revision);
        Assert.AreEqual(resumed.SwitchFrame - 100, earlier.OwnFrame);
    }

    [TestMethod]
    public void RebuildScalesAChainOfPlacementsAndNeverTouchesNativeRevisions()
    {
        var first = Revision(Document(120, edit: s => s.PracticeSeconds = 12), 48000);
        var second = SpliceAt(first, Prepared(first, Document(90)), 130000);
        var third = SpliceAt(second, Prepared(second, Document(100)), 180000);
        var state = new RetainedPlayback(third, second, null, 208000, 210000);
        var resumed = state.Rebuild(44100);
        Assert.AreEqual((long)Math.Round(third.Shift * 44100d / 48000), resumed.Active.Shift);
        Assert.AreEqual((long)Math.Round(second.Shift * 44100d / 48000), resumed.Previous!.Shift);
        Assert.IsNull(resumed.Pending);
        Assert.AreEqual(Math.Round(third.EndFrame!.Value * 44100d / 48000), resumed.Active.EndFrame!.Value, 1.0);
        Assert.AreSame(state, state.Rebuild(48000));
        var native = new RetainedPlayback(first, null, null, 0, 1000).Rebuild(44100);
        Assert.AreEqual(0L, native.Active.Shift);
        Assert.AreEqual((2L + 12) * 44100, native.Active.EndFrame, "Unplaced revisions are recompiled natively, not scaled.");
    }
}
