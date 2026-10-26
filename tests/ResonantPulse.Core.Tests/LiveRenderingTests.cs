using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class LiveRenderingTests
{
    [TestMethod]
    public void AttackPartitionRetainsExactSamplesAndTailsAcrossLoopSeams()
    {
        var document = Check.Document();
        document.Settings.Subdivision = 8;
        document.Settings.Tempo = 1000;
        document.Lanes = Enumerable.Range(0, 8).Select(_ => new RhythmLane { Volume = 1, Pulses = 16 }).ToList();
        var plan = Check.Plan(document);
        foreach (long boundary in new[] { 0L, 1L, 31L, plan.TotalFrames - 1, plan.TotalFrames, plan.TotalFrames + 17 })
        {
            long start = Math.Max(0, boundary - 16);
            float[] original = new float[1024], before = new float[1024], after = new float[1024];
            PcmRenderer.Fill(original, plan, start, masterGain: 1);
            PcmRenderer.Fill(before, plan, start, masterGain: 1, attacksBeforeFrame: boundary, limitPeaks: false);
            PcmRenderer.Fill(after, plan, start, masterGain: 1, attacksFromFrame: boundary, limitPeaks: false);
            for (int index = 0; index < original.Length; index++)
                Assert.AreEqual(original[index], Math.Clamp(before[index] + after[index], -1f, 1f), 0.000001f,
                    $"Boundary {boundary}, sample {index}");
        }
    }

    [TestMethod]
    public void SoundChangeKeepsPriorAttackTailAndUsesNewSoundForNextAttack()
    {
        var document = Check.Document();
        var oldPlan = Check.Plan(document);
        document.Settings.Sound = "Glass";
        var newPlan = Check.Plan(document);
        float[] oldTail = new float[256], newTail = new float[256], baseline = new float[256];
        PcmRenderer.Fill(oldTail, oldPlan, 80, loop: false, attacksBeforeFrame: 80);
        PcmRenderer.Fill(newTail, newPlan, 80, loop: false, attacksFromFrame: 80);
        PcmRenderer.Fill(baseline, oldPlan, 80, loop: false);
        CollectionAssert.AreEqual(baseline, oldTail);
        Assert.IsTrue(newTail.All(value => value == 0));
        PcmRenderer.Fill(newTail, newPlan, 4000, loop: false, attacksFromFrame: 80);
        PcmRenderer.Fill(baseline, newPlan, 4000, loop: false);
        CollectionAssert.AreEqual(baseline, newTail);
        PcmRenderer.Fill(oldTail, oldPlan, 4000, loop: false);
        Assert.IsTrue(oldTail.Where((value, index) => value != newTail[index]).Any());
    }

    [TestMethod]
    public void ExplicitPulseChoiceOverridesGroupsIncludingSilentWithoutLosingGrouping()
    {
        var document = Check.Document();
        document.Settings.Groups = [2, 2];
        document.Settings.Accents = [Accent.Silent, Accent.Soft, Accent.Normal, Accent.Strong];
        Assert.AreEqual(Accent.Strong, Check.Plan(document).Onsets[0].Accent);
        document.Settings.AccentOverridesGroups = true;
        var plan = Check.Plan(document);
        Assert.IsFalse(plan.Onsets.Any(onset => onset.Frame == 0));
        Check.Sequence(new[] { Accent.Soft, Accent.Normal, Accent.Strong }, plan.Onsets.Select(onset => onset.Accent));
        Check.Sequence(new[] { 2, 2 }, document.Settings.Groups);
        document.Settings.Accents = [Accent.Normal];
        Assert.IsTrue(Check.Plan(document).Onsets.All(onset => onset.Accent == Accent.Normal));
    }

    [TestMethod]
    public void EditingOneDottedUnitPulsePreservesOtherGroupBoundariesAcrossBars()
    {
        var document = Check.Document();
        document.Settings.BeatUnitQuarters = 1.5;
        document.Settings.Groups = [2, 2];
        document.Settings.Accents = [Accent.Normal, Accent.Normal, Accent.Normal];
        var baseline = Check.Plan(document, bars: 3);
        document.Settings.ManualAccentIndices = [2];
        document.Settings.Accents[2] = Accent.Soft;
        var edited = Check.Plan(document, bars: 3);
        Assert.AreEqual(baseline.Onsets.Count, edited.Onsets.Count);
        for (int index = 0; index < baseline.Onsets.Count; index++)
        {
            var before = baseline.Onsets[index];
            var after = edited.Onsets[index];
            Assert.AreEqual(before.Frame, after.Frame);
            var withinBar = (index * 1.5) % 4;
            Assert.AreEqual((int)(withinBar / 1.5) == 2 ? Accent.Soft : before.Accent, after.Accent,
                $"Pulse {index} at {index * 1.5} quarters");
        }
        Assert.IsTrue(edited.Onsets.Count > 6, "The fixture must span multiple bars.");
    }

    [TestMethod]
    public void PerPulseOverrideCanSilenceGroupBoundaryWithoutChangingOtherBoundaries()
    {
        var document = Check.Document();
        document.Settings.Groups = [2, 2];
        document.Settings.Accents = [Accent.Silent, Accent.Normal, Accent.Normal, Accent.Normal];
        document.Settings.ManualAccentIndices = [0];
        var plan = Check.Plan(document);
        Assert.IsFalse(plan.Onsets.Any(onset => onset.Frame == 0));
        Assert.IsTrue(plan.Onsets.Any(onset => onset.Accent == Accent.Strong));
        document.Settings.CountInBars = 1;
        Assert.AreEqual(Accent.Strong, Check.Plan(document, countIn: true).Onsets[0].Accent);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(256)]
    public void InvalidManualAccentIndicesAreRejected(int index)
    {
        var document = Check.Document();
        document.Settings.ManualAccentIndices = [index];
        Assert.ThrowsExactly<ArgumentException>(() => DocumentValidator.Validate(document));
    }

    [TestMethod]
    public void DuplicateManualAccentIndicesAreRejected()
    {
        var document = Check.Document();
        document.Settings.ManualAccentIndices = [0, 0];
        Assert.ThrowsExactly<ArgumentException>(() => DocumentValidator.Validate(document));
    }
}
