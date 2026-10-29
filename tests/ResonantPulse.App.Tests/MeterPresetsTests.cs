using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class MeterPresetsTests
{
    private const int Rate = 48000;

    private static PulseDocument DocumentFor(MeterPreset preset, double tempo = 120)
    {
        var document = new PulseDocument { Settings = { Tempo = tempo, CountInBars = 0 } };
        MeterPresets.Apply(document.Settings, preset.Beats, preset.Denominator, preset.Groups);
        return document;
    }

    [TestMethod]
    public void EveryQuickPickIsValidAndGroupsAddUpToTheBeats()
    {
        Assert.AreEqual(12, MeterPresets.All.Count);
        foreach (var preset in MeterPresets.All)
        {
            var document = DocumentFor(preset);
            DocumentValidator.Validate(document);
            if (preset.Groups.Length > 0) Assert.AreEqual(preset.Beats, preset.Groups.Sum(), $"{preset.Beats}/{preset.Denominator}");
        }
        CollectionAssert.AreEqual(new[] { "2/4", "3/4", "4/4", "6/8" },
            MeterPresets.All.Take(4).Select(p => $"{p.Beats}/{p.Denominator}").ToArray());
    }

    [TestMethod]
    public void EveryWrittenBeatIsOneAudibleClickAndTheBarLastsBeatsTimesTheBeatInterval()
    {
        foreach (var preset in MeterPresets.All)
        {
            var document = DocumentFor(preset);
            Assert.AreEqual(preset.Beats, MeterPresets.PulsesPerBar(document.Settings), $"{preset.Beats}/{preset.Denominator}");
            var plan = ScheduleCompiler.Compile(document, Rate, 2, false);
            var barFrames = (long)Math.Round(preset.Beats * 60.0 / 120 * Rate);
            var firstBar = plan.Onsets.Where(onset => onset.Frame < barFrames).ToArray();
            Assert.AreEqual(preset.Beats, firstBar.Length, $"clicks in one {preset.Beats}/{preset.Denominator} bar");
            Assert.AreEqual(barFrames * 2, plan.TotalFrames, $"two bars of {preset.Beats}/{preset.Denominator}");
            Assert.AreEqual(Accent.Strong, firstBar[0].Accent);
            var starts = new HashSet<int> { 0 };
            var offset = 0;
            foreach (var group in preset.Groups) { starts.Add(offset); offset += group; }
            for (var index = 0; index < firstBar.Length; index++)
                Assert.AreEqual(starts.Contains(index) ? Accent.Strong : Accent.Normal, firstBar[index].Accent,
                    $"{preset.Beats}/{preset.Denominator} beat {index + 1}");
        }
    }

    [TestMethod]
    public void EightEightAddsEightClicksWhereTheOldQuarterUnitGaveFour()
    {
        var legacy = new PulseDocument { Settings = { BeatsPerBar = 8, BeatDenominator = 8, CountInBars = 0 } };
        Assert.AreEqual(4, MeterPresets.PulsesPerBar(legacy.Settings));
        MeterPresets.Apply(legacy.Settings, 8, 8);
        Assert.AreEqual(8, MeterPresets.PulsesPerBar(legacy.Settings));
        Assert.AreEqual(0.5, legacy.Settings.BeatUnitQuarters);
        Assert.AreEqual(8, legacy.Settings.Accents.Length);
    }

    [TestMethod]
    public void ApplyRestartsTheAccentPatternAndClearsManualEdits()
    {
        var settings = new PulseSettings
        {
            Accents = [Accent.Silent, Accent.Strong, Accent.Soft, Accent.Normal], AccentOverridesGroups = true,
            ManualAccentIndices = [1, 2], Groups = [2, 2]
        };
        MeterPresets.Apply(settings, 3, 4);
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Normal, Accent.Normal }, settings.Accents);
        Assert.IsFalse(settings.AccentOverridesGroups);
        Assert.IsNull(settings.ManualAccentIndices);
        Assert.AreEqual(0, settings.Groups.Length);
        Assert.AreEqual(1d, settings.BeatUnitQuarters);
    }

    [TestMethod]
    public void ApplyAlsoEndsTheStrengthsOfTheClicksThatSplitABeatAndLeavesTheSplitToTheCaller()
    {
        var settings = new PulseSettings { Subdivision = 2, SubdivisionAccents = [Accent.Strong, Accent.Silent] };
        MeterPresets.Apply(settings, 3, 4);
        Assert.IsNull(settings.SubdivisionAccents);
        Assert.AreEqual(2, settings.Subdivision);
    }

    [TestMethod]
    public void PickingATimeSignatureAfterACompoundRhythmKeepsTheClicksWhereTheyWere()
    {
        var document = new PulseDocument
        {
            Settings = new()
            {
                Tempo = 80, BeatsPerBar = 6, BeatDenominator = 8, BeatUnitQuarters = 1.5, Subdivision = 3, CountInBars = 0,
                Accents = [Accent.Strong, Accent.Normal], Groups = [3, 3], SubdivisionAccents = [Accent.Normal]
            }
        };
        var before = ScheduleCompiler.Compile(document, Rate, 2, false).Onsets.Select(onset => onset.Frame).ToArray();
        Assert.AreEqual(12, before.Length);

        MeterPresets.ApplyOneClickPerBeat(document.Settings, 6, 8, [3, 3]);

        var settings = document.Settings;
        Assert.AreEqual(1, settings.Subdivision);
        Assert.AreEqual(0.5, settings.Swing);
        Assert.AreEqual(240d, settings.Tempo, "the old split raises the tempo, so the clicks keep coming at the same rate");
        Assert.IsNull(settings.SubdivisionAccents);
        Assert.AreEqual(6, MeterPresets.PulsesPerBar(settings));
        DocumentValidator.Validate(document);
        CollectionAssert.AreEqual(before, ScheduleCompiler.Compile(document, Rate, 2, false).Onsets.Select(onset => onset.Frame).ToArray());
    }

    [TestMethod]
    public void ASplitIsKeptWhenTheClickUnitStaysAndEndedWhenItChanges()
    {
        var kept = new PulseSettings { Tempo = 100, Subdivision = 2, Swing = 2d / 3, SubdivisionAccents = [Accent.Strong] };
        MeterPresets.ApplyOneClickPerBeat(kept, 3, 4);
        Assert.AreEqual(2, kept.Subdivision);
        Assert.AreEqual(2d / 3, kept.Swing);
        Assert.AreEqual(100d, kept.Tempo);
        Assert.IsNull(kept.SubdivisionAccents, "the strengths belonged to the old bar");

        // The split clicks of 4/4 in two were an eighth note apart, which is 6/8's beat: the same clicks keep coming.
        var sameClicks = new PulseSettings { Tempo = 77.3, Subdivision = 2, Swing = 2d / 3 };
        MeterPresets.ApplyOneClickPerBeat(sameClicks, 6, 8);
        Assert.AreEqual(154.6, sameClicks.Tempo);
        Assert.AreEqual(1, sameClicks.Subdivision);
        Assert.AreEqual(0.5, sameClicks.Swing, "straight again: a swing needs a pair to swing");

        var capped = new PulseSettings { Tempo = 600, Subdivision = 2 };
        MeterPresets.ApplyOneClickPerBeat(capped, 6, 8);
        Assert.AreEqual(1000d, capped.Tempo);

        // Triplets are not 6/8's beat, so the BPM number is kept, as choosing a meter always kept it.
        var different = new PulseSettings { Tempo = 100, Subdivision = 3 };
        MeterPresets.ApplyOneClickPerBeat(different, 6, 8);
        Assert.AreEqual(100d, different.Tempo);
        Assert.AreEqual(1, different.Subdivision);

        var unsplit = new PulseSettings { Tempo = 77.3 };
        MeterPresets.ApplyOneClickPerBeat(unsplit, 6, 8);
        Assert.AreEqual(77.3, unsplit.Tempo, "nothing was split, so the tempo is the click rate already");
    }

    [TestMethod]
    public void ACompoundRhythmChosenAsFourFourKeepsItsBpmNumberRatherThanTheClickRate()
    {
        var settings = new PulseSettings
        {
            Tempo = 80, BeatsPerBar = 6, BeatDenominator = 8, BeatUnitQuarters = 1.5, Subdivision = 3, Groups = [3, 3]
        };
        MeterPresets.ApplyOneClickPerBeat(settings, 4, 4);
        Assert.AreEqual(80d, settings.Tempo, "4/4 at 240 BPM would be the same click rate and nothing anybody asked for");
        Assert.AreEqual(1, settings.Subdivision);
        Assert.AreEqual(1d, settings.BeatUnitQuarters);
    }

    [TestMethod]
    public void IsAppliedOnlyWhenChoosingThePresetWouldChangeNothing()
    {
        var four = MeterPresets.All.Single(p => p is { Beats: 4, Denominator: 4 });
        var settings = new PulseSettings();
        Assert.IsTrue(MeterPresets.IsApplied(settings, four));
        settings.BeatUnitQuarters = 0.5;
        Assert.IsTrue(MeterPresets.IsCurrent(settings, four));
        Assert.IsFalse(MeterPresets.IsApplied(settings, four));
        settings.BeatUnitQuarters = 1;
        settings.Groups = [2, 2];
        Assert.IsFalse(MeterPresets.IsApplied(settings, four));
    }

    [TestMethod]
    public void PulsesPerBarFollowsTheTempoUnitNotTheDenominator()
    {
        var settings = new PulseSettings { BeatsPerBar = 6, BeatDenominator = 8, BeatUnitQuarters = 1.5 };
        Assert.AreEqual(2, MeterPresets.PulsesPerBar(settings));
        settings.BeatUnitQuarters = 1;
        Assert.AreEqual(3, MeterPresets.PulsesPerBar(settings));
        settings.BeatsPerBar = 64; settings.BeatDenominator = 64; settings.BeatUnitQuarters = 1d / 16;
        Assert.AreEqual(64, MeterPresets.PulsesPerBar(settings));
    }

    [TestMethod]
    [DataRow("", 7, true, "")]
    [DataRow("2+2+3", 7, true, "2+2+3")]
    [DataRow(" 3 + 3 ", 6, true, "3+3")]
    [DataRow("2+2+2", 7, false, "")]
    [DataRow("2++3", 5, false, "")]
    [DataRow("0+4", 4, false, "")]
    [DataRow("-1+5", 4, false, "")]
    [DataRow("a+b", 4, false, "")]
    [DataRow("65", 65, false, "")]
    public void GroupingTextIsParsedAndValidated(string text, int beats, bool valid, string expected)
    {
        Assert.AreEqual(valid, MeterPresets.TryParseGroups(text, beats, out var groups));
        Assert.AreEqual(expected, MeterPresets.GroupsText(groups));
    }

    [TestMethod]
    [DataRow(4, 4, 1d, true)]
    [DataRow(4, 4, 0.25, true)]
    [DataRow(4, 4, 1.5, false)]
    [DataRow(4, 4, 3d, false)]
    [DataRow(6, 8, 1.5, true)]
    [DataRow(6, 8, 0.5, true)]
    [DataRow(6, 8, 2d, false)]
    [DataRow(3, 8, 1d, false)]
    [DataRow(3, 8, 0.5, true)]
    [DataRow(7, 8, 0.5, true)]
    [DataRow(7, 8, 1d, false)]
    [DataRow(2, 2, 2d, true)]
    public void OnlyUnitsThatFitTheBarKeepClicksAndAccentsInStep(int beats, int denominator, double unit, bool fits)
    {
        Assert.AreEqual(fits, MeterPresets.UnitFitsBar(beats, denominator, unit));
    }

    [TestMethod]
    public void EveryQuickPickFitsItsOwnOneClickPerBeatUnit()
    {
        foreach (var preset in MeterPresets.All)
            Assert.IsTrue(MeterPresets.UnitFitsBar(preset.Beats, preset.Denominator, 4d / preset.Denominator),
                $"{preset.Beats}/{preset.Denominator}");
    }

    [TestMethod]
    public void OnlyAUnitThatDoesNotDivideTheBarNeedsAFix()
    {
        // The saved state behind "the clicks do not match the circle": 8/8 counted in dotted halves (3 quarters).
        Assert.IsTrue(MeterPresets.UnitNeedsFix(new PulseSettings { BeatsPerBar = 8, BeatDenominator = 8, BeatUnitQuarters = 3 }));
        Assert.IsTrue(MeterPresets.UnitNeedsFix(new PulseSettings { BeatsPerBar = 3, BeatDenominator = 4, BeatUnitQuarters = 2 }));
        Assert.IsFalse(MeterPresets.UnitNeedsFix(new PulseSettings { BeatsPerBar = 6, BeatDenominator = 8, BeatUnitQuarters = 1.5 }));
        Assert.IsFalse(MeterPresets.UnitNeedsFix(new PulseSettings()));
        foreach (var preset in MeterPresets.All)
        {
            var settings = new PulseSettings();
            MeterPresets.Apply(settings, preset.Beats, preset.Denominator, preset.Groups);
            Assert.IsFalse(MeterPresets.UnitNeedsFix(settings), $"{preset.Beats}/{preset.Denominator}");
        }
    }

    [TestMethod]
    public void TheFixRealignsClicksWithTheBarAndKeepsTheClickRate()
    {
        const double Tempo = 75;
        var document = new PulseDocument
        {
            Settings = { BeatsPerBar = 8, BeatDenominator = 8, BeatUnitQuarters = 3, Tempo = Tempo, CountInBars = 0 }
        };
        var settings = document.Settings;
        var beforeFix = ScheduleCompiler.Compile(document, Rate, 4, false).Onsets;
        var clickGap = beforeFix[1].Frame - beforeFix[0].Frame;
        Assert.AreEqual((long)(60.0 / Tempo * Rate), clickGap, "one BPM unit per click");

        // What the banner's button does: apply the current written meter again.
        MeterPresets.Apply(settings, settings.BeatsPerBar, settings.BeatDenominator, settings.Groups);

        Assert.IsFalse(MeterPresets.UnitNeedsFix(settings));
        Assert.AreEqual(Tempo, settings.Tempo, "the BPM number is kept");
        Assert.AreEqual(8, MeterPresets.PulsesPerBar(settings));
        var plan = ScheduleCompiler.Compile(document, Rate, 2, false);
        var barFrames = (long)Math.Round(8 * 60.0 / Tempo * Rate);
        var firstBar = plan.Onsets.Where(onset => onset.Frame < barFrames).ToArray();
        Assert.AreEqual(8, firstBar.Length, "eight clicks fill the bar");
        Assert.AreEqual(Accent.Strong, firstBar[0].Accent);
        Assert.AreEqual(clickGap, plan.Onsets[1].Frame - plan.Onsets[0].Frame, "the audible click rate does not change");
        Assert.AreEqual(barFrames * 2, plan.TotalFrames);
    }

    [TestMethod]
    public void TempoReadsAsAWholeNumberUnlessItIsGenuinelyFractional()
    {
        var english = CultureInfo.GetCultureInfo("en");
        var dutch = CultureInfo.GetCultureInfo("nl");
        Assert.AreEqual("120", TempoFormat.Format(120, english));
        Assert.AreEqual("120", TempoFormat.Format(120.04, english));
        Assert.AreEqual("120", TempoFormat.Format(119.96, english));
        Assert.AreEqual("77.3", TempoFormat.Format(77.3, english));
        Assert.AreEqual("77,3", TempoFormat.Format(77.3, dutch));
        Assert.AreEqual("1", TempoFormat.Format(1, english));
        Assert.AreEqual("1000", TempoFormat.Format(1000, english));
    }
}
