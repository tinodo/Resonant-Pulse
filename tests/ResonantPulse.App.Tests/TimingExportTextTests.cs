using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Tests;

/// <summary>
/// The words around the export of a finished timing check, in every language: what the command is called, what the hint under it
/// says (and the conditions that what it says depends on), the line that names what would be saved, and the sentence that says why
/// a drift is missing. The CSV file's own column names are English by design and are held by the Core tests.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TimingExportTextTests
{
    private static readonly string[] Languages = ["en", "nl", "de", "fr", "es"];

    private static readonly string[] Keys = ["ExportTimingCsv", "TimingCsvHint", "TimingCsvReady", "TimingCsvUnavailable", "TimingDriftUnavailable"];

    private static string Get(string language, string key)
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            Text.Select(language);
            return Text.Get(key);
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [TestMethod]
    public void EverySentenceIsInEveryLanguageAndTheReadyLineHasAPlaceForEachOfItsThreeNames()
    {
        foreach (var language in Languages)
        {
            foreach (var key in Keys) Assert.IsFalse(string.IsNullOrWhiteSpace(Get(language, key)), $"{language}/{key}");
            var template = Get(language, "TimingCsvReady");
            foreach (var slot in new[] { "{0}", "{1}", "{2}" }) StringAssert.Contains(template, slot, language);
            // Project, rhythm, time: in that order, whatever the language does with the words between them.
            Assert.IsTrue(template.IndexOf("{0}", StringComparison.Ordinal) < template.IndexOf("{1}", StringComparison.Ordinal), language);
            Assert.IsTrue(template.IndexOf("{1}", StringComparison.Ordinal) < template.IndexOf("{2}", StringComparison.Ordinal), language);
        }
    }

    [TestMethod]
    public void TheHintStatesWhatItsClaimsDependOn()
    {
        // The result can be saved until another check starts or the app closes; the file is for a spreadsheet; its column names and
        // numbers do not depend on the language. Each claim is a sentence a person acts on, so its condition is in the words.
        var spreadsheet = new Dictionary<string, string>
        {
            ["en"] = "spreadsheet", ["nl"] = "spreadsheet", ["de"] = "Tabellenkalkulation", ["fr"] = "tableur", ["es"] = "hoja de cálculo"
        };
        var untilAnotherCheck = new Dictionary<string, string>
        {
            ["en"] = "start another check", ["nl"] = "een nieuwe check start", ["de"] = "eine neue Prüfung starten",
            ["fr"] = "lanciez un nouveau contrôle", ["es"] = "inicie otra comprobación"
        };
        var orCloseTheApp = new Dictionary<string, string>
        {
            ["en"] = "close the app", ["nl"] = "de app sluit", ["de"] = "die App schließen", ["fr"] = "fermiez l’application", ["es"] = "cierre la aplicación"
        };
        var sameInEveryLanguage = new Dictionary<string, string>
        {
            ["en"] = "same in every language", ["nl"] = "in elke taal hetzelfde", ["de"] = "in jeder Sprache gleich",
            ["fr"] = "les mêmes dans toutes les langues", ["es"] = "iguales en todos los idiomas"
        };
        var lastFinished = new Dictionary<string, string>
        {
            ["en"] = "last finished check", ["nl"] = "laatst afgeronde check", ["de"] = "zuletzt beendete Prüfung",
            ["fr"] = "dernier contrôle terminé", ["es"] = "última comprobación terminada"
        };
        foreach (var language in Languages)
        {
            var hint = Get(language, "TimingCsvHint");
            StringAssert.Contains(hint, "CSV", language);
            StringAssert.Contains(hint, spreadsheet[language], language);
            StringAssert.Contains(hint, lastFinished[language], language);
            StringAssert.Contains(hint, untilAnotherCheck[language], language);
            StringAssert.Contains(hint, orCloseTheApp[language], language);
            StringAssert.Contains(hint, sameInEveryLanguage[language], language);
        }
    }

    [TestMethod]
    public void TheNewTextCallsACheckWhatTheCommandThatStartsOneCallsIt()
    {
        var check = new Dictionary<string, string>
        {
            ["en"] = "check", ["nl"] = "check", ["de"] = "Prüfung", ["fr"] = "contrôle", ["es"] = "comprobación"
        };
        foreach (var language in Languages)
        {
            StringAssert.Contains(Get(language, "BeginTiming"), check[language], StringComparison.OrdinalIgnoreCase, language);
            StringAssert.Contains(Get(language, "TimingCsvHint"), check[language], StringComparison.OrdinalIgnoreCase, language);
            StringAssert.Contains(Get(language, "TimingCsvUnavailable"), check[language], StringComparison.OrdinalIgnoreCase, language);
        }
    }

    [TestMethod]
    public void TheDriftSentenceStatesBothConditionsOfAnEstimate()
    {
        var four = new Dictionary<string, string>
            { ["en"] = "four", ["nl"] = "vier", ["de"] = "vier", ["fr"] = "quatre", ["es"] = "cuatro" };
        var differentTimes = new Dictionary<string, string>
        {
            ["en"] = "different times", ["nl"] = "verschillende momenten", ["de"] = "unterschiedlichen Zeitpunkten",
            ["fr"] = "moments différents", ["es"] = "momentos distintos"
        };
        foreach (var language in Languages)
        {
            var sentence = Get(language, "TimingDriftUnavailable");
            StringAssert.Contains(sentence, four[language], language);
            StringAssert.Contains(sentence, differentTimes[language], language);
        }
    }

    [TestMethod]
    public void NoWordOfTheEngineOrTheFileFormatReachesThePerson()
    {
        // What the hint explains is a CSV file for a spreadsheet, not how it is encoded.
        string[] words =
        [
            "frozen", "snapshot", "input-arrival", "input arrival", "immutable", "invariant", "schema", "UTF", "BOM", "CRLF", "JSON",
            "onset", "armed", "deterministic", "flush", "frame", "Momentaufnahme", "onveranderlijk", "instantané", "inmutable"
        ];
        foreach (var language in Languages)
        foreach (var key in Keys)
        foreach (var word in words)
            Assert.IsFalse(Get(language, key).Contains(word, StringComparison.OrdinalIgnoreCase), $"{language}/{key} uses “{word}”");
    }

    [TestMethod]
    public void TheTextIsShortEnoughForTheRoomItHas()
    {
        foreach (var language in Languages)
        {
            // A command in a row of four: its label cannot be long. The line and the hint sit in a card 300 DIP wide at 400x720.
            Assert.IsTrue(Get(language, "ExportTimingCsv").Length <= 28, $"{language}/ExportTimingCsv: {Get(language, "ExportTimingCsv")}");
            Assert.IsTrue(Get(language, "TimingCsvReady").Length <= 60, $"{language}/TimingCsvReady: {Get(language, "TimingCsvReady").Length}");
            Assert.IsTrue(Get(language, "TimingCsvHint").Length <= 320, $"{language}/TimingCsvHint: {Get(language, "TimingCsvHint").Length}");
            Assert.IsTrue(Get(language, "TimingCsvUnavailable").Length <= 100, $"{language}/TimingCsvUnavailable: {Get(language, "TimingCsvUnavailable").Length}");
            Assert.IsTrue(Get(language, "TimingDriftUnavailable").Length <= 110, $"{language}/TimingDriftUnavailable: {Get(language, "TimingDriftUnavailable").Length}");
        }
        Assert.IsTrue(Get("en", "TimingCsvHint").Length <= 260, $"en/TimingCsvHint: {Get("en", "TimingCsvHint").Length}");
    }
}
