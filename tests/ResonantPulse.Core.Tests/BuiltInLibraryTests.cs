using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class BuiltInLibraryTests
{
    [TestMethod]
    public void Catalogs_IdsAndMetadata_AreStableNonemptyAndUnique()
    {
        var all = BuiltInLibrary.Presets().Concat(BuiltInLibrary.PracticePrograms()).ToArray();
        string[] required = ["open", "compound-6-8", "additive-7-8", "swing", "polyrhythm-3-4",
            "polymeter-3-4", "euclidean-5-8", "paradiddle", "warmup-ladder", "internal-time", "ramp-domains", "seeded-density"];
        foreach (var id in required) Assert.IsTrue(all.Any(p => p.Id == id), $"Missing stable ID {id}");
        Assert.AreEqual(all.Length, all.Select(p => p.Id).Distinct().Count());
        Assert.IsTrue(all.All(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Description)));
        var record = new LibraryPreset("custom", "Study", "Description", Check.Document());
        Assert.AreEqual("custom", record.Id); Assert.AreEqual("Study", record.Name);
        Assert.AreEqual("Description", record.Description); Assert.AreEqual(1, record.Document.SchemaVersion);
    }

    [TestMethod]
    public void Catalogs_RepeatedCalls_ReturnFreshMutableGraphs()
    {
        var first = BuiltInLibrary.Presets(); var second = BuiltInLibrary.Presets();
        foreach (var preset in first)
        {
            var other = second.Single(p => p.Id == preset.Id);
            Assert.AreNotSame(preset.Document, other.Document);
            Assert.AreNotSame(preset.Document.Settings, other.Document.Settings);
            preset.Document.Settings.Accents[0] = (Accent)99;
            preset.Document.Settings.Tempo = 999;
            preset.Document.Setlist.Add(new() { Notes = "changed" });
            foreach (var lane in preset.Document.Lanes)
            {
                lane.Pan = 1;
                if (lane.Events.Count > 0) lane.Events[0].Gain = 0;
            }
            Assert.AreNotEqual(999d, other.Document.Settings.Tempo);
            Assert.IsFalse(other.Document.Settings.Accents.Contains((Accent)99));
            Assert.AreEqual(0, other.Document.Setlist.Count);
            Assert.IsTrue(other.Document.Lanes.All(l => l.Pan != 1 && l.Events.All(e => e.Gain == 1)));
        }
        var programs = BuiltInLibrary.PracticePrograms();
        programs[0].Document.Program[0].StartTempo = 999;
        Assert.AreEqual(80d, BuiltInLibrary.PracticePrograms().Single(p => p.Id == "warmup-ladder").Document.Program[0].StartTempo);
    }

    [TestMethod]
    public void Catalogs_EachDocument_ValidatesAndCompilesFinitePlan()
    {
        foreach (var p in BuiltInLibrary.Presets().Concat(BuiltInLibrary.PracticePrograms()))
        {
            DocumentValidator.Validate(p.Document);
            var plan = ScheduleCompiler.Compile(p.Document, 8000, bars: 1);
            Assert.IsTrue(plan.TotalFrames > plan.CountInFrames, p.Id);
            Assert.AreEqual(plan.TotalFrames, plan.SectionBoundaries[^1].EndFrame);
            Assert.AreEqual(8000, plan.SampleRate);
        }
        var compound = BuiltInLibrary.Presets().Single(p => p.Id == "compound-6-8").Document;
        Assert.AreEqual(1.5, compound.Settings.BeatUnitQuarters); Check.Sequence(new[] { 3, 3 }, compound.Settings.Groups);
    }
}
