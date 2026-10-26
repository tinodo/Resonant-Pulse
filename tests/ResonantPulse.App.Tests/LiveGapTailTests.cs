using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.Core;
using static ResonantPulse.App.Tests.LiveTestKit;

namespace ResonantPulse.App.Tests;

/// <summary>
/// A gap is an output mask, not just the absence of new attacks (PcmRenderer.Fill): a click ringing out of one bar must
/// not sound in the next when that bar is a gap. A live splice mixes the old revision's ringing tails under the new
/// one, so the new revision's mask has to silence them too, in the new revision's own timeline.
/// </summary>
[TestClass]
public sealed class LiveGapTailTests
{
    // 8 kHz, 120 BPM: a beat is 4000 frames, a bar 16000, the count-in is one bar and a loop is 16 bars.
    // Sixteen subdivisions put a click every 250 frames (31 ms) and a click rings for 400 frames (50 ms), so the click
    // just before a bar line is still sounding 150 frames into the bar.
    private const int Rate = 8000, Quantum = 1000, Bar = 16000;
    private const long CountIn = 16000;

    private static PulseDocument Gapped(int audible, int silent) =>
        Document(120, edit: s => { s.Subdivision = 16; s.AudibleBars = audible; s.SilentBars = silent; });

    private static PulseDocument Ungapped() => Document(120, edit: s => s.Subdivision = 16);

    private static PulseDocument Waltz() =>
        Document(120, beats: 3, edit: s => { s.Subdivision = 16; s.AudibleBars = 1; s.SilentBars = 1; });

    /// <summary>
    /// Clears the absolute frames at or after <paramref name="from"/> that <paramref name="plan"/> marks silent when its
    /// own timeline is absolute + <paramref name="shift"/>. A plain loop over the plan's silent ranges, independent of
    /// the renderer under test.
    /// </summary>
    private static void ClearSilence(float[] pcm, RenderPlan plan, long shift, long from)
    {
        foreach (var (start, end) in plan.SilentRanges)
            for (var frame = Math.Max(from, start - shift); frame < end - shift && frame * 2 < pcm.Length; frame++)
                pcm[frame * 2] = pcm[frame * 2 + 1] = 0f;
    }

    private static bool Rings(float[] pcm, long from, int frames) =>
        Enumerable.Range((int)from * 2, frames * 2).Any(index => pcm[index] != 0f);

    [TestMethod]
    [DataRow(2, DisplayName = "first loop, inside Initial")]
    [DataRow(18, DisplayName = "second loop, a Body cycle")]
    public void RingingTailsDoNotLeakIntoABarTheNewRevisionKeepsSilent(int loopBar)
    {
        // Old: three audible bars then one silent. New: two and two. Bar 2 of each four is audible before, silent after.
        var oldLong = ScheduleCompiler.Compile(Gapped(3, 1), Rate, 64, true);
        var newLong = ScheduleCompiler.Compile(Gapped(2, 2), Rate, 64, true);
        var boundary = CountIn + loopBar * (long)Bar;
        Assert.AreEqual(boundary, oldLong.FrameAtQuarter(4 * loopBar), "Quarters count from the first loop bar, after the count-in.");
        var before = Revision(Gapped(3, 1), Rate);
        var after = Revision(Gapped(2, 2), Rate);
        Assert.IsTrue(LiveSplice.PreservesLoop(before, after), "Same bar structure, so the rhythm continues with no shift.");

        var total = boundary + 2L * Bar;
        var run = new LiveRun(before, total, 1f, lag: 2000);
        run.RunTo(boundary - 700, Quantum);
        run.Queue.Post(after);
        run.RunTo(total, Quantum);
        Assert.AreEqual(boundary, run.FirstTransition()!.Timeline.SwitchFrame);
        Assert.AreEqual(0L, run.Timeline.Active.Shift);

        // Fixture guard: under the old revision's mask alone the previous click would ring across the bar line.
        var oldTail = Partition(oldLong, 0, 0, total, 1f, attacksBeforeOwn: boundary);
        Assert.IsTrue(Rings(oldTail, boundary, 150), "The fixture must put an audible click tail across the bar line.");

        // Both silent bars are silent from their very first frame.
        for (var frame = boundary; frame < total; frame++)
            if (run.Pcm[frame * 2] != 0f || run.Pcm[frame * 2 + 1] != 0f)
                Assert.Fail($"Frame {frame - boundary} of the new silent bars is audible.");

        var expected = Sum(oldTail, Partition(newLong, 0, 0, total, 1f, attacksFromOwn: boundary));
        ClearSilence(expected, newLong, 0, boundary);
        AssertSamePcm(expected, run.Pcm, total, "splice into a gap");
    }

    [TestMethod]
    public void TailsStillRingWhenTheNewRevisionIsAudibleAtTheBoundary()
    {
        // Old: three audible bars then one silent. New: no gaps. Nothing may be masked after the splice.
        const long boundary = CountIn + 2L * Bar, total = boundary + Bar;
        var oldLong = ScheduleCompiler.Compile(Gapped(3, 1), Rate, 64, true);
        var newLong = ScheduleCompiler.Compile(Ungapped(), Rate, 64, true);
        var before = Revision(Gapped(3, 1), Rate);
        var after = Revision(Ungapped(), Rate);
        Assert.IsTrue(LiveSplice.PreservesLoop(before, after));
        Assert.AreEqual(0, newLong.SilentRanges.Count);

        var run = new LiveRun(before, total, 1f, lag: 2000);
        run.RunTo(boundary - 700, Quantum);
        run.Queue.Post(after);
        run.RunTo(total, Quantum);
        Assert.AreEqual(boundary, run.FirstTransition()!.Timeline.SwitchFrame);

        var oldTail = Partition(oldLong, 0, 0, total, 1f, attacksBeforeOwn: boundary);
        Assert.IsTrue(Rings(oldTail, boundary, 150), "The fixture must put an audible click tail across the bar line.");
        AssertSamePcm(Sum(oldTail, Partition(newLong, 0, 0, total, 1f, attacksFromOwn: boundary)), run.Pcm, total,
            "splice out of a gap");
    }

    [TestMethod]
    public void TheMaskFollowsTheNewRevisionsOwnTimelineAfterARestart()
    {
        // A meter change restarts the rhythm: the boundary becomes bar 1 of the new revision, an audible bar, so the
        // old tail keeps ringing. Reading the new plan's mask in absolute frames instead of its own timeline would
        // land inside its silent bar and wrongly cut the tail. Bar 1 of the old rhythm is audible, so a tail exists.
        const long boundary = CountIn + 2L * Bar;
        var before = Revision(Gapped(3, 1), Rate);
        var after = Revision(Waltz(), Rate);
        Assert.IsFalse(LiveSplice.PreservesLoop(before, after));
        var oldLong = ScheduleCompiler.Compile(Gapped(3, 1), Rate, 64, true);
        var newLong = ScheduleCompiler.Compile(Waltz(), Rate, 64, true);
        var shift = after.CountInFrames - boundary;
        var total = boundary + 3L * 12000;

        var run = new LiveRun(before, total, 1f, lag: 2000);
        run.RunTo(boundary - 700, Quantum);
        run.Queue.Post(after);
        run.RunTo(total, Quantum);
        Assert.AreEqual(boundary, run.FirstTransition()!.Timeline.SwitchFrame);
        Assert.AreEqual(shift, run.Timeline.Active.Shift);

        var oldTail = Partition(oldLong, 0, 0, total, 1f, attacksBeforeOwn: boundary);
        Assert.IsTrue(Rings(oldTail, boundary, 150), "The fixture must put an audible click tail across the bar line.");
        Assert.IsTrue(newLong.SilentRanges.Any(range => range.StartFrame <= boundary && boundary < range.EndFrame),
            "Fixture: the boundary's absolute frame must fall inside a silent bar of the new plan, to catch a missing shift.");
        var expected = Sum(oldTail, Partition(newLong, shift, 0, total, 1f, attacksFromOwn: after.CountInFrames));
        ClearSilence(expected, newLong, shift, boundary);
        Assert.IsTrue(Rings(expected, boundary, 150), "The tail rings into the audible first bar of the new rhythm.");
        AssertSamePcm(expected, run.Pcm, total, "restart onto an audible bar");
    }
}
