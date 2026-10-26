using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LiveSoundEditsTests
{
    [TestMethod]
    public void ApplyCopiesOnlyLiveSoundFieldsAndRetainsActiveClockAndPendingStructuralEdits()
    {
        var active = new PulseDocument { Lanes = [new RhythmLane()], Program = [new ProgramStep()] };
        var requested = new PulseSettings
        {
            Sound = "Glass", Accents = [Accent.Silent, Accent.Soft], AccentOverridesGroups = true,
            ManualAccentIndices = [0],
            Tempo = 600, BeatsPerBar = 7, PracticeSeconds = 99, MasterGain = 0
        };
        var copy = LiveSoundEdits.Apply(active, requested);
        Assert.AreEqual("Glass", copy.Settings.Sound);
        Assert.AreEqual(Accent.Silent, copy.Settings.Accents[0]);
        Assert.IsTrue(copy.Settings.AccentOverridesGroups);
        Assert.AreEqual(active.Settings.Tempo, copy.Settings.Tempo);
        Assert.AreEqual(active.Settings.BeatsPerBar, copy.Settings.BeatsPerBar);
        Assert.AreEqual(active.Settings.PracticeSeconds, copy.Settings.PracticeSeconds);
        Assert.AreEqual(active.Settings.MasterGain, copy.Settings.MasterGain);
        copy.Settings.Accents[0] = Accent.Strong;
        copy.Settings.ManualAccentIndices![0] = 1;
        copy.Lanes.Clear();
        copy.Program[0].Bars = 1;
        Assert.AreEqual(Accent.Silent, requested.Accents[0]);
        Assert.AreEqual(0, requested.ManualAccentIndices![0]);
        Assert.AreEqual(1, active.Lanes.Count);
        Assert.AreEqual(8, active.Program[0].Bars);
    }

    [TestMethod]
    public void EffectiveGroupsAreDisplayedAndRepeatedPulseEditsCycleAllFourChoices()
    {
        var settings = new PulseSettings { Groups = [2, 2], Accents = [Accent.Normal] };
        var displayed = AccentPatterns.ExpandEffectiveMain(settings, 4);
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Normal, Accent.Strong, Accent.Normal }, displayed);
        for (int index = 0; index < 12; index++)
        {
            var current = AccentPatterns.ExpandEffectiveMain(settings, 4)[0];
            var pattern = AccentPatterns.ExpandMain(settings.Accents, 4);
            pattern[0] = (Accent)(((int)current + 1) % 4);
            settings.Accents = pattern;
            settings.ManualAccentIndices = [0];
            Assert.AreEqual((Accent)(index % 4), AccentPatterns.ExpandEffectiveMain(settings, 4)[0]);
            Assert.AreEqual(Accent.Strong, AccentPatterns.ExpandEffectiveMain(settings, 4)[2]);
        }
    }

    [TestMethod]
    public void PlayOptionsAndLiveGuidanceExistInEveryLanguage()
    {
        var original = Text.Language;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var uiCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            foreach (string language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                foreach (string key in new[] { "RhythmTab", "TempoTab", "PracticeTab", "ProjectTab",
                    "HelpTab", "CloseOptions", "ScrollControlsHint", "LiveSoundHint", "PulseNumber", "SoundSessionChanged",
                    "AudioOptions", "AudioOptionsHelp", "PulseStrengths" })
                    Assert.AreNotEqual(key, Text.Get(key), $"{language}/{key}");
            }
        }
        finally
        {
            Text.Select(original);
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [TestMethod]
    public void MaximumPulseDisplayPreservesEveryGroupAndOnlyTheManuallyEditedSlot()
    {
        var settings = new PulseSettings
        {
            BeatsPerBar = 64, BeatDenominator = 2, BeatUnitQuarters = 0.5,
            Groups = Enumerable.Repeat(1, 64).ToArray(), Accents = [Accent.Normal]
        };
        var original = AccentPatterns.ExpandEffectiveMain(settings, 256);
        for (int index = 0; index < original.Length; index++)
            Assert.AreEqual(index % 4 == 0 ? Accent.Strong : Accent.Normal, original[index]);
        settings.Accents = [Accent.Soft, Accent.Normal, Accent.Normal, Accent.Normal];
        settings.ManualAccentIndices = [0];
        var edited = AccentPatterns.ExpandEffectiveMain(settings, 256);
        Assert.AreEqual(Accent.Soft, edited[0]);
        for (int index = 1; index < edited.Length; index++) Assert.AreEqual(original[index], edited[index]);
    }
}
