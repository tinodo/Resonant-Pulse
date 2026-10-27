using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class PracticeFormatTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl");

    [TestMethod]
    public void ZeroMeansNoLimitInEveryField()
    {
        Assert.AreEqual("off", PracticeFormat.Time(0, "off", English));
        Assert.AreEqual("uit", PracticeFormat.Count(0, "uit", Dutch));
    }

    [TestMethod]
    public void ATimeReadsAsSecondsMinutesOrHours()
    {
        Assert.AreEqual("45 s", PracticeFormat.Time(45, "off", English));
        Assert.AreEqual("1:00", PracticeFormat.Time(60, "off", English));
        Assert.AreEqual("5:05", PracticeFormat.Time(305, "off", English));
        Assert.AreEqual("59:59", PracticeFormat.Time(3599, "off", English));
        Assert.AreEqual("1:00:00", PracticeFormat.Time(3600, "off", English));
        Assert.AreEqual("24:00:00", PracticeFormat.Time(86400, "off", English));
    }

    [TestMethod]
    public void ACountUsesTheLanguagesDigitGrouping()
    {
        Assert.AreEqual("64", PracticeFormat.Count(64, "off", English));
        Assert.AreEqual("6,400,000", PracticeFormat.Count(6_400_000, "off", English));
        Assert.AreEqual("6.400.000", PracticeFormat.Count(6_400_000, "uit", Dutch));
    }
}
