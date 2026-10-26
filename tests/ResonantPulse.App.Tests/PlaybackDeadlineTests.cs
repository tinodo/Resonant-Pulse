using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class PlaybackDeadlineTests
{
    [TestMethod]
    public void DisabledTimerKeepsInfinitePlaybackAndFiniteProgramEnd()
    {
        Assert.IsNull(PlaybackDeadline.Resolve(48000, 96000, 0, null));
        Assert.AreEqual(240000L, PlaybackDeadline.Resolve(48000, 96000, 0, 240000)!.Value);
        Assert.AreEqual(512, PlaybackDeadline.RenderCount(123456789, 512, null));
    }

    [TestMethod]
    [DataRow(48000, 96000L, 240000L)]
    [DataRow(44100, 88200L, 220500L)]
    public void TimerExcludesCountInAndResolvesAtActualSampleRate(int sampleRate, long countIn, long expected)
    {
        Assert.AreEqual(expected, PlaybackDeadline.Resolve(sampleRate, countIn, 3, null)!.Value);
        Assert.AreEqual(expected, PlaybackDeadline.Resolve(sampleRate, countIn, 3, expected * 2)!.Value);
        Assert.AreEqual(countIn + 1, PlaybackDeadline.Resolve(sampleRate, countIn, 3, countIn + 1)!.Value);
    }

    [TestMethod]
    [DataRow(90L, 20, 100L, 10)]
    [DataRow(100L, 20, 100L, 0)]
    [DataRow(110L, 20, 100L, 0)]
    [DataRow(0L, 20, 100L, 20)]
    public void RenderingNeverIncludesDeadlineFrameOrLaterFrames(long start, int requested, long end, int expected) =>
        Assert.AreEqual(expected, PlaybackDeadline.RenderCount(start, requested, end));

    [TestMethod]
    public void CompletedPositionUsesLastRenderedFrameWithoutRewindingElapsedTime()
    {
        Assert.AreEqual(99L, PlaybackDeadline.PositionFrame(100, 100, true));
        Assert.AreEqual(99L, PlaybackDeadline.PositionFrame(120, 100, true));
        Assert.AreEqual(80L, PlaybackDeadline.PositionFrame(80, 100, true));
        Assert.AreEqual(100L, PlaybackDeadline.PositionFrame(100, 100, false));
        Assert.AreEqual(100L, PlaybackDeadline.PositionFrame(100, null, true));
    }

    [TestMethod]
    public void TimerMetadataRejectsInvalidBoundsAndOverflow()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlaybackDeadline.Resolve(0, 0, 1, null));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlaybackDeadline.Resolve(48000, -1, 1, null));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlaybackDeadline.Resolve(48000, 0, -1, null));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlaybackDeadline.Resolve(48000, 0, 86401, null));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlaybackDeadline.Resolve(48000, 96000, 1, 96000));
        Assert.ThrowsExactly<OverflowException>(() => PlaybackDeadline.Resolve(48000, long.MaxValue, 1, null));
        Assert.AreEqual(4147200000L, PlaybackDeadline.Resolve(48000, 0, 86400, null)!.Value);
    }

    [TestMethod]
    public void FadeReachesZeroOnLastAllowedFrameAndMatchesAcrossQuanta()
    {
        var whole = Enumerable.Repeat(1f, 960).ToArray();
        var chunked = whole.ToArray();
        PlaybackDeadline.FadeAtEnd(whole, 0, 48000, 480);
        for (var at = 0; at < 480; at += 64)
            PlaybackDeadline.FadeAtEnd(chunked.AsSpan(at * 2, Math.Min(64, 480 - at) * 2), at, 48000, 480);

        CollectionAssert.AreEqual(whole, chunked);
        Assert.IsTrue(whole.Take(480).All(sample => sample == 1));
        Assert.AreEqual(0f, whole[^1]);
        Assert.AreEqual(0f, whole[^2]);
        for (var frame = 0; frame < 480; frame++)
        {
            Assert.AreEqual(whole[frame * 2], whole[frame * 2 + 1]);
            Assert.IsTrue(whole[frame * 2] is >= 0 and <= 1);
        }
    }
}
