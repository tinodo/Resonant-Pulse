using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class ClickGridTests
{
    private const int Rate = 48000;

    /// <summary>The "Compound 6/8" ready-made rhythm: 6/8 felt in two, each beat split in three.</summary>
    private static PulseSettings Compound() => new()
    {
        Tempo = 80, BeatsPerBar = 6, BeatDenominator = 8, BeatUnitQuarters = 1.5, Subdivision = 3,
        Accents = [Accent.Strong, Accent.Normal], Groups = [3, 3], CountInBars = 0
    };

    private static string[] Labels(ClickGrid grid) => Enumerable.Range(0, grid.Count).Select(grid.Label).ToArray();

    [TestMethod]
    public void ACompoundBarFeltInTwoShowsSixCellsOneForEveryClickItPlays()
    {
        var settings = Compound();
        var grid = ClickGrid.Of(settings);
        Assert.AreEqual(2, grid.Beats);
        Assert.AreEqual(3, grid.Split);
        Assert.AreEqual(6, grid.Count);
        Assert.AreEqual(4, grid.SplitSlots);
        CollectionAssert.AreEqual(new[] { false, true, true, false, true, true },
            Enumerable.Range(0, grid.Count).Select(grid.IsSplit).ToArray());
        CollectionAssert.AreEqual(new[] { "1", "2", "3", "4", "5", "6" }, Labels(grid), "numbered as the dial and status line count");
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Soft, Accent.Soft, Accent.Strong, Accent.Soft, Accent.Soft },
            grid.Levels(settings), "the second beat is Strong because a group starts there; split clicks are Soft unless set");
        CollectionAssert.AreEquivalent(new[] { 3 }, grid.SpaceBefore().ToArray());

        var plan = ScheduleCompiler.Compile(new PulseDocument { Settings = settings }, Rate, 1, false);
        Assert.AreEqual(grid.Count, plan.Onsets.Count, "the strip has a cell for every click the schedule plays");
    }

    [TestMethod]
    public void SplitClickStrengthsAreShownInBarOrderAndRepeatLikeCore()
    {
        var settings = Compound();
        settings.SubdivisionAccents = [Accent.Normal, Accent.Silent, Accent.Strong, Accent.Soft];
        var grid = ClickGrid.Of(settings);
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Normal, Accent.Silent, Accent.Strong, Accent.Strong, Accent.Soft },
            grid.Levels(settings));
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, new[] { grid.SlotOf(1), grid.SlotOf(2), grid.SlotOf(4), grid.SlotOf(5) });

        settings.SubdivisionAccents = [Accent.Normal];
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Normal, Accent.Normal, Accent.Strong, Accent.Normal, Accent.Normal },
            ClickGrid.Of(settings).Levels(settings), "one entry repeats over every split click");
    }

    [TestMethod]
    public void ClicksBetweenWrittenBeatsAreDotsAndAMismatchedBeatKeepsItsWrittenNumber()
    {
        var eighths = ClickGrid.Of(new PulseSettings { Subdivision = 2 });
        CollectionAssert.AreEqual(new[] { "1", "·", "2", "·", "3", "·", "4", "·" }, Labels(eighths));
        CollectionAssert.AreEquivalent(new[] { 2, 4, 6 }, eighths.SpaceBefore().ToArray(), "each split beat is set apart");

        // 4/4 counted in half notes: the two beats are written beats 1 and 3.
        CollectionAssert.AreEqual(new[] { "1", "3" }, Labels(ClickGrid.Of(new PulseSettings { BeatUnitQuarters = 2 })));
        // 4/4 in quarters, no split: one cell per written beat, as before.
        CollectionAssert.AreEqual(new[] { "1", "2", "3", "4" }, Labels(ClickGrid.Of(new PulseSettings())));
        Assert.AreEqual(0, ClickGrid.Of(new PulseSettings()).SpaceBefore().Count);
    }

    [TestMethod]
    public void ABeatThatFallsBetweenWrittenBeatsIsNumberedLikeEveryOtherBeat()
    {
        // An eighth note in 4/4: every other beat falls between two written beats, so all of them count 1, 2, 3 and on
        // (written-beat numbers for some and pulse numbers for the rest gave 1 2 2 4 3 6 4 8).
        var eighths = new PulseSettings { BeatUnitQuarters = 0.5 };
        CollectionAssert.AreEqual(new[] { "1", "2", "3", "4", "5", "6", "7", "8" }, Labels(ClickGrid.Of(eighths)));
        eighths.Subdivision = 2;
        CollectionAssert.AreEqual(
            new[] { "1", "·", "2", "·", "3", "·", "4", "·", "5", "·", "6", "·", "7", "·", "8", "·" }, Labels(ClickGrid.Of(eighths)));

        // A dotted quarter in 4/4: three beats, the last cut short by the bar line.
        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, Labels(ClickGrid.Of(new PulseSettings { BeatUnitQuarters = 1.5 })));
    }

    [TestMethod]
    public void ABarWhoseUnitDoesNotFitShowsItsBeatsAloneBecauseItsLastBeatIsCutShort()
    {
        // 4/4 counted in dotted quarters, each beat split in three: Core plays eight clicks in the bar, not nine, because the click
        // that would fall on the bar line is cut. Cells for nine would promise one that never sounds.
        var settings = new PulseSettings { BeatUnitQuarters = 1.5, Subdivision = 3, CountInBars = 0 };
        Assert.IsTrue(MeterPresets.UnitNeedsFix(settings));
        var plan = ScheduleCompiler.Compile(new PulseDocument { Settings = settings }, Rate, 1, false);
        Assert.AreEqual(8, plan.Onsets.Count, "what Core really plays in that bar");
        var grid = ClickGrid.Of(settings);
        Assert.AreEqual(3, grid.Beats);
        Assert.AreEqual(1, grid.Split);
        Assert.AreEqual(0, grid.SplitSlots);
        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, Labels(grid));

        // A unit the bar divides shows every click, and each of them sounds.
        settings.BeatUnitQuarters = 2;
        Assert.IsFalse(MeterPresets.UnitNeedsFix(settings));
        var fitting = ClickGrid.Of(settings);
        Assert.AreEqual(6, fitting.Count);
        Assert.AreEqual(fitting.Count, ScheduleCompiler.Compile(new PulseDocument { Settings = settings }, Rate, 1, false).Onsets.Count);
    }

    [TestMethod]
    public void ASwungClickIsNumberedWhereCorePlaysItNotWhereAStraightOneWouldBe()
    {
        // Half-note beats in 4/4 split in two with swing 0.75: Core plays the clicks at 0, 1.5, 2 and 3.5 quarter notes, so the
        // second and fourth are between written beats, not on beats 2 and 4.
        var settings = new PulseSettings { BeatUnitQuarters = 2, Subdivision = 2, Swing = 0.75, CountInBars = 0 };
        CollectionAssert.AreEqual(new[] { "1", "·", "3", "·" }, Labels(ClickGrid.Of(settings)));
        settings.Swing = 0.5;
        CollectionAssert.AreEqual(new[] { "1", "2", "3", "4" }, Labels(ClickGrid.Of(settings)), "straight, the clicks do fall on beats 2 and 4");
    }

    [TestMethod]
    [DataRow(80d, 4, 4, 2.0, 2, 0.75, "")]
    [DataRow(80d, 4, 4, 1.0, 2, 0.5, "")]
    [DataRow(80d, 4, 4, 1.0, 4, 2d / 3, "")]
    [DataRow(80d, 6, 8, 1.5, 3, 0.5, "3+3")]
    [DataRow(80d, 6, 8, 1.5, 4, 0.7, "3+3")]
    [DataRow(80d, 6, 8, 1.5, 2, 0.75, "3+3")]
    [DataRow(80d, 7, 8, 0.5, 2, 0.6, "2+2+3")]
    public void ANumberIsOnAWrittenBeatOfTheCompiledScheduleAndADotIsBetweenTwo(
        double tempo, int beats, int denominator, double unit, int subdivision, double swing, string groups)
    {
        var settings = new PulseSettings
        {
            Tempo = tempo, BeatsPerBar = beats, BeatDenominator = denominator, BeatUnitQuarters = unit,
            Subdivision = subdivision, Swing = swing, CountInBars = 0,
            Groups = groups.Length == 0 ? [] : groups.Split('+').Select(int.Parse).ToArray()
        };
        var grid = ClickGrid.Of(settings);
        var plan = ScheduleCompiler.Compile(new PulseDocument { Settings = settings }, Rate, 1, false);
        var onsets = plan.Onsets.OrderBy(onset => onset.Frame).ToArray();
        Assert.AreEqual(grid.Count, onsets.Length);
        var writtenBeatQuarters = 4d / denominator;
        for (var cell = 0; cell < onsets.Length; cell++)
        {
            var quarters = onsets[cell].Frame / (double)Rate * tempo * unit / 60d;
            var written = quarters / writtenBeatQuarters;
            var onWrittenBeat = Math.Abs(written - Math.Round(written)) < 1e-3;
            var expected = onWrittenBeat ? (Math.Round(written) + 1).ToString(CultureInfo.CurrentCulture) : "·";
            Assert.AreEqual(expected, grid.Label(cell),
                $"click {cell} of {beats}/{denominator} (split {subdivision}, swing {swing}) sounds {quarters:0.###} quarter notes into the bar");
        }
    }

    [TestMethod]
    public void TheStripIsLitOnlyByAudioThatPlacesItsClicksWhereTheCellsAre()
    {
        var shown = ClickGrid.Of(new PulseSettings { Subdivision = 2, Swing = 0.5 });
        Assert.IsTrue(shown.IsPlayedBy(new PulseSettings { Subdivision = 2, Swing = 0.5 }));
        Assert.IsTrue(shown.IsPlayedBy(new PulseSettings { Subdivision = 2, Swing = 0.5, Tempo = 200 }), "tempo does not move a click within the bar");
        Assert.IsFalse(shown.IsPlayedBy(new PulseSettings { Subdivision = 1 }), "a subdivision that has not reached the audio yet");
        Assert.IsFalse(shown.IsPlayedBy(new PulseSettings { Subdivision = 2, Swing = 0.7 }), "a swing that has not");
        Assert.IsFalse(shown.IsPlayedBy(new PulseSettings { Subdivision = 2, BeatUnitQuarters = 0.5 }), "a unit that has not");
        Assert.IsFalse(shown.IsPlayedBy(new PulseSettings { Subdivision = 2, BeatsPerBar = 3 }), "a meter that has not");
    }

    [TestMethod]
    [DataRow(4, 4, 1.0, 1, "")]
    [DataRow(4, 4, 0.5, 1, "")]
    [DataRow(4, 4, 0.5, 3, "")]
    [DataRow(4, 4, 2.0, 2, "")]
    [DataRow(4, 4, 1.5, 3, "")]
    [DataRow(6, 8, 1.5, 1, "3+3")]
    [DataRow(6, 8, 1.5, 2, "3+3")]
    [DataRow(6, 8, 1.5, 3, "3+3")]
    [DataRow(6, 8, 0.5, 2, "3+3")]
    [DataRow(7, 8, 0.5, 3, "2+2+3")]
    [DataRow(12, 8, 1.5, 1, "3+3+3+3")]
    [DataRow(12, 8, 1.5, 3, "3+3+3+3")]
    [DataRow(3, 4, 1.0, 4, "")]
    [DataRow(5, 4, 2.0, 2, "")]
    [DataRow(2, 2, 2.0, 2, "")]
    public void NoTwoBeatsShareANumberAndNoTwoClicksShareAName(int beats, int denominator, double unit, int subdivision, string groups)
    {
        var settings = new PulseSettings
        {
            BeatsPerBar = beats, BeatDenominator = denominator, BeatUnitQuarters = unit, Subdivision = subdivision,
            Groups = groups.Length == 0 ? [] : groups.Split('+').Select(int.Parse).ToArray()
        };
        var grid = ClickGrid.Of(settings);
        var beatNumbers = Enumerable.Range(0, grid.Beats).Select(beat => grid.Label(beat * grid.Split)).ToArray();
        var where = $"{beats}/{denominator} counted in {unit} quarters, split {subdivision}";
        CollectionAssert.AllItemsAreUnique(beatNumbers, where);
        Assert.IsFalse(beatNumbers.Contains("·"), $"a beat is never a dot: {where}");
        // A click is named by the number of the beat it belongs to and its place in it, as the accessible names read.
        var names = Enumerable.Range(0, grid.Count).Select(cell => (grid.Label(grid.BeatOf(cell) * grid.Split), grid.PartOf(cell))).ToArray();
        Assert.AreEqual(grid.Count, names.Distinct().Count(), $"every click has its own name: {where}");
    }

    [TestMethod]
    public void AStripIsTheSameBarOnlyWhenItsCellsMeanTheSameClicks()
    {
        var twoWay = ClickGrid.Of(new PulseSettings { Subdivision = 2 });
        Assert.IsTrue(twoWay.SameCells(ClickGrid.Of(new PulseSettings { Subdivision = 2, Tempo = 200 })), "tempo does not move a cell");
        Assert.IsFalse(twoWay.SameCells(ClickGrid.Of(new PulseSettings { Subdivision = 3 })), "cell 2 is beat 2, then the third click of beat 1");
        Assert.IsFalse(twoWay.SameCells(ClickGrid.Of(new PulseSettings { Subdivision = 2, BeatUnitQuarters = 0.5 })));
        Assert.IsFalse(twoWay.SameCells(ClickGrid.Of(new PulseSettings { Subdivision = 2, BeatsPerBar = 3 })));
    }

    [TestMethod]
    public void ACountInLightsTheBeatsThatSoundAndSkipsTheSilentClicksBetweenThem()
    {
        var compound = ClickGrid.Of(Compound());
        foreach (var (position, cell) in new[] { (0d, 0), (0.5, 0), (1.0, 0), (1.49, 0), (1.5, 3), (2.0, 3), (2.9, 3) })
            Assert.AreEqual(cell, compound.CellAt(position, countIn: true), $"{position} quarters into a count-in bar");
        Assert.AreEqual(1, compound.CellAt(0.5), "the body of the bar still lights its split clicks");
        var plain = ClickGrid.Of(new PulseSettings());
        Assert.AreEqual(2, plain.CellAt(2.5, countIn: true), "without a split a count-in lights the beat as always");
    }

    [TestMethod]
    public void NoSplitClickIsLitWhileTheScheduleCountsIn()
    {
        var settings = Compound();
        settings.CountInBars = 1;
        var grid = ClickGrid.Of(settings);
        var plan = ScheduleCompiler.Compile(new PulseDocument { Settings = settings }, Rate, 1, includeCountIn: true);
        Assert.IsTrue(plan.CountInFrames > 0, "the plan has a count-in");
        Assert.AreEqual(grid.Beats, plan.Onsets.Count(onset => onset.Frame < plan.CountInFrames), "a count-in plays the beats alone");

        var lit = new List<int>();
        for (var frame = 0L; frame < plan.CountInFrames; frame += 480)
        {
            var position = plan.PositionAtFrame(frame);
            Assert.IsTrue(position.IsCountIn, $"frame {frame}");
            var cell = grid.CellAt((position.BeatIndex + position.BeatPhase) * 4d / settings.BeatDenominator, position.IsCountIn);
            Assert.IsFalse(grid.IsSplit(cell), $"frame {frame} lit a click that does not sound");
            if (lit.Count == 0 || lit[^1] != cell) lit.Add(cell);
        }
        CollectionAssert.AreEqual(new[] { 0, 3 }, lit, "each beat of the count-in bar is lit in its turn");
    }

    [TestMethod]
    public void AdditiveGroupsSpaceTheirStartsAndTheStripNeverHasMoreThanTheMaximumOfCells()
    {
        var seven = new PulseSettings { BeatsPerBar = 7, BeatDenominator = 8, BeatUnitQuarters = 0.5, Groups = [2, 2, 3] };
        CollectionAssert.AreEquivalent(new[] { 2, 4 }, ClickGrid.Of(seven).SpaceBefore().ToArray());

        var limit = ClickGrid.MaximumCells / 4;
        Assert.AreEqual(ClickGrid.MaximumCells, ClickGrid.Of(new PulseSettings { Subdivision = limit }).Count);
        var tooMany = ClickGrid.Of(new PulseSettings { Subdivision = limit + 1 });
        Assert.AreEqual(4, tooMany.Count, "beats alone are shown when the clicks would not fit");
        Assert.AreEqual(1, tooMany.Split);
        Assert.AreEqual(0, tooMany.SplitSlots);
        Assert.IsFalse(tooMany.IsSplit(3));
    }

    [TestMethod]
    public void TheLitCellFollowsThePositionInTheBar()
    {
        var compound = ClickGrid.Of(Compound());
        foreach (var (position, cell) in new[] { (0d, 0), (0.4, 0), (0.5, 1), (0.99, 1), (1.0, 2), (1.49, 2), (1.5, 3), (2.0, 4), (2.9, 5) })
            Assert.AreEqual(cell, compound.CellAt(position), $"{position} quarters into the bar");

        var straight = ClickGrid.Of(new PulseSettings { Subdivision = 2 });
        Assert.AreEqual(1, straight.CellAt(0.5));
        var swung = ClickGrid.Of(new PulseSettings { Subdivision = 2, Swing = 2d / 3 });
        Assert.AreEqual(0, swung.CellAt(0.5), "a swung off-beat has not sounded yet");
        Assert.AreEqual(1, swung.CellAt(0.7));
        Assert.AreEqual(8, ClickGrid.Of(new PulseSettings { Subdivision = 2 }).CellAt(4.0), "past the bar: the caller lights nothing");
    }

    [TestMethod]
    [DataRow(80d, 6, 8, 1.5, 3, 0.5, "3+3")]
    [DataRow(100d, 4, 4, 1.0, 2, 0.5, "")]
    [DataRow(100d, 4, 4, 1.0, 2, 0.75, "")]
    [DataRow(150d, 7, 8, 0.5, 3, 0.5, "2+2+3")]
    [DataRow(90d, 3, 4, 1.0, 4, 0.5, "")]
    public void TheLitCellIsTheCellOfTheClickThatIsSoundingForEveryClickTheScheduleHas(
        double tempo, int beats, int denominator, double unit, int subdivision, double swing, string groups)
    {
        var settings = new PulseSettings
        {
            Tempo = tempo, BeatsPerBar = beats, BeatDenominator = denominator, BeatUnitQuarters = unit,
            Subdivision = subdivision, Swing = swing, CountInBars = 0,
            Groups = groups.Length == 0 ? [] : groups.Split('+').Select(int.Parse).ToArray()
        };
        var grid = ClickGrid.Of(settings);
        var plan = ScheduleCompiler.Compile(new PulseDocument { Settings = settings }, Rate, 1, false);
        var onsets = plan.Onsets.OrderBy(onset => onset.Frame).ToArray();
        Assert.AreEqual(grid.Count, onsets.Length);
        for (var index = 0; index < onsets.Length; index++)
        {
            // A few samples after the click, so rounding to a sample cannot put the playhead before it.
            var position = plan.PositionAtFrame(onsets[index].Frame + 3);
            var withinBar = (position.BeatIndex + position.BeatPhase) * 4d / denominator;
            Assert.AreEqual(index, grid.CellAt(withinBar), $"click {index} of {beats}/{denominator} (split {subdivision}, swing {swing})");
            Assert.AreEqual(onsets[index].LaneIndex == -2, grid.IsSplit(index), $"click {index} is a beat or a split click");
        }
    }
}
