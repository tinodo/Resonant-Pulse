using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

/// <summary>
/// The pane of the top navigation clips the name of the Settings item when the item of the selected page and the item of Settings do not both
/// fit: at 320 DIP wide "Instellingen", "Einstellungen" and "Paramètres" were cut. The item shows its gear alone where its name does not fit.
/// The widths below are the widths of the names as measured on the real window (Segoe UI Variable Text at 14 DIP), the window's width less its frame.
/// </summary>
[TestClass]
public sealed class NavigationFitTests
{
    // Pane widths of a 320 and a 400 DIP window (the client area is 16 DIP narrower than the window).
    private const double Narrowest = 304;
    private const double Narrow = 384;

    // (widest page name, name of Settings) of each language: Library and its neighbours, then Settings.
    private static readonly (string Language, double Pages, double Settings)[] Names =
    [
        ("English", 41.8, 49.7), ("Dutch", 68.9, 71.2), ("German", 61.6, 82.5), ("French", 78.1, 68.7)
    ];

    [TestMethod]
    public void AtTheSmallestWindowOnlyEnglishKeepsTheNameOfSettings()
    {
        Assert.IsTrue(NavigationFit.SettingsNameFits(Narrowest, Names[0].Pages, Names[0].Settings), "English fits with a few DIP to spare.");
        foreach (var (language, pages, settings) in Names.Skip(1))
            Assert.IsFalse(NavigationFit.SettingsNameFits(Narrowest, pages, settings), $"{language} was clipped at 320 DIP wide.");
    }

    [TestMethod]
    public void At400DipWideEveryLanguageKeepsTheNameOfSettings()
    {
        foreach (var (language, pages, settings) in Names)
            Assert.IsTrue(NavigationFit.SettingsNameFits(Narrow, pages, settings), $"{language} has room at 400 DIP wide.");
    }

    [TestMethod]
    public void AWiderWindowNeverTakesTheNameAwayAgain()
    {
        foreach (var (language, pages, settings) in Names)
        {
            var fitted = false;
            for (var width = 280.0; width <= 700; width += 1)
            {
                var fits = NavigationFit.SettingsNameFits(width, pages, settings);
                Assert.IsTrue(fits || !fitted, $"{language}: the name came back and went again at {width} DIP.");
                fitted |= fits;
            }
            Assert.IsTrue(fitted, $"{language} never fits.");
        }
    }

    [TestMethod]
    public void ALongerNameNeedsAWiderPane()
    {
        Assert.IsTrue(NavigationFit.SettingsNameFits(384, 61.6, 82.5));
        Assert.IsFalse(NavigationFit.SettingsNameFits(384, 61.6, 120), "A longer name (a larger Windows text size) does not fit where the shorter one did.");
        Assert.IsFalse(NavigationFit.SettingsNameFits(384, 140, 82.5));
    }
}
