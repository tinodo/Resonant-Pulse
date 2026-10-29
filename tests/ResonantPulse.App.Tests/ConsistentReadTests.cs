using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using static ResonantPulse.App.Tests.LiveTestKit;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class ConsistentReadTests
{
    [TestMethod]
    public void ARetiredPreviousRevisionIsNeverPairedWithAPreSpliceCursor()
    {
        // 8 kHz, 120 BPM: pulses every 4000 frames from 16000. A meter change restarts the rhythm on the new revision.
        var before = Revision(Document(120), 8000);
        var after = Revision(Document(120, beats: 3), 8000);
        var boundary = LiveSplice.NextPulseBoundary(before, 40000)!.Value;
        var spliced = LiveSplice.Splice(before, after, boundary);
        var retired = new LiveTimeline(spliced, before, boundary.Frame).Retire();
        var preSplice = boundary.Frame - 1000;
        var postSplice = boundary.Frame + 5000;

        // Fixture guard: pairing the old cursor with the retired timeline maps a frame of the old bar into the new count-in.
        var wrong = LiveSplice.Present(retired, preSplice, false);
        Assert.AreSame(spliced, wrong.Revision);
        Assert.IsTrue(wrong.OwnFrame < spliced.CountInFrames, "This is the torn pairing the retry has to prevent.");

        // The audio thread advances the cursor and retires the previous revision while the UI thread is between its reads.
        var cursor = preSplice;
        var timelineReads = 0;
        var (processed, timeline) = ConsistentRead.Read(() => cursor, () =>
        {
            if (timelineReads++ == 0) cursor = postSplice;
            return retired;
        });

        Assert.AreEqual(postSplice, processed);
        Assert.AreSame(retired, timeline);
        Assert.AreEqual(2, timelineReads, "The torn pair is discarded and read again.");
        var presented = LiveSplice.Present(timeline, processed, false);
        Assert.AreSame(spliced, presented.Revision);
        Assert.IsTrue(presented.OwnFrame >= spliced.CountInFrames, "After the splice the position is inside the new revision's bars.");
    }

    [TestMethod]
    public void ACursorThatDoesNotMoveIsReadTwiceAndTheTimelineOnce()
    {
        var cursorReads = 0;
        var timelineReads = 0;
        var (cursor, value) = ConsistentRead.Read(() => { cursorReads++; return 42L; }, () => { timelineReads++; return "timeline"; });
        Assert.AreEqual((42L, "timeline"), (cursor, value));
        Assert.AreEqual((2, 1), (cursorReads, timelineReads));
    }

    [TestMethod]
    public void ACursorThatNeverSettlesStopsAfterTheBoundAndReturnsTheNewestCursor()
    {
        long cursor = 0;
        var timelineReads = 0;
        var (processed, value) = ConsistentRead.Read(() => cursor++, () => ++timelineReads);
        Assert.AreEqual(ConsistentRead.MaxAttempts, timelineReads);
        Assert.AreEqual(timelineReads, value);
        Assert.AreEqual(2L * ConsistentRead.MaxAttempts - 1, processed);
    }
}
