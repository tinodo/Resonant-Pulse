using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class FormMetricsTests
{
    [TestMethod]
    public void DigitsCountTheLongestLimitAndAMinusSign()
    {
        Assert.AreEqual(1, FormMetrics.Digits(0, 9));
        Assert.AreEqual(3, FormMetrics.Digits(20, 960));
        Assert.AreEqual(5, FormMetrics.Digits(0, 86400));
        Assert.AreEqual(7, FormMetrics.Digits(0, 6_400_000));
        Assert.AreEqual(3, FormMetrics.Digits(-99, 99));
    }

    [TestMethod]
    public void ANumberBoxIsAsWideAsItsLargestValueAndNeverWiderThanThat()
    {
        var tempo = FormMetrics.NumberWidth(20, 960);
        var seconds = FormMetrics.NumberWidth(0, 86400);
        var beats = FormMetrics.NumberWidth(0, 6_400_000);
        Assert.IsTrue(tempo < seconds && seconds < beats, $"{tempo} < {seconds} < {beats}");
        // The owner's complaint: a box for "100" or "1000" was the width of the window. Even the largest limit stays small.
        Assert.IsTrue(beats <= 200, $"{beats}");
        Assert.IsTrue(tempo >= 140, $"{tempo}");
    }

    [TestMethod]
    public void ANumberBoxHasRoomForAtLeastThreeDigits()
    {
        Assert.AreEqual(FormMetrics.NumberWidth(0, 999), FormMetrics.NumberWidth(0, 9));
        Assert.AreEqual(FormMetrics.NumberWidth(0, 99), FormMetrics.NumberWidth(1, 1));
    }

    [TestMethod]
    public void FractionDigitsAndTextScaleWidenANumberBox()
    {
        Assert.IsTrue(FormMetrics.NumberWidth(0, 999, 2) > FormMetrics.NumberWidth(0, 999));
        Assert.IsTrue(FormMetrics.NumberWidth(0, 999, 0, 2) > FormMetrics.NumberWidth(0, 999));
        // A scale below 100% never shrinks the box below what the digits need at 100%.
        Assert.AreEqual(FormMetrics.NumberWidth(0, 999), FormMetrics.NumberWidth(0, 999, 0, 0.5));
    }

    [TestMethod]
    public void ADropDownFollowsItsLongestOptionWithinLimits()
    {
        Assert.AreEqual(FormMetrics.SelectMinWidth, FormMetrics.SelectWidth(10));
        Assert.AreEqual(FormMetrics.SelectMaxWidth, FormMetrics.SelectWidth(900));
        Assert.AreEqual(FormMetrics.SelectWidth(120) + 40, FormMetrics.SelectWidth(160));
        Assert.IsTrue(FormMetrics.SelectWidth(60) >= 60 + FormMetrics.SelectChrome - 1);
    }

    [TestMethod]
    public void PagesUseTwoColumnsOnlyWhenTheyAreWideEnough()
    {
        Assert.AreEqual(1, FormMetrics.PageColumns(520));
        Assert.AreEqual(1, FormMetrics.PageColumns(FormMetrics.TwoColumnBreakpoint - 1));
        Assert.AreEqual(2, FormMetrics.PageColumns(FormMetrics.TwoColumnBreakpoint));
        Assert.AreEqual(2, FormMetrics.PageColumns(3000));
    }

    [TestMethod]
    public void ASettingsControlMovesUnderItsTextInANarrowCard()
    {
        Assert.IsFalse(FormMetrics.SettingRowSideBySide(FormMetrics.StackedRowBreakpoint - 1));
        Assert.IsTrue(FormMetrics.SettingRowSideBySide(FormMetrics.StackedRowBreakpoint));
    }

    [TestMethod]
    public void WrapRowsKeepChildrenOnOneRowWhileTheyFit()
    {
        CollectionAssert.AreEqual(new[] { 0 }, WrapRows.Starts([100, 100, 100], 320, 10).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 2 }, WrapRows.Starts([100, 100, 100], 250, 10).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, WrapRows.Starts([100, 100, 100], 150, 10).ToArray());
    }

    [TestMethod]
    public void SpacingCountsTowardsTheRowWidth()
    {
        // 3 x 100 + 2 x 10 = 320 fits exactly; one DIP less does not.
        CollectionAssert.AreEqual(new[] { 0 }, WrapRows.Starts([100, 100, 100], 320, 10).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 2 }, WrapRows.Starts([100, 100, 100], 319, 10).ToArray());
    }

    [TestMethod]
    public void AChildWiderThanTheRowSitsAloneAndNeverLoopsForever()
    {
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, WrapRows.Starts([50, 400, 50], 200, 8).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, WrapRows.Starts([400, 50], 200, 8).ToArray());
    }

    [TestMethod]
    public void AnUnknownOrInfiniteWidthNeverWraps()
    {
        CollectionAssert.AreEqual(new[] { 0 }, WrapRows.Starts([100, 100, 100], double.PositiveInfinity, 10).ToArray());
        CollectionAssert.AreEqual(new[] { 0 }, WrapRows.Starts([100, 100, 100], 0, 10).ToArray());
    }

    [TestMethod]
    public void NoChildrenMeansNoRows()
    {
        Assert.AreEqual(0, WrapRows.Starts([], 300, 10).Count);
    }
}
