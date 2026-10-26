using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TextTests
{
    [TestMethod]
    public void TapInFeedbackIsPresentAndFormatsInEverySupportedLanguage()
    {
        var previous = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                foreach (var key in new[] { "TapReady", "TapEstimateUnknown", "TapEstimate", "TapInCount",
                    "ArmTapIn", "CancelTapIn", "TapInProgress", "TapInRejected", "TapInPreparing",
                    "TapInStopFirst", "TapInCancelled", "TapInRange", "TapInHint", "TapInMonitoringUnavailable",
                    "TapInOutputChanged" })
                    Assert.AreNotEqual(key, Text.Get(key), $"{language}: {key}");
                Assert.IsFalse(Text.Format("TapInProgress", 2, 4, "120").Contains('{'));
                Assert.IsFalse(Text.Format("TapInRejected", 0, 4, "120").Contains('{'));
                Assert.IsFalse(Text.Format("TapEstimate", 120d, 3, 1).Contains('{'));
            }
        }
        finally
        {
            Text.Select(previous);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [TestMethod]
    public void SelectingSystemRestoresInitialLanguageAfterExplicitSelections()
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var initial = Text.InitialSystemLanguage;
        var expected = initial is "nl" or "de" or "fr" or "es" ? initial : "en";
        try
        {
            foreach (var explicitLanguage in new[] { "fr", "de", "es", "nl", "en" })
            {
                Text.Select(explicitLanguage);
                Assert.AreEqual(explicitLanguage, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
                Text.Select("");
                Assert.AreEqual("", Text.Language);
                Assert.AreEqual(expected, Text.Culture.TwoLetterISOLanguageName);
                Assert.AreEqual(expected, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
                Assert.AreEqual(initial, Text.InitialSystemLanguage);
                var systemPlayLabel = Text.Get("Play");
                Text.Select(expected);
                Assert.AreEqual(systemPlayLabel, Text.Get("Play"));
            }
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
