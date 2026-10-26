using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TextTests
{
    [TestMethod]
    public void TimingCsvGuidanceAndFrozenSourceFormatInEverySupportedLanguage()
    {
        var previous = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                foreach (var key in new[] { "ExportTimingCsv", "TimingCsvHint", "TimingCsvReady", "TimingCsvUnavailable" })
                {
                    Assert.AreNotEqual(key, Text.Get(key), $"{language}: {key}");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(Text.Get(key)), $"{language}: {key}");
                }
                var source = Text.Format("TimingCsvReady", "Project", "Lane", "12:34");
                StringAssert.Contains(source, "Project");
                StringAssert.Contains(source, "Lane");
                StringAssert.Contains(source, "12:34");
                Assert.IsFalse(source.Contains('{'));
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
    public void StemExportGuidanceAndSizeConfirmationExistInEverySupportedLanguage()
    {
        var previous = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                foreach (var key in new[] { "ExportStems", "StemExportHint", "StemExportConfirm", "StemExportBusy" })
                {
                    Assert.AreNotEqual(key, Text.Get(key), $"{language}: {key}");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(Text.Get(key)), $"{language}: {key}");
                }
                var confirmation = Text.Format("StemExportConfirm", 3, 12.5);
                StringAssert.Contains(confirmation, 12.5.ToString("N1", Text.Culture));
                Assert.IsFalse(confirmation.Contains('{'));
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
    public void QuickStartAndMusicalReferenceExistInEverySupportedLanguage()
    {
        var previous = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                foreach (var key in new[] { "HelpDestination", "Help", "QuickStart", "MusicalReference", "HelpIntro",
                    "HelpOutput", "HelpStarting", "HelpTransport", "HelpEditing", "HelpPractice",
                    "HelpBpmUnit", "HelpWrittenBeats", "HelpSubdivision", "HelpAccents", "HelpTapping",
                    "HelpKeyboardScope", "KeyboardHelp", "PresentationLimits" })
                {
                    Assert.AreNotEqual(key, Text.Get(key), $"{language}: {key}");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(Text.Get(key)), $"{language}: {key}");
                }
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
    public void TempoGuideAndInvalidEntryGuidanceFormatInEverySupportedLanguage()
    {
        var previous = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                foreach (var key in new[] { "TempoGuideValue", "TempoGuideInput", "TempoGuideHint" })
                    Assert.AreNotEqual(key, Text.Get(key), $"{language}: {key}");
                foreach (var bpm in new[] { 1d, 30, 50, 63, 70, 90, 110, 140, 160, 180, 250 })
                    foreach (var key in TempoGuide.MarkingsAt(bpm))
                        Assert.AreNotEqual(key, Text.Get(key), $"{language}: {key}");
                var formatted = TempoGuide.Format(42);
                StringAssert.Contains(formatted, "Grave / Largo");
                Assert.IsFalse(formatted.Contains('{'));
                foreach (var bpm in new[] { double.NaN, double.PositiveInfinity, 0, 1001 })
                    Assert.AreEqual(Text.Get("TempoGuideInput"), TempoGuide.Format(bpm));
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
