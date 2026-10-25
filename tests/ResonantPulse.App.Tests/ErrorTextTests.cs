using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ErrorTextTests
{
    [TestMethod]
    public void MidiConstraintsWinOverGenericConstantAndSwingKeywordsInEveryLanguage()
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var rampTranslations = new HashSet<string>();
        var ppqTranslations = new HashSet<string>();
        try
        {
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                var ramp = ErrorText.Explain(new ArgumentException(
                    "MIDI export preflight failed: Section 'Practice' has a continuous ramp. " +
                    "Export WAV/project or author explicit constant-tempo steps."));
                var ppq = ErrorText.Explain(new ArgumentException(
                    "MIDI export preflight failed: Exact onset coordinates require PPQ above 32767. " +
                    "Use exact rational authored lane events for ratios such as 2:1 swing."));
                Assert.AreEqual(Text.Get("ErrorMidiRamp"), ramp);
                Assert.AreNotEqual(Text.Get("ErrorRamp"), ramp);
                Assert.AreEqual(Text.Get("ErrorMidiPpq"), ppq);
                Assert.AreNotEqual(Text.Get("ErrorSwing"), ppq);
                Assert.AreNotEqual("ErrorMidiRamp", ramp);
                Assert.AreNotEqual("ErrorMidiPpq", ppq);
                rampTranslations.Add(ramp);
                ppqTranslations.Add(ppq);
            }
            Assert.AreEqual(5, rampTranslations.Count);
            Assert.AreEqual(5, ppqTranslations.Count);
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [TestMethod]
    public void NonMidiAuthoringErrorsKeepTheirSpecificGuidance()
    {
        Assert.AreEqual(Text.Get("ErrorRamp"),
            ErrorText.Explain(new ArgumentException("Constant steps require equal tempos.")));
        Assert.AreEqual(Text.Get("ErrorSwing"),
            ErrorText.Explain(new ArgumentException("Swing requires paired even subdivisions.")));
        Assert.AreEqual(Text.Get("ErrorMidi"),
            ErrorText.Explain(new ArgumentException("MIDI duration exceeds the supported tick range.")));
    }

    [TestMethod]
    public void TimingGuardsAndPartialLibraryWarningsAreLocalizedInAllFiveLanguages()
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var keys = new[] { "TimingPendingEdits", "TimingTargetChanged", "LibraryPartial", "LibraryFailedPaths" };
        var translations = keys.ToDictionary(key => key, _ => new HashSet<string>());
        try
        {
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                foreach (var key in keys)
                {
                    var translated = Text.Get(key);
                    Assert.AreNotEqual(key, translated);
                    translations[key].Add(translated);
                }
                var paths = Path.Combine("Library", "corrupt.pulse");
                StringAssert.Contains(Text.Format("LibraryFailedPaths", 1, paths), paths);
                Assert.AreEqual(Text.Get("TimingPendingEdits"),
                    ErrorText.Explain(new InvalidOperationException(Text.Get("TimingPendingEdits"))));
            }
            foreach (var key in keys) Assert.AreEqual(5, translations[key].Count);
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
