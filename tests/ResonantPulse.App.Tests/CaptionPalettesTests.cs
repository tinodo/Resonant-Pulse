using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class CaptionPalettesTests
{
    [TestMethod]
    [DataRow(false, false, nameof(CaptionPalette.LightOnDark))]
    [DataRow(false, true, nameof(CaptionPalette.DarkOnLight))]
    [DataRow(true, false, nameof(CaptionPalette.System))]
    [DataRow(true, true, nameof(CaptionPalette.System))]
    public void ContrastThemesKeepTheSystemCaptionColorsAndOtherwiseTheThemeDecides(bool highContrast, bool lightTheme,
        string expected) =>
        Assert.AreEqual(expected, CaptionPalettes.Choose(highContrast, lightTheme).ToString());
}
