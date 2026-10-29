using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class StrengthEditTests
{
    private static PulseSettings FourFour(int subdivision = 1) => new()
    {
        BeatsPerBar = 4, BeatDenominator = 4, BeatUnitQuarters = 1, Subdivision = subdivision,
        Accents = [Accent.Strong, Accent.Normal, Accent.Normal, Accent.Normal], Groups = []
    };

    [TestMethod]
    public void ATapCyclesABeatThroughTheFourStrengthsAndMarksItAsSetByHand()
    {
        var settings = FourFour();
        var shown = ClickGrid.Of(settings);
        var seen = new List<Accent>();
        for (var tap = 0; tap < 4; tap++)
        {
            Assert.IsTrue(AccentPatterns.ChangeStrength(settings, shown, 1, 1, wrap: true));
            seen.Add(settings.Accents[1]);
        }
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Silent, Accent.Soft, Accent.Normal }, seen);
        CollectionAssert.AreEqual(new[] { 1 }, settings.ManualAccentIndices);
    }

    [TestMethod]
    public void AnArrowKeyStepsUpOrDownAndStopsAtTheEnds()
    {
        var settings = FourFour();
        var shown = ClickGrid.Of(settings);
        Assert.IsTrue(AccentPatterns.ChangeStrength(settings, shown, 0, 1, wrap: false));
        Assert.AreEqual(Accent.Strong, settings.Accents[0], "Strong is the top");
        for (var step = 0; step < 5; step++) AccentPatterns.ChangeStrength(settings, shown, 0, -1, wrap: false);
        Assert.AreEqual(Accent.Silent, settings.Accents[0], "Silent is the bottom");
    }

    [TestMethod]
    public void ATapOnASplitClickSetsThatClickAndNoOther()
    {
        var settings = FourFour(subdivision: 2);
        var shown = ClickGrid.Of(settings);
        Assert.IsTrue(AccentPatterns.ChangeStrength(settings, shown, 3, 1, wrap: true));
        CollectionAssert.AreEqual(new[] { Accent.Soft, Accent.Normal, Accent.Soft, Accent.Soft }, settings.SubdivisionAccents);
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Normal, Accent.Normal, Accent.Normal }, settings.Accents,
            "the beats are untouched");
        Assert.IsNull(settings.ManualAccentIndices);
    }

    [TestMethod]
    public void AShortPatternIsExpandedToTheBarSoTheTappedClickAloneChanges()
    {
        var settings = FourFour(subdivision: 2);
        settings.SubdivisionAccents = [Accent.Normal];
        Assert.IsTrue(AccentPatterns.ChangeStrength(settings, ClickGrid.Of(settings), 3, 1, wrap: true));
        CollectionAssert.AreEqual(new[] { Accent.Normal, Accent.Strong, Accent.Normal, Accent.Normal }, settings.SubdivisionAccents);
    }

    [TestMethod]
    public void APatternLongerThanTheBarKeepsItsLengthWhenOneVisibleClickChanges()
    {
        // 4/4 split in two shows four split clicks, but a program section in 8/4 reads all eight.
        var stored = new[]
        {
            Accent.Soft, Accent.Normal, Accent.Strong, Accent.Silent, Accent.Normal, Accent.Strong, Accent.Soft, Accent.Normal
        };
        var settings = FourFour(subdivision: 2);
        settings.SubdivisionAccents = (Accent[])stored.Clone();
        Assert.IsTrue(AccentPatterns.ChangeStrength(settings, ClickGrid.Of(settings), 1, 1, wrap: true));
        var expected = (Accent[])stored.Clone();
        expected[0] = Accent.Normal;
        CollectionAssert.AreEqual(expected, settings.SubdivisionAccents, "clicks 5 to 8 of the longer bar keep their strengths");
    }

    [TestMethod]
    public void ATapOnAStripTheBarHasOutgrownChangesNothing()
    {
        var settings = FourFour(subdivision: 2);
        var shown = ClickGrid.Of(settings);
        // The change of subdivision that was still being applied lands first: cell 2 is no longer beat 2 but the third click of beat 1.
        settings.Subdivision = 3;
        Assert.IsFalse(AccentPatterns.ChangeStrength(settings, shown, 2, 1, wrap: true));
        Assert.IsNull(settings.SubdivisionAccents);
        Assert.IsNull(settings.ManualAccentIndices);
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Normal, Accent.Normal, Accent.Normal }, settings.Accents);

        var beats = FourFour();
        var shownBeats = ClickGrid.Of(beats);
        beats.BeatUnitQuarters = 0.5;
        Assert.IsFalse(AccentPatterns.ChangeStrength(beats, shownBeats, 2, 1, wrap: true), "the bar now has eight beats");
        Assert.IsNull(beats.ManualAccentIndices);
    }

    [TestMethod]
    public void ATapOnAClickThatIsGoneChangesNothing()
    {
        var settings = FourFour();
        var shown = ClickGrid.Of(settings);
        foreach (var cell in new[] { -1, 4, 99 })
            Assert.IsFalse(AccentPatterns.ChangeStrength(settings, shown, cell, 1, wrap: true), $"cell {cell}");
        Assert.IsNull(settings.ManualAccentIndices);
    }
}
