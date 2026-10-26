using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ErrorTextTests
{
    [TestMethod]
    public void ActualTapInRangeFailuresKeepSpecificRecoveryGuidanceInAllLanguages()
    {
        var previous = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                foreach (var tempo in new[] { 0d, 1000d })
                {
                    var document = new PulseDocument { Program = [new() { StartTempo = 100, EndTempo = 200 }] };
                    var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                        TempoEdits.ApplyTapIn(document, tempo));
                    Assert.AreEqual(Text.Get("TapInRange"), ErrorText.Explain(exception));
                    Assert.AreNotEqual(Text.Get("ErrorRhythm"), ErrorText.Explain(exception));
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
        var keys = new[] { "TimingPendingEdits", "TimingTargetChanged", "LibraryPartial", "LibraryFailedPaths",
            "LibraryLimited", "LibraryOverflow", "StageBusy", "EditorBusy", "EditorChanged", "ErrorWavePaths" };
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
                StringAssert.Contains(Text.Format("LibraryOverflow", 1000), "1000");
                Assert.AreNotEqual(Text.Get("Locked"), Text.Get("StageBusy"));
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

    [TestMethod]
    [DataRow(WaveExportFailure.RollbackFailed, "ErrorWaveRollback", 2)]
    [DataRow(WaveExportFailure.CommittedCleanupFailed, "ErrorWaveCommittedCleanup", 4)]
    [DataRow(WaveExportFailure.FailedCleanup, "ErrorWaveFailedCleanup", 4)]
    public void TypedWaveOutcomesPreserveAllPathsAndSpecificGuidanceInEveryLanguage(
        WaveExportFailure failure, string key, int candidateCount)
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var translations = new HashSet<string>();
        var wavePath = Path.Combine("exports", "concert {1} é.wav");
        var manifestPath = wavePath + ".manifest.json";
        var candidates = Enumerable.Range(0, candidateCount)
            .Select(index => Path.Combine("exports", $"original-or-staged-{index}.backup")).ToArray();
        try
        {
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                // Even a Message equal to the generic translated I/O text
                // cannot override the machine-readable typed outcome.
                var exception = new WaveExportException(failure, wavePath, manifestPath, candidates,
                    Text.Get("ErrorFile"), new IOException("Untranslated technical cause"));
                var explanation = ErrorText.Explain(exception);
                var expected = Text.Get(key) + Environment.NewLine + Environment.NewLine +
                    Text.Format("ErrorWavePaths", wavePath, manifestPath, string.Join(Environment.NewLine, candidates));
                Assert.AreEqual(expected, explanation);
                Assert.AreNotEqual(key, Text.Get(key));
                Assert.IsFalse(string.IsNullOrWhiteSpace(explanation));
                Assert.AreNotEqual(ErrorText.Explain(new IOException("I/O")), explanation);
                StringAssert.Contains(explanation, wavePath);
                StringAssert.Contains(explanation, manifestPath);
                foreach (var candidate in candidates) StringAssert.Contains(explanation, candidate);
                Assert.IsFalse(explanation.Contains("Untranslated technical cause", StringComparison.Ordinal));
                Assert.AreEqual(explanation, ErrorText.Explain(new AggregateException(exception)));
                var outcomes = new[] { "ErrorWaveRollback", "ErrorWaveCommittedCleanup", "ErrorWaveFailedCleanup" };
                Assert.AreEqual(3, outcomes.Select(Text.Get).Distinct().Count());
                translations.Add(Text.Get(key));
            }
            Assert.AreEqual(5, translations.Count);
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
