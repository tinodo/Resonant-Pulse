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
