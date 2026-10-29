using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class PlaybackDeadlineTests
{
    private static (RenderPlan Initial, RenderPlan Body) Plans(PulseDocument document) =>
        (ScheduleCompiler.Compile(document, 48000, 2, true),
         ScheduleCompiler.Compile(document, 48000, 2, false));

    [TestMethod]
    [DataRow(1)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(16)]
    [DataRow(17)]
    [DataRow(24)]
    [DataRow(6400000)]
    public void BeatStopsCrossIntroAndRepeatedBodySeamsWithoutRepeatingCountIn(int beats)
    {
        var document = new PulseDocument { Settings = new() { PracticeBeats = beats } };
        var plans = Plans(document);
        Assert.AreEqual(96000L + beats * 24000L,
            PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, false)!.Value);
        Assert.IsTrue(PlaybackDeadline.IsEnabled(document.Settings));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(100000)]
    public void BarStopsUseWholeBarsAndLongFrameCoordinatesAcrossLoops(int bars)
    {
        var document = new PulseDocument { Settings = new() { PracticeBars = bars } };
        var plans = Plans(document);
        Assert.AreEqual(96000L + bars * 96000L,
            PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, false)!.Value);
    }

    [TestMethod]
    [DataRow(3, 0, 108000L)]
    [DataRow(0, 2, 216000L)]
    [DataRow(100, 0, 1272000L)]
    public void WrittenEighthBeatsRemainIndependentOfDottedBpmUnitsSubdivisionsAndSilence(
        int beats, int bars, long expected)
    {
        var document = new PulseDocument
        {
            Settings = new()
            {
                Tempo = 80, BeatUnitQuarters = 1.5, BeatsPerBar = 6, BeatDenominator = 8,
                Subdivision = 6, Accents = [Accent.Silent], AudibleBars = 1, SilentBars = 1,
                PracticeBeats = beats, PracticeBars = bars
            }
        };
        var plans = Plans(document);
        Assert.AreEqual(expected,
            PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, false)!.Value);
    }

    [TestMethod]
    [DataRow("LinearBeat", 4, 0, 5)]
    [DataRow("LinearBeat", 14, 0, 25)]
    [DataRow("LinearBeat", 0, 3, 23)]
    [DataRow("LinearBeat", 0, 4, 33)]
    [DataRow("LinearTime", 4, 0, 5)]
    [DataRow("LinearTime", 14, 0, 25)]
    [DataRow("LinearTime", 0, 3, 23)]
    [DataRow("LinearTime", 0, 4, 33)]
    public void MixedMeterRampAndRepeatStopsUseExactAuthoredMusicalCoordinates(
        string ramp, int beats, int bars, int quarterNumerator)
    {
        var document = new PulseDocument
        {
            Settings = new() { PracticeBeats = beats, PracticeBars = bars },
            Program =
            [
                new() { Bars = 1, BeatsPerBar = 3, BeatDenominator = 8, StartTempo = 120, EndTempo = 120 },
                new() { Bars = 2, Repeats = 2, BeatsPerBar = 5, BeatDenominator = 4,
                    StartTempo = 90, EndTempo = 180, Ramp = ramp }
            ]
        };
        var plans = Plans(document);
        var expected = plans.Initial.FrameAtQuarter(new Rational(quarterNumerator, 2));
        Assert.AreEqual(expected,
            PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, true)!.Value);
        Assert.AreEqual(0,
            PlaybackDeadline.RenderCount(expected, 512,
                PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, true)));
    }

    [TestMethod]
    public void EarliestEnabledConditionWinsAndFiniteProgramsNeverExtendToRequestedCounts()
    {
        var document = new PulseDocument
        {
            Settings = new() { PracticeSeconds = 2, PracticeBeats = 3, PracticeBars = 1 }
        };
        var plans = Plans(document);
        Assert.AreEqual(168000L,
            PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, false)!.Value);
        document.Settings.PracticeSeconds = 1;
        Assert.AreEqual(144000L,
            PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, false)!.Value);
        document.Settings.PracticeSeconds = 10;
        document.Settings.PracticeBeats = 100;
        document.Settings.PracticeBars = 100;
        document.Program = [new() { Bars = 1, StartTempo = 120, EndTempo = 120 }];
        plans = Plans(document);
        Assert.AreEqual(plans.Initial.TotalFrames,
            PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, true)!.Value);
    }

    [TestMethod]
    public void DisabledCountedLimitsPreserveLegacyInfiniteAndFinitePlayback()
    {
        var document = new PulseDocument();
        var plans = Plans(document);
        Assert.IsFalse(PlaybackDeadline.IsEnabled(document.Settings));
        Assert.IsNull(PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, false));
        Assert.AreEqual(plans.Initial.TotalFrames,
            PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, true)!.Value);
    }

    [TestMethod]
    public void CountedStopsWithoutCountInDoNotAlterAuthoredPlanDuration()
    {
        var document = new PulseDocument { Settings = new() { CountInBars = 0, PracticeBeats = 5 } };
        var plans = Plans(document);
        Assert.AreEqual(192000L, plans.Initial.TotalFrames);
        Assert.AreEqual(120000L,
            PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, false)!.Value);
        document.Settings.PracticeBars = 3;
        document.Settings.PracticeBeats = 0;
        Assert.AreEqual(288000L,
            PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, false)!.Value);
        Assert.AreEqual(192000L, plans.Initial.TotalFrames);
        document.Settings.PracticeBars = -1;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, false));
        document.Settings.PracticeBars = 0;
        document.Settings.PracticeBeats = 6400001;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlaybackDeadline.Resolve(plans.Initial, plans.Body, document.Settings, false));
    }

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
