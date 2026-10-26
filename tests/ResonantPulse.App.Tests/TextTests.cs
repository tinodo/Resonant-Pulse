using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TextTests
{
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

    [TestMethod]
    public void TheMuteButtonNeverMentionsAClickBecauseTheSoundCanChange()
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                var mute = Text.Get("Mute");
                var unmute = Text.Get("Unmute");
                Assert.AreNotEqual(mute, unmute, language);
                foreach (var label in new[] { mute, unmute })
                foreach (var word in new[] { "click", "klik", "klick", "clic", "tik" })
                    Assert.IsFalse(label.Contains(word, StringComparison.OrdinalIgnoreCase), $"{language}: {label}");
            }
            Text.Select("en");
            Assert.AreEqual("Mute", Text.Get("Mute"));
            Assert.AreEqual("Unmute", Text.Get("Unmute"));
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [TestMethod]
    public void TheUnitMismatchNoticeIsTranslatedAndFillsEveryPlaceholder()
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            Text.Select("en");
            var english = new Dictionary<string, string>
            {
                ["UnitMismatchTitle"] = Text.Get("UnitMismatchTitle"),
                ["UnitMismatchMessage"] = Text.Format("UnitMismatchMessage", "UNIT", 8, 8),
                ["UnitMismatchFix"] = Text.Format("UnitMismatchFix", "UNIT"),
                ["PlayTools"] = Text.Get("PlayTools")
            };
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                var localized = new Dictionary<string, string>
                {
                    ["UnitMismatchTitle"] = Text.Get("UnitMismatchTitle"),
                    ["UnitMismatchMessage"] = Text.Format("UnitMismatchMessage", "UNIT", 8, 8),
                    ["UnitMismatchFix"] = Text.Format("UnitMismatchFix", "UNIT"),
                    ["PlayTools"] = Text.Get("PlayTools")
                };
                foreach (var (key, value) in localized)
                {
                    Assert.AreNotEqual(key, value, $"{language}/{key} is missing");
                    Assert.IsFalse(value.Contains('{') || value.Contains('}'), $"{language}/{key} has an unfilled placeholder: {value}");
                    if (language != "en") Assert.AreNotEqual(english[key], value, $"{language}/{key} is untranslated");
                }
                StringAssert.Contains(localized["UnitMismatchMessage"], "UNIT", language);
                StringAssert.Contains(localized["UnitMismatchMessage"], "8/8", language);
                StringAssert.Contains(localized["UnitMismatchFix"], "UNIT", language);
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
