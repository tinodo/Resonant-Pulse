using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class PresetSummaryTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    private static string MetaOf(string id) =>
        PresetSummary.Meta(BuiltInLibrary.Presets().Concat(BuiltInLibrary.PracticePrograms()).Single(preset => preset.Id == id).Document, English);

    [TestMethod]
    public void AMeterAndTempoReadAsOneShortLine()
    {
        Assert.AreEqual("4/4 · 120 BPM", MetaOf("open"));
        Assert.AreEqual("6/8 · 80 BPM", MetaOf("compound-6-8"));
        Assert.AreEqual("7/8 · 180 BPM", MetaOf("additive-7-8"));
    }

    [TestMethod]
    public void AProgramShowsItsTempoRange()
    {
        Assert.AreEqual("4/4 · 60–120 BPM", MetaOf("ramp-domains"));
        Assert.AreEqual("4/4 · 80–112 BPM", MetaOf("warmup-ladder"));
    }

    [TestMethod]
    public void AProgramThatStaysAtOneTempoShowsThatTempo()
    {
        Assert.AreEqual("4/4 · 96 BPM", MetaOf("internal-time"));
    }

    [TestMethod]
    public void EveryBuiltInRhythmHasASummary()
    {
        foreach (var preset in BuiltInLibrary.Presets().Concat(BuiltInLibrary.PracticePrograms()))
        {
            var meta = PresetSummary.Meta(preset.Document, English);
            StringAssert.Matches(meta, new System.Text.RegularExpressions.Regex(@"^\d+/\d+ · [\d.,]+(–[\d.,]+)? BPM$"), preset.Id);
        }
    }

    [TestMethod]
    public void TheTempoUsesTheLanguagesDecimalSeparator()
    {
        var document = new PulseDocument { Settings = new() { Tempo = 92.5 } };
        Assert.AreEqual("4/4 · 92,5 BPM", PresetSummary.Meta(document, CultureInfo.GetCultureInfo("nl")));
        Assert.AreEqual("4/4 · 92.5 BPM", PresetSummary.Meta(document, English));
    }
}
