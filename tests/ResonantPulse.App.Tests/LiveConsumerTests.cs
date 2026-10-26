using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.Core;
using static ResonantPulse.App.Tests.LiveTestKit;

namespace ResonantPulse.App.Tests;

/// <summary>
/// The window's consumers of a placed revision (practice countdown, keyboard-timing expectations) take ABSOLUTE
/// device frames and must convert them with the revision's Shift. These tests use revisions whose Shift is not zero,
/// in both directions, so mixing the two timelines up cannot pass.
/// </summary>
[TestClass]
public sealed class LiveConsumerTests
{
    // 48 kHz, 120 BPM: beat 24000, one-bar count-in ends at 96000. 90 BPM: beat 32000, count-in ends at 128000.
    // 3/4 at 120 BPM: three-beat count-in ends at 72000.
    private const int Rate = 48000;

    private static PlaybackRevision Spliced(PlaybackRevision before, PlaybackRevision after, long cursor = 140000)
    {
        var spliced = LiveSplice.Splice(before, after, LiveSplice.NextPulseBoundary(before, cursor)!.Value);
        Assert.AreNotEqual(0L, spliced.Shift, "The scenario must place the revision away from its own origin.");
        return spliced;
    }

    [TestMethod]
    public void PracticeTimeLeftIsMeasuredInDeviceFramesAfterATempoSplice()
    {
        var before = Revision(Document(120, edit: s => s.PracticeSeconds = 10));
        var spliced = Spliced(before, Revision(Document(90, edit: s => s.PracticeSeconds = 10)));
        Assert.AreEqual(48000L, spliced.Shift);
        Assert.AreEqual(96000L + 10 * Rate, spliced.EndFrameAbsolute, "The wall-clock stop keeps its device frame.");
        // 576000 - 300000 = 276000 frames = 5.75 s, shown as 6 s. Ignoring the shift would show 7 s.
        Assert.AreEqual(276000L, spliced.FramesRemainingAt(300000));
        Assert.AreEqual(0L, spliced.FramesRemainingAt(576000));
        Assert.AreEqual(0L, spliced.FramesRemainingAt(900000));
    }

    [TestMethod]
    public void PracticeTimeLeftIsMeasuredInDeviceFramesAfterAMeterRestart()
    {
        var before = Revision(Document(120, edit: s => s.PracticeSeconds = 10));
        var spliced = Spliced(before, Revision(Document(120, beats: 3, edit: s => s.PracticeSeconds = 10)));
        Assert.AreEqual(-72000L, spliced.Shift);
        Assert.AreEqual(96000L + 10 * Rate, spliced.EndFrameAbsolute);
        Assert.AreEqual(276000L, spliced.FramesRemainingAt(300000));
    }

    [TestMethod]
    public void CountedStopsRestartFromTheSpliceBecauseTheyBelongToTheNewRhythm()
    {
        var before = Revision(Document(120, edit: s => s.PracticeBeats = 8));
        var spliced = Spliced(before, Revision(Document(120, beats: 3, edit: s => s.PracticeBeats = 8)));
        // Bar 1 beat 1 of the new meter is the boundary at device frame 144000; eight beats of 24000 follow it.
        Assert.AreEqual(144000L + 8 * 24000, spliced.EndFrameAbsolute);
        Assert.AreEqual(336000L - 200000, spliced.FramesRemainingAt(200000));
    }

    [TestMethod]
    public void NoStopConditionMeansNoCountdown() =>
        Assert.IsNull(Spliced(Revision(Document(120)), Revision(Document(90))).FramesRemainingAt(300000));

    [TestMethod]
    public void KeyboardTimingExpectationsFollowAPlacedTempoRevision()
    {
        var spliced = Spliced(Revision(Document(120)), Revision(Document(90)));
        // The new beat is 32000 frames and the splice boundary 144000 is a click, so clicks fall at 144000 + k * 32000.
        const long start = 150000;
        var expected = spliced.TargetSeconds(-1, false, start, start + 2 * Rate);
        AssertSeconds(expected, start, 176000, 208000, 240000);
    }

    [TestMethod]
    public void KeyboardTimingExpectationsFollowAPlacedMeterRevision()
    {
        var spliced = Spliced(Revision(Document(120)), Revision(Document(120, beats: 3)));
        // The boundary 144000 is bar 1 beat 1 of 3/4 and every beat is 24000 frames.
        const long start = 150000;
        var expected = spliced.TargetSeconds(-1, false, start, start + 72000);
        AssertSeconds(expected, start, 168000, 192000, 216000);
    }

    [TestMethod]
    public void KeyboardTimingExpectationsOfAnUnplacedRevisionAreUnchanged()
    {
        var revision = Revision(Document(120));
        Assert.AreEqual(0L, revision.Shift);
        // After the one-bar count-in (never a target) the clicks fall every 24000 frames from 96000.
        var expected = revision.TargetSeconds(-1, false, 0, 192000);
        AssertSeconds(expected, 0, 96000, 120000, 144000, 168000);
    }

    private static void AssertSeconds(List<double> actual, long startFrame, params long[] absoluteClickFrames)
    {
        Assert.AreEqual(absoluteClickFrames.Length, actual.Count, string.Join(", ", actual));
        for (var index = 0; index < actual.Count; index++)
            Assert.AreEqual((double)(absoluteClickFrames[index] - startFrame) / Rate, actual[index], 1e-9, $"click {index}");
    }
}
