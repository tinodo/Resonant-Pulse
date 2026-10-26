using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.Core;
using static ResonantPulse.App.Tests.LiveTestKit;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class LiveSpliceTests
{
    // 48 kHz, 120 BPM: a beat is 24000 frames, the one-bar count-in ends at 96000 and one loop is 16 bars.
    // 90 BPM: 32000 frames per beat, count-in ends at 128000.
    private const long Beat120 = 24000, CountIn120 = 96000, Beat90 = 32000, CountIn90 = 128000;

    [TestMethod]
    [DataRow(0L, 0, 0, 96000L)]
    [DataRow(95999L, 0, 0, 96000L)]
    [DataRow(96000L, 0, 0, 96000L)]
    [DataRow(96001L, 0, 1, 120000L)]
    [DataRow(120000L, 0, 1, 120000L)]
    [DataRow(120001L, 0, 2, 144000L)]
    [DataRow(1608000L, 0, 63, 1608000L)]
    [DataRow(1608001L, 1, 0, 1632000L)]
    [DataRow(1632000L, 1, 0, 1632000L)]
    [DataRow(1632001L, 1, 1, 1656000L)]
    [DataRow(3167999L, 2, 0, 3168000L)]
    [DataRow(3168001L, 2, 1, 3192000L)]
    public void NextPulseBoundaryIsTheFirstPrimaryPulseAtOrAfterTheCursor(long cursor, int loop, int pulse, long frame)
    {
        var revision = Revision(Document(120));
        var boundary = LiveSplice.NextPulseBoundary(revision, cursor);
        Assert.AreEqual(new PulseBoundary(loop, pulse, frame, frame), boundary);
    }

    [TestMethod]
    public void NextPulseBoundaryFollowsTheOwnTimelineOfASplicedRevision()
    {
        var before = Revision(Document(120));
        var after = Revision(Document(90));
        var spliced = LiveSplice.Splice(before, after, LiveSplice.NextPulseBoundary(before, 140000)!.Value);
        Assert.AreEqual(Beat90 * 2 + CountIn90 - (CountIn120 + Beat120 * 2), spliced.Shift);
        var next = LiveSplice.NextPulseBoundary(spliced, 150000);
        Assert.AreEqual(new PulseBoundary(0, 3, CountIn90 + 3 * Beat90, CountIn90 + 3 * Beat90 - spliced.Shift), next);
        Assert.AreEqual(176000L, next!.Value.Frame);
    }

    [TestMethod]
    public void FiniteProgramsHaveNoPulseBoundaries()
    {
        var document = Document();
        document.Program = [new ProgramStep { Bars = 2, StartTempo = 100, EndTempo = 100 }];
        Assert.IsNull(LiveSplice.NextPulseBoundary(Revision(document, 8000), 0));
    }

    [TestMethod]
    public void LoopContinuityIsKeptForTempoSubdivisionGroupAndSwingEditsAndGapsThatDivideTheLoop()
    {
        var baseline = Revision(Document(120), 8000);
        Assert.IsTrue(LiveSplice.PreservesLoop(baseline, Revision(Document(90), 8000)));
        Assert.IsTrue(LiveSplice.PreservesLoop(baseline,
            Revision(Document(120, edit: s => { s.Subdivision = 4; s.Groups = [2, 2]; s.Swing = 0.6; }), 8000)));
        Assert.IsTrue(LiveSplice.PreservesLoop(baseline,
            Revision(Document(120, edit: s => { s.AudibleBars = 2; s.SilentBars = 2; }), 8000)),
            "Four gap bars still divide the sixteen-bar loop.");
    }

    [TestMethod]
    public void MeterUnitAndLoopLengthChangesRestartTheRhythm()
    {
        var baseline = Revision(Document(120), 8000);
        Assert.IsFalse(LiveSplice.PreservesLoop(baseline, Revision(Document(120, beats: 3), 8000)));
        Assert.IsFalse(LiveSplice.PreservesLoop(baseline, Revision(Document(120, beats: 8, denominator: 8, unit: 0.5), 8000)));
        Assert.IsFalse(LiveSplice.PreservesLoop(baseline, Revision(Document(120, unit: 0.5), 8000)));
        Assert.IsFalse(LiveSplice.PreservesLoop(baseline,
            Revision(Document(120, edit: s => { s.AudibleBars = 3; s.SilentBars = 2; }), 8000)),
            "Five gap bars stretch the loop to twenty bars.");
    }

    [TestMethod]
    public void ContinuityKeepsLoopAndPulseWhileRestartLandsOnTheNewDownbeat()
    {
        var after = Revision(Document(90));
        // Loop 0 lives inside Initial, later loops are Body cycles after Initial.TotalFrames.
        Assert.AreEqual(CountIn90 + 5 * Beat90, LiveSplice.MapContinuity(after, new PulseBoundary(0, 5, 0, 0)));
        Assert.AreEqual(2176000L + 4096000L + 5 * Beat90,
            LiveSplice.MapContinuity(after, new PulseBoundary(3, 5, 0, 0)));
        Assert.AreEqual(after.Initial.TotalFrames, LiveSplice.MapContinuity(after, new PulseBoundary(1, 0, 0, 0)));
        Assert.AreEqual(CountIn90, LiveSplice.MapRestart(after));
        Assert.AreEqual(0L, LiveSplice.MapRestart(Revision(Document(90, countIn: 0))));
    }

    [TestMethod]
    [DataRow(480, 2)]
    [DataRow(1024, 2)]
    [DataRow(4096, 2)]
    [DataRow(7001, 2)]
    [DataRow(480, 10)]
    [DataRow(1024, 10)]
    [DataRow(4096, 10)]
    [DataRow(7001, 10)]
    public void TempoSpliceAtAPulseGivesOneClickAtTheBoundaryAndContinuesTheBar(int quantum, int pulse)
    {
        var boundary = CountIn120 + pulse * Beat120;
        var ownNew = CountIn90 + pulse * Beat90;
        var shift = ownNew - boundary;
        var total = boundary + 130000;
        var before = Revision(Document(120));
        var after = Revision(Document(90));
        var run = new LiveRun(before, total, lag: 2 * quantum);
        run.RunTo(boundary - 10000, quantum);
        run.Queue.Post(after);
        run.RunTo(total, quantum);

        var transition = run.FirstTransition()!;
        Assert.AreEqual(boundary, transition.Timeline.SwitchFrame);
        Assert.IsTrue(transition.Cursor <= boundary && boundary < transition.Cursor + transition.Frames,
            "The boundary must be spliced inside the quantum that contains it.");
        Assert.AreSame(before, transition.Timeline.Previous);
        Assert.AreEqual(shift, run.Timeline.Active.Shift);
        Assert.AreEqual(1, run.TransitionCount());
        Assert.IsNull(run.Timeline.Previous, "The old tail window has been retired.");

        // Clicks: old spacing up to and including the boundary click, new spacing after it, none doubled or missing.
        var attacks = PrimaryAttacks(before.Initial, CountIn120, boundary)
            .Concat(PrimaryAttacks(after.Initial, ownNew).Select(frame => frame - shift))
            .TakeWhile(frame => frame <= boundary + 100000).ToArray();
        Assert.AreEqual(1, attacks.Count(frame => frame == boundary));
        for (var index = 1; index < attacks.Length; index++)
            Assert.AreEqual(attacks[index] <= boundary ? Beat120 : Beat90, attacks[index] - attacks[index - 1], $"Click {index}");
        Assert.AreEqual(boundary, attacks[pulse]);
        // The strong accent phase carries on: beat 3 -> 4 -> strong downbeat of the next bar.
        for (var step = 0; step < 6; step++)
            Assert.AreEqual((pulse + step) % 4 == 0 ? Accent.Strong : Accent.Normal,
                AccentAt(after.Initial, ownNew + step * Beat90), $"Pulse {pulse + step}");
        foreach (var frame in attacks)
            Assert.AreNotEqual(0f, run.Pcm[(int)(frame + 1) * 2], $"No click audible at {frame}.");

        var expected = Sum(Partition(before.Initial, 0, 0, total, 0.5f, attacksBeforeOwn: boundary),
            Partition(after.Initial, shift, 0, total, 0.5f, attacksFromOwn: ownNew));
        AssertSamePcm(expected, run.Pcm, total, "splice");
        var naive = Sum(Partition(before.Initial, 0, 0, total, 0.5f), Partition(after.Initial, shift, 0, total, 0.5f));
        Assert.IsFalse(naive.AsSpan(0, (int)total * 2).SequenceEqual(run.Pcm),
            "The fixture must be able to tell a doubled or early click from the correct splice.");

        // What the editor shows: the bar counter and beat continue at the splice, elapsed time stays absolute.
        var heardOld = LiveSplice.Present(transition.Timeline, boundary - 1, false);
        var heardNew = LiveSplice.Present(transition.Timeline, boundary, false);
        Assert.AreSame(before, heardOld.Revision);
        Assert.AreSame(run.Timeline.Active, heardNew.Revision);
        Assert.AreEqual((long)(pulse - 1) / 4 + 1, BarAndBeat(heardOld).Bar);
        Assert.AreEqual((pulse - 1) % 4 + 1, BarAndBeat(heardOld).Beat);
        Assert.AreEqual(ownNew, heardNew.OwnFrame);
        Assert.AreEqual(boundary, heardNew.Revision.ToAbsolute(heardNew.OwnFrame));
        for (var step = 0; step < 6; step++)
        {
            // Probe a little inside each beat: PositionAtFrame is floating point exactly on a pulse frame.
            var presented = LiveSplice.Present(run.Timeline, boundary + step * Beat90 + 100, false);
            Assert.AreEqual(((long)(pulse + step) / 4 + 1, (pulse + step) % 4 + 1), BarAndBeat(presented), $"Pulse {pulse + step}");
        }
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(63)]
    [DataRow(64)]
    [DataRow(74)]
    [DataRow(127)]
    [DataRow(128)]
    public void SplicesInAnyLoopMatchAnIndependentLongPlanAcrossInitialAndBodySeams(int quarter)
    {
        // 8 kHz, 120 -> 96 BPM: 4000 and 5000 frames per beat, so a plan compiled over four loops is an exact oracle.
        var oldLong = ScheduleCompiler.Compile(Document(120), 8000, 64, true);
        var newLong = ScheduleCompiler.Compile(Document(96), 8000, 64, true);
        var boundary = oldLong.FrameAtQuarter(quarter);
        var ownNew = newLong.FrameAtQuarter(quarter);
        var shift = ownNew - boundary;
        var total = boundary + 21000;
        var before = Revision(Document(120), 8000);
        var after = Revision(Document(96), 8000);
        var run = new LiveRun(before, total, 1f, lag: 2000);
        run.RunTo(boundary - 700, 1000);
        run.Queue.Post(after);
        run.RunTo(total, 1000);

        Assert.AreEqual(boundary, run.FirstTransition()!.Timeline.SwitchFrame);
        Assert.AreEqual(shift, run.Timeline.Active.Shift);
        var expected = Sum(Partition(oldLong, 0, 0, total, 1f, attacksBeforeOwn: boundary),
            Partition(newLong, shift, 0, total, 1f, attacksFromOwn: ownNew));
        AssertSamePcm(expected, run.Pcm, total, $"quarter {quarter}");

        // The bar counter (with the loop offset the editor adds) counts straight through the splice.
        var oldSide = LiveSplice.Present(run.FirstTransition()!.Timeline, boundary - 100, false);
        Assert.AreEqual(((long)(quarter - 1) / 4 + 1, (quarter - 1) % 4 + 1), BarAndBeat(oldSide));
        for (var step = 0; step < 6; step++)
            Assert.AreEqual(((long)(quarter + step) / 4 + 1, (quarter + step) % 4 + 1),
                BarAndBeat(LiveSplice.Present(run.Timeline, boundary + step * 5000 + 100, false)), $"Pulse {quarter + step}");
    }

    [TestMethod]
    [DataRow(3, 4, 1.0, 12000L)]
    [DataRow(8, 8, 0.5, 32000L)]
    public void MeterChangesRestartWithAStrongDownbeatAtTheBoundary(int beats, int denominator, double unit, long countIn)
    {
        // 8 kHz: the boundary is beat 3 of the first bar, 24000 frames in. Both new meters pulse every 4000 frames.
        const long boundary = 16000 + 2 * 4000;
        var before = Revision(Document(120), 8000);
        var after = Revision(Document(120, beats, denominator, unit), 8000);
        var shift = countIn - boundary;
        var total = boundary + 60000;
        var run = new LiveRun(before, total, 1f);
        run.RunTo(boundary - 1500, 1000);
        run.Queue.Post(after);
        run.RunTo(total, 1000);

        Assert.AreEqual(boundary, run.FirstTransition()!.Timeline.SwitchFrame);
        Assert.AreEqual(countIn, after.CountInFrames);
        Assert.AreEqual(shift, run.Timeline.Active.Shift);
        Assert.AreEqual(Accent.Strong, AccentAt(after.Initial, after.CountInFrames), "Beat 1 of the new meter is strong.");
        var expected = Sum(Partition(before.Initial, 0, 0, total, 1f, attacksBeforeOwn: boundary),
            Partition(after.Initial, shift, 0, total, 1f, attacksFromOwn: countIn));
        AssertSamePcm(expected, run.Pcm, total, $"{beats}/{denominator}");
        Assert.AreNotEqual(0f, run.Pcm[(int)(boundary + 1) * 2], "The new downbeat sounds at the boundary.");

        // Bar counter and beat restart at the click that changed the meter.
        Assert.AreEqual((1L, 2), BarAndBeat(LiveSplice.Present(run.FirstTransition()!.Timeline, boundary - 100, false)));
        for (var step = 0; step < beats * 2 + 1; step++)
        {
            var presented = LiveSplice.Present(run.Timeline, boundary + step * 4000 + 100, false);
            Assert.AreEqual((long)step / beats + 1, BarAndBeat(presented).Bar, $"Step {step}");
            Assert.AreEqual(step % beats + 1, BarAndBeat(presented).Beat, $"Step {step}");
            Assert.IsFalse(presented.Plan.PositionAtFrame(presented.MusicalFrame).IsCountIn);
        }
        Assert.AreEqual(countIn, LiveSplice.Present(run.Timeline, boundary, false).OwnFrame);
    }

    [TestMethod]
    public void ARequestDuringTheCountInSplicesAtTheStartOfTheBodyAndNeverReplaysTheCountIn()
    {
        var before = Revision(Document(120));
        var after = Revision(Document(90));
        const long total = CountIn120 + 200000;
        var run = new LiveRun(before, total);
        run.RunTo(30000, 1024);
        run.Queue.Post(after);
        run.RunTo(31024, 1024);
        Assert.AreEqual(new PulseBoundary(0, 0, CountIn120, CountIn120), run.Queue.ArmedBoundary,
            "The boundary is pinned to the start of the body while the count-in plays.");
        run.RunTo(total, 1024);

        Assert.AreEqual(CountIn120, run.FirstTransition()!.Timeline.SwitchFrame);
        Assert.AreEqual(CountIn90 - CountIn120, run.Timeline.Active.Shift);
        var expected = Sum(Partition(before.Initial, 0, 0, total, 0.5f, attacksBeforeOwn: CountIn120),
            Partition(after.Initial, run.Timeline.Active.Shift, 0, total, 0.5f, attacksFromOwn: CountIn90));
        AssertSamePcm(expected, run.Pcm, total, "count-in");
        // Old count-in clicks, then body clicks at the new tempo; the new revision's own count-in never sounds.
        var clicks = before.Initial.Onsets.Where(onset => onset.LaneIndex == -1 && onset.Frame < CountIn120).Select(o => o.Frame)
            .Concat(PrimaryAttacks(after.Initial, CountIn90).Select(frame => frame - run.Timeline.Active.Shift)).Take(8).ToArray();
        CollectionAssert.AreEqual(new long[] { 0, 24000, 48000, 72000, 96000, 128000, 160000, 192000 }, clicks);
        foreach (var frame in clicks) Assert.AreNotEqual(0f, run.Pcm[(int)(frame + 1) * 2], $"Click at {frame}");
        var silentBetween = run.Pcm.AsSpan((int)(72000 + 3000) * 2, (int)(CountIn120 - 72000 - 3000) * 2);
        foreach (var sample in silentBetween) Assert.AreEqual(0f, sample);
        Assert.IsNull(run.Queue.ArmedBoundary);
    }

    [TestMethod]
    public void OldClickTailsRingPastTheBoundaryAndBothPartitionsAreClampedOnce()
    {
        // Dense overlapping fixture at full gain: old tails and new attacks both exceed full scale.
        static PulseDocument Dense(double tempo)
        {
            var document = Document(tempo, countIn: 0, edit: s => s.Subdivision = 8);
            document.Lanes = Enumerable.Range(0, 8).Select(_ => new RhythmLane { Volume = 1, Pulses = 16 }).ToList();
            return document;
        }
        var before = Revision(Dense(1000), 8000);
        var after = Revision(Dense(900), 8000);
        var boundary = before.Initial.FrameAtQuarter(5);
        var ownNew = after.Initial.FrameAtQuarter(5);
        var shift = ownNew - boundary;
        const long total = 6000;
        var run = new LiveRun(before, total, 1f);
        run.RunTo(boundary - 300, 512);
        run.Queue.Post(after);
        run.RunTo(total, 512);
        Assert.AreEqual(boundary, run.FirstTransition()!.Timeline.SwitchFrame);

        var oldPart = Partition(before.Initial, 0, 0, total, 1f, attacksBeforeOwn: boundary);
        var newPart = Partition(after.Initial, shift, 0, total, 1f, attacksFromOwn: ownNew);
        AssertSamePcm(Sum(oldPart, newPart), run.Pcm, total, "dense");
        var tail = oldPart.AsSpan((int)boundary * 2, 800);
        Assert.IsTrue(tail.ToArray().Any(sample => sample != 0), "Old attacks from before the boundary keep ringing.");
        Assert.IsTrue(Enumerable.Range((int)boundary * 2, 800).Any(index => Math.Abs(oldPart[index] + newPart[index]) > 1),
            "The summed partitions must exceed full scale for the clamp to matter.");
        Assert.IsTrue(Enumerable.Range((int)boundary * 2, 800).Any(index =>
            Math.Clamp(oldPart[index], -1f, 1f) + Math.Clamp(newPart[index], -1f, 1f) != Math.Clamp(oldPart[index] + newPart[index], -1f, 1f)),
            "Clamping each partition separately would sound different from clamping the sum once.");
        Assert.IsTrue(run.Pcm.All(sample => sample is >= -1f and <= 1f));
    }

    [TestMethod]
    public void TwoSuccessiveSplicesKeepTheBarPositionAndEveryClickExactlyOnce()
    {
        var first = Revision(Document(120));
        var second = Revision(Document(90));
        var third = Revision(Document(100));
        const long firstBoundary = CountIn120 + 2 * Beat120;
        const long total = 420000;
        var run = new LiveRun(first, total, lag: 2048);
        run.RunTo(firstBoundary - 10000, 1024);
        run.Queue.Post(second);
        run.RunTo(180000, 1024);
        Assert.IsNull(run.Timeline.Previous);
        var firstShift = CountIn90 + 2 * Beat90 - firstBoundary;
        Assert.AreEqual(firstShift, run.Timeline.Active.Shift);
        // The next pulse of the spliced timeline (bar 2 downbeat) is at 208000 absolute.
        run.Queue.Post(third);
        run.RunTo(total, 1024);

        const long secondBoundary = 208000;
        var thirdShift = 115200 + 4 * 28800 - secondBoundary;
        Assert.AreEqual(2, run.TransitionCount());
        Assert.AreEqual(thirdShift, run.Timeline.Active.Shift);
        var expected = Sum(Sum(
            Partition(first.Initial, 0, 0, total, 0.5f, attacksBeforeOwn: firstBoundary),
            Partition(second.Initial, firstShift, 0, total, 0.5f, attacksFromOwn: CountIn90 + 2 * Beat90,
                attacksBeforeOwn: secondBoundary + firstShift)),
            Partition(third.Initial, thirdShift, 0, total, 0.5f, attacksFromOwn: 115200 + 4 * 28800));
        AssertSamePcm(expected, run.Pcm, total, "two splices");
        // Beats 3, 4 of bar 1 at 90 BPM, then bar 2 beats 1.. at 100 BPM; each probed inside its beat.
        var wanted = new (long Frame, long Bar, int Beat)[]
        {
            (144000, 1, 3), (176000, 1, 4), (208000, 2, 1), (236800, 2, 2), (265600, 2, 3), (294400, 2, 4), (323200, 3, 1)
        };
        foreach (var (frame, bar, beat) in wanted)
            Assert.AreEqual((bar, beat), BarAndBeat(LiveSplice.Present(run.TimelineAt(frame + 100), frame + 100, false)),
                $"Frame {frame}");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(500)]
    [DataRow(999)]
    [DataRow(1000)]
    public void ABoundaryAtAnyEdgeOfAQuantumIsSplicedWithoutAGapOrADoubleClick(int offset)
    {
        // The quantum starts `offset` frames before the boundary: 0 = first frame of the quantum,
        // 999 = its last frame, 1000 = first frame of the following quantum.
        const long boundary = CountIn120 + 2 * Beat120, shift = CountIn90 + 2 * Beat90 - boundary, total = 200000;
        var before = Revision(Document(120));
        var after = Revision(Document(90));
        var run = new LiveRun(before, total, lag: 2000);
        run.RunTo(boundary - offset, 1000);
        Assert.AreEqual(boundary - offset, run.Cursor);
        run.Queue.Post(after);
        run.RunTo(total, 1000);

        var transition = run.FirstTransition()!;
        Assert.AreEqual(offset == 1000 ? boundary : boundary - offset, transition.Cursor);
        Assert.IsTrue(transition.Cursor <= boundary && boundary < transition.Cursor + transition.Frames);
        Assert.AreEqual(boundary, transition.Timeline.SwitchFrame);
        Assert.AreEqual(shift, run.Timeline.Active.Shift);
        var expected = Sum(Partition(before.Initial, 0, 0, total, 0.5f, attacksBeforeOwn: boundary),
            Partition(after.Initial, shift, 0, total, 0.5f, attacksFromOwn: CountIn90 + 2 * Beat90));
        AssertSamePcm(expected, run.Pcm, total, $"offset {offset}");
    }

    [TestMethod]
    public void FramesBeforeAnEarlyPlacedRevisionBeginsStaySilentWithoutThrowing()
    {
        var before = Revision(Document(120), 8000);
        var restarted = LiveSplice.Splice(before, Revision(Document(120, beats: 3, countIn: 0), 8000),
            LiveSplice.NextPulseBoundary(before, 17000)!.Value);
        Assert.IsTrue(restarted.Shift < 0);
        var samples = new float[4096 * 2];
        LiveSplice.Render(samples, 0, 4096, new LiveTimeline(restarted), 0.5f, false, new float[32768 * 2]);
        Assert.IsTrue(samples.All(sample => sample == 0));
    }

    [TestMethod]
    public void MuteSilencesTheWholeSpliceAndGainScalesBothPartitions()
    {
        var before = Revision(Document(120), 8000);
        var after = Revision(Document(96), 8000);
        var boundary = LiveSplice.NextPulseBoundary(before, 17000)!.Value;
        var timeline = new LiveTimeline(LiveSplice.Splice(before, after, boundary), before, boundary.Frame);
        var scratch = new float[32768 * 2];
        var muted = new float[8000 * 2];
        LiveSplice.Render(muted, boundary.Frame - 1000, 8000, timeline, 0.5f, true, scratch);
        Assert.IsTrue(muted.All(sample => sample == 0));
        var loud = new float[8000 * 2];
        var quiet = new float[8000 * 2];
        LiveSplice.Render(loud, boundary.Frame - 1000, 8000, timeline, 0.5f, false, scratch);
        LiveSplice.Render(quiet, boundary.Frame - 1000, 8000, timeline, 0.25f, false, scratch);
        Assert.IsTrue(loud.Any(sample => sample != 0));
        for (var index = 0; index < loud.Length; index++) Assert.AreEqual(loud[index] / 2, quiet[index], 1e-6f);
    }
}
