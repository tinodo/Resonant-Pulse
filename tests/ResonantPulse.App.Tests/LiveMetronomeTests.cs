using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LiveMetronomeTests
{
    [TestMethod]
    public void TheBeatThatSoundsIsLitAndNothingIsLitWhileNothingPlays()
    {
        Assert.AreEqual(2, LiveMetronome.LitBeat(plays: true, silentGap: false, beatIndex: 2, beats: 4));
        Assert.AreEqual(0, LiveMetronome.LitBeat(true, false, 0, 4));
        Assert.AreEqual(-1, LiveMetronome.LitBeat(plays: false, silentGap: false, beatIndex: 2, beats: 4), "stopped or finished");
        Assert.AreEqual(-1, LiveMetronome.LitBeat(plays: true, silentGap: true, beatIndex: 2, beats: 4), "a silent gap makes no sound");
        Assert.AreEqual(-1, LiveMetronome.LitBeat(true, false, 0, 0), "a meter without beats");
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
