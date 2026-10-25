using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class TempoEditsTests
{
    [TestMethod]
    public void ChangedTapProducesOneNumberBoxValueChange()
    {
        var value = 120d;
        var valueChanges = 0;
        foreach (var estimate in new double?[] { 132, 132, 132 })
        {
            if (!TempoEdits.TryApplyTap(value, estimate, out var proposed)) continue;
            value = proposed;
            valueChanges++;
        }
        Assert.AreEqual(132d, value);
        Assert.AreEqual(1, valueChanges);
        Assert.IsTrue(TempoEdits.IsChange(120, 132));
        Assert.IsFalse(TempoEdits.IsChange(132, 132));
    }

    [TestMethod]
    public void UnchangedAndClampedEstimatesDoNotRequestAnotherCommit()
    {
        Assert.IsFalse(TempoEdits.TryApplyTap(120, 120, out _));
        Assert.IsFalse(TempoEdits.TryApplyTap(1000, 1400, out _));
        Assert.IsFalse(TempoEdits.TryApplyTap(1, 0.5, out _));
        Assert.IsTrue(TempoEdits.TryApplyTap(120, 1400, out var maximum));
        Assert.AreEqual(1000d, maximum);
    }

    [TestMethod]
    public void MissingAndNonfiniteEstimatesCannotEditTempo()
    {
        foreach (var estimate in new double?[] { null, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.IsFalse(TempoEdits.TryApplyTap(120, estimate, out _));
        Assert.IsFalse(TempoEdits.IsChange(120, double.NaN));
    }
}
