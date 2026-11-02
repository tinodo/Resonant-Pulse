using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LiveMetronomeTests
{
    [TestMethod]
    public void TheBeatThatSoundsIsLitAndNothingIsLitWhileNothingPlaysOrTheCuesAreHidden()
    {
        Assert.AreEqual(2, LiveMetronome.LitBeat(plays: true, cuesHidden: false, beatIndex: 2, beats: 4));
        Assert.AreEqual(0, LiveMetronome.LitBeat(true, false, 0, 4));
        Assert.AreEqual(-1, LiveMetronome.LitBeat(plays: false, cuesHidden: false, beatIndex: 2, beats: 4), "stopped or finished");
        Assert.AreEqual(-1, LiveMetronome.LitBeat(plays: true, cuesHidden: true, beatIndex: 2, beats: 4),
            "a silent bar of a project that hides its phase cues in gaps shows no beat, as the dial does");
        Assert.AreEqual(-1, LiveMetronome.LitBeat(true, false, 0, 0), "a meter without beats");
    }

    [TestMethod]
    public void ALongBarGetsSmallLightsSoThePinnedStripTakesAFewRowsAtMost()
    {
        // The strip is pinned above the page, so however many beats a bar has (up to 64) its lights must not take the page. The worst case
        // is a bar in which every beat begins an additive group, in the narrowest window, which leaves about 280 DIP for the lights.
        for (var beats = 1; beats <= 64; beats++)
        {
            var emphasized = LiveMetronome.Emphasized(beats, Enumerable.Repeat(1, beats).ToArray());
            var height = LightMetrics.For(beats).Height(beats, emphasized, 280);
            Assert.IsTrue(height <= 64, $"{beats} beats take {height} DIP of the page");
        }
        Assert.AreEqual(18d, LightMetrics.For(12).Size, "a bar of 12/8 is still drawn in full size");
        Assert.AreEqual(26d, LightMetrics.For(4).EmphasizedSize);
        Assert.AreEqual(10d, LightMetrics.For(LightMetrics.CompactFrom).Size);
        Assert.AreEqual(LightMetrics.For(64).Size, LightMetrics.For(64).EmphasizedSize, "where lights are small the first of a group is told by its ring");
        // One row in a wide window, and the height of one light when everything fits.
        Assert.AreEqual(18d, LightMetrics.For(4).Height(4, LiveMetronome.Emphasized(4, []).Where(index => index != 0).ToHashSet(), 900));
        Assert.AreEqual(26d, LightMetrics.For(4).Height(4, LiveMetronome.Emphasized(4, []), 900));
        Assert.AreEqual(0d, LightMetrics.For(0).Height(0, new HashSet<int>(), 280));
    }

    [TestMethod]
    public void ABeatOutsideTheBarIsKeptInsideIt()
    {
        Assert.AreEqual(3, LiveMetronome.LitBeat(true, false, 9, 4));
        Assert.AreEqual(0, LiveMetronome.LitBeat(true, false, -3, 4));
    }

    [TestMethod]
    public void TheBeatsThatBeginTheBarOrAGroupAreEmphasized()
    {
        CollectionAssert.AreEquivalent(new[] { 0 }, LiveMetronome.Emphasized(4, []).ToArray());
        CollectionAssert.AreEquivalent(new[] { 0 }, LiveMetronome.Emphasized(1, []).ToArray());
        CollectionAssert.AreEquivalent(new[] { 0, 2 }, LiveMetronome.Emphasized(4, [2, 2]).ToArray());
        CollectionAssert.AreEquivalent(new[] { 0, 3, 5 }, LiveMetronome.Emphasized(7, [3, 2, 2]).ToArray());
        // Groups that run past the bar never emphasize a beat the bar does not have.
        CollectionAssert.AreEquivalent(new[] { 0, 2 }, LiveMetronome.Emphasized(4, [2, 2, 2]).ToArray());
    }

    [TestMethod]
    public void TheLiveMetronomeTextsAreTranslatedAndFillEveryPlace()
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            Text.Select("en");
            var english = new Dictionary<string, string>
            {
                ["LiveMetronome"] = Text.Get("LiveMetronome"),
                ["LiveBeatOf"] = LiveMetronome.Describe("TEMPO", 4, 1),
                ["LiveBeats"] = LiveMetronome.Describe("TEMPO", 4, -1),
                ["LiveStep"] = Text.Format("LiveStep", 2, 5, "NAME")
            };
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                var localized = new Dictionary<string, string>
                {
                    ["LiveMetronome"] = Text.Get("LiveMetronome"),
                    ["LiveBeatOf"] = LiveMetronome.Describe("TEMPO", 4, 1),
                    ["LiveBeats"] = LiveMetronome.Describe("TEMPO", 4, -1),
                    ["LiveStep"] = Text.Format("LiveStep", 2, 5, "NAME")
                };
                foreach (var (key, value) in localized)
                {
                    Assert.AreNotEqual(key, value, $"{language}/{key} is missing");
                    Assert.IsFalse(value.Contains('{') || value.Contains('}'), $"{language}/{key} has an unfilled place: {value}");
                    if (language != "en") Assert.AreNotEqual(english[key], value, $"{language}/{key} is untranslated");
                }
                StringAssert.Contains(localized["LiveBeatOf"], "TEMPO", language);
                // The beat that is lit, counted from 1 (the second of four), and the number of beats.
                StringAssert.Contains(localized["LiveBeatOf"], "2", language);
                StringAssert.Contains(localized["LiveBeatOf"], "4", language);
                StringAssert.Contains(localized["LiveBeats"], "TEMPO", language);
                StringAssert.Contains(localized["LiveBeats"], "4", language);
                StringAssert.Contains(localized["LiveStep"], "NAME", language);
                StringAssert.Contains(localized["LiveStep"], "5", language);
            }
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
