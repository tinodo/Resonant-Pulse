using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class TempoGuideTests
{
    [TestMethod]
    [DataRow(1d, "TempoLarghissimo")]
    [DataRow(24.5d, "TempoLarghissimo")]
    [DataRow(30d, "TempoGrave")]
    [DataRow(50d, "TempoLargo")]
    [DataRow(63d, "TempoLarghetto")]
    [DataRow(70d, "TempoAdagio")]
    [DataRow(90d, "TempoAndante")]
    [DataRow(110d, "TempoModerato")]
    [DataRow(140d, "TempoAllegro")]
    [DataRow(160d, "TempoVivace")]
    [DataRow(180d, "TempoPresto")]
    [DataRow(250d, "TempoPrestissimo")]
    [DataRow(1000d, "TempoPrestissimo")]
    public void InteriorTempoReturnsItsApproximateMarking(double bpm, string expected)
    {
        CollectionAssert.AreEqual(new[] { expected }, TempoGuide.MarkingsAt(bpm).ToArray());
    }

    [TestMethod]
    public void SharedBandsAreNotPresentedAsOneAuthoritativeClassification()
    {
        CollectionAssert.AreEqual(new[] { "TempoGrave", "TempoLargo" }, TempoGuide.MarkingsAt(42).ToArray());
        CollectionAssert.AreEqual(new[] { "TempoModerato", "TempoAllegro" }, TempoGuide.MarkingsAt(120).ToArray());
        CollectionAssert.AreEqual(new[] { "TempoVivace", "TempoPresto" }, TempoGuide.MarkingsAt(170).ToArray());
    }

    [TestMethod]
    public void FractionalTempoSweepHasNoUnlabelledGaps()
    {
        for (var step = 0; step <= 9990; step++)
        {
            var bpm = 1 + step / 10d;
            Assert.IsTrue(TempoGuide.MarkingsAt(bpm).Count > 0, $"No guide for {bpm} BPM.");
        }
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(1000.1d)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void InvalidTempoIsRejectedRatherThanAssignedAMusicalMarking(double bpm)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TempoGuide.MarkingsAt(bpm));
    }
}
