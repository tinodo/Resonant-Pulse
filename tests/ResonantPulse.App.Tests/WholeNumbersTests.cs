using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class WholeNumbersTests
{
    [TestMethod]
    [DataRow(16.0, 16)]
    [DataRow(1.0, 1)]
    [DataRow(0.0, 0)]
    [DataRow(-3.0, -3)]
    [DataRow(1024.0, 1024)]
    public void WholeValuesAreAccepted(double value, int expected)
    {
        Assert.IsTrue(WholeNumbers.TryGet(value, out var whole));
        Assert.AreEqual(expected, whole);
    }

    // A NumberBox shows 1.9 as "2" but reports 1.9. Casting it would save 1 while the field says 2, so it is refused.
    [TestMethod]
    [DataRow(1.9)]
    [DataRow(1.5)]
    [DataRow(1.1)]
    [DataRow(16.0000001)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(3e9)]
    [DataRow(-3e9)]
    public void FractionsAndOutOfRangeValuesAreRefusedNotTruncated(double value)
    {
        Assert.IsFalse(WholeNumbers.TryGet(value, out var whole));
        Assert.AreEqual(0, whole);
    }
}
