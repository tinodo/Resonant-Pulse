using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Tests;

/// <summary>
/// The words around the export of separate tracks, in every language: what the command is called, what the hint under it says, the
/// question that is asked before a file is chosen (with the conditions that what it says depends on), and the sentences for a track
/// that is too long and for a file that could not be saved. The files in the archive have fixed English names by design and are held
/// by the Core tests.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class StemExportTextTests
{
    private static readonly string[] Languages = ["en", "nl", "de", "fr", "es"];

    private static readonly string[] Keys = ["ExportStems", "StemExportHint", "StemExportConfirm", "StemExportSaving", "StemExportSlow", "StemExportNotSaved", "StemExportLeftover", "StemExportTooLong"];

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

    private static string ConfirmIn(string language, StemExport.Summary summary)
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            Text.Select(language);
            return StemExport.ConfirmText(summary);
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [TestMethod]
    public void EverySentenceIsInEveryLanguageAndTheQuestionHasAPlaceForEachOfItsThreeNumbers()
    {
        foreach (var language in Languages)
        {
            foreach (var key in Keys)
            {
                var text = Get(language, key);
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{language}/{key}");
                Assert.AreNotEqual(key, text, $"{language}/{key} is not defined");
            }
            var question = Get(language, "StemExportConfirm");
            foreach (var slot in new[] { "{0}", "{1}", "{2}" }) StringAssert.Contains(question, slot, language);
            // Tracks, length, size: in that order, whatever the language does with the words between them.
            Assert.IsTrue(question.IndexOf("{0}", StringComparison.Ordinal) < question.IndexOf("{1}", StringComparison.Ordinal), language);
            Assert.IsTrue(question.IndexOf("{1}", StringComparison.Ordinal) < question.IndexOf("{2}", StringComparison.Ordinal), language);
            StringAssert.Contains(Get(language, "StemExportTooLong"), "{0}", language);
            // The path of the temporary file that stays comes last, with nothing after it that could be read as part of the path.
            var leftover = Get(language, "StemExportLeftover");
            Assert.IsTrue(leftover.EndsWith("{0}", StringComparison.Ordinal), $"{language}: the path is the last thing in the sentence: '{leftover}'");
            Assert.AreEqual(leftover.IndexOf("{0}", StringComparison.Ordinal), leftover.LastIndexOf("{0}", StringComparison.Ordinal), language);
        }
    }

    [TestMethod]
    public void TheHintStatesWhatThePersonGetsAndWhatLinesTheFilesUp()
    {
        // A ZIP with a file for the main beat and one for each lane, and that they start at the same moment (which is what lets them be
        // lined up). The conditions that only matter once a project is chosen (the count-in, a lane that does not sound) are in the
        // question that is asked before anything is saved, where they cost the page no room.
        var parts = new Dictionary<string, string[]>
        {
            ["en"] = ["ZIP", "WAV file", "main beat", "each lane", "same moment"],
            ["nl"] = ["ZIP", "WAV-bestand", "hoofdpuls", "elke laag", "hetzelfde moment"],
            ["de"] = ["ZIP", "WAV-Datei", "Hauptpuls", "jede Spur", "selben Moment"],
            ["fr"] = ["ZIP", "fichier WAV", "pulsation principale", "chaque piste", "même instant"],
            ["es"] = ["ZIP", "archivo WAV", "pulso principal", "cada pista", "mismo momento"]
        };
        foreach (var language in Languages)
        {
            var hint = Get(language, "StemExportHint");
            foreach (var part in parts[language]) StringAssert.Contains(hint, part, language);
        }
    }

    [TestMethod]
    public void TheQuestionSaysWhatWillBeSavedWhatDependsOnTheProjectAndWhatItTakes()
    {
        // Each claim is a sentence a person acts on, so its condition is in the words: the count-in is in the main file ONLY, and it is a
        // MUTED lane that is a silent file (a lane that solo leaves out, one with a chance of 0 and an empty lane are silent files too;
        // docs/WAV-STEMS.md says so, the sentence does not claim they are not).
        var parts = new Dictionary<string, string[]>
        {
            ["en"] = ["main beat", "each lane", "separate WAV files", "one ZIP", "same moment", "count-in", "main file only", "muted lane", "silent file", "Unzipped", "the ZIP can be smaller"],
            ["nl"] = ["hoofdpuls", "elke laag", "losse WAV-bestanden", "één ZIP", "dezelfde start", "aftellen", "alleen in het hoofdbestand", "gedempte laag", "stil bestand", "Uitgepakt", "de ZIP kan kleiner zijn"],
            ["de"] = ["Hauptpuls", "jede Spur", "getrennte WAV-Dateien", "einer ZIP-Datei", "demselben Start", "Einzählen", "nur in der Hauptdatei", "stummgeschaltete Spur", "stille Datei", "Entpackt", "die ZIP-Datei kann kleiner sein"],
            ["fr"] = ["pulsation principale", "chaque piste", "fichiers WAV séparés", "un seul ZIP", "même instant", "décompte", "seulement dans le fichier principal", "le son est coupé",             "fichier silencieux", "Décompressés", "le ZIP peut être plus petit"],
                        ["es"] = ["pulso principal", "cada pista", "archivos WAV separados", "un solo ZIP", "mismo inicio", "cuenta de entrada", "solo en el archivo principal", "pista silenciada", "archivo en silencio", "Descomprimidos", "el ZIP puede ser menor"]
        };
        foreach (var language in Languages)
        {
            var question = Get(language, "StemExportConfirm");
            foreach (var part in parts[language]) StringAssert.Contains(question, part, language);
        }
    }

    [TestMethod]
    public void TheTextCallsTheMainBeatAndALaneWhatThePagesThatShowThemCallThem()
    {
        // The words come from the strings of Play, Studio and the timing check, so a renamed control renames this text too.
        foreach (var language in Languages)
        {
            var main = Get(language, "MainTarget");
            StringAssert.Contains(Get(language, "StemExportHint"), main, StringComparison.OrdinalIgnoreCase, language);
            StringAssert.Contains(Get(language, "StemExportConfirm"), main, StringComparison.OrdinalIgnoreCase, language);
            // "Lane 1, volume" in English, "Laag 1" in Dutch, "Spur 1", "Piste 1", "Pista 1": the first word is the name of a lane.
            var lane = Get(language, "LaneField").Split(' ')[0];
            StringAssert.Contains(Get(language, "StemExportHint"), lane, StringComparison.OrdinalIgnoreCase, language);
            StringAssert.Contains(Get(language, "StemExportConfirm"), lane, StringComparison.OrdinalIgnoreCase, language);
        }
    }

    [TestMethod]
    public void TheTooLongSentenceNamesTheSettingThatCanBeChangedWithItsOwnLabel()
    {
        foreach (var language in Languages)
        {
            var sentence = string.Format(CultureInfo.InvariantCulture, Get(language, "StemExportTooLong"), Get(language, "ExportBars"));
            StringAssert.Contains(sentence, Get(language, "ExportBars"), language);
            StringAssert.Contains(sentence, "4\u00A0GiB", language);
            Assert.IsFalse(sentence.Contains('{'), language);
        }
    }

    [TestMethod]
    public void TheNotSavedSentenceSaysThatAFileThatWasThereIsUnchanged()
    {
        var unchanged = new Dictionary<string, string>
        {
            ["en"] = "has not been changed", ["nl"] = "is niet gewijzigd", ["de"] = "wurde nicht verändert",
            ["fr"] = "n’a pas été modifié", ["es"] = "no se ha modificado"
        };
        var spaceAndOpen = new Dictionary<string, string>
        {
            ["en"] = "free space", ["nl"] = "vrije ruimte", ["de"] = "freien Platz", ["fr"] = "espace libre", ["es"] = "espacio libre"
        };
        var temporaryFile = new Dictionary<string, string>
        {
            ["en"] = "temporary file", ["nl"] = "tijdelijk bestand", ["de"] = "temporäre Datei", ["fr"] = "fichier temporaire", ["es"] = "archivo temporal"
        };
        foreach (var language in Languages)
        {
            var sentence = Get(language, "StemExportNotSaved");
            StringAssert.Contains(sentence, unchanged[language], language);
            StringAssert.Contains(sentence, spaceAndOpen[language], language);
            // The sentence for a temporary file that could not be removed says the same about the file that was there, and which kind of file stays.
            var leftover = Get(language, "StemExportLeftover");
            StringAssert.Contains(leftover, unchanged[language], language);
            StringAssert.Contains(leftover, temporaryFile[language], language);
        }
    }

    [TestMethod]
    public void NoWordOfTheEngineOrTheFileFormatReachesThePerson()
    {
        // What the text explains is separate sound files in a ZIP, not how they are encoded or why they can differ by a rounding step.
        string[] words =
        [
            "PCM", "RIFF", "RF64", "peak", "clamp", "limiter", "manifest", "recombin", "suppress", "stem", "onset", "armed", "deterministic",
            "flush", "frame", "quantum", "spliced", "document", "Spitzen", "Manifest", "kanaal", "stéréo", "estéreo", "stereo"
        ];
        foreach (var language in Languages)
        foreach (var key in Keys)
        foreach (var word in words)
            Assert.IsFalse(Get(language, key).Contains(word, StringComparison.OrdinalIgnoreCase), $"{language}/{key} uses “{word}”");
    }

    [TestMethod]
    public void TheFrenchPunctuationHasNoBreakSpacesSoThatNoLineStartsWithAColonOrAQuestionMark()
    {
        foreach (var key in Keys)
        {
            var french = Get("fr", key);
            foreach (var mark in new[] { " :", " ?", " ;", " !" })
                Assert.IsFalse(french.Contains(mark), $"fr/{key} has a space that can break before '{mark.Trim()}': {french}");
        }
        StringAssert.Contains(Get("fr", "StemExportConfirm"), "ZIP\u00A0?");
        StringAssert.Contains(Get("fr", "StemExportConfirm"), "Fichiers WAV\u00A0:");
        StringAssert.Contains(Get("fr", "StemExportConfirm"), "environ {2}\u00A0;");
    }

    [TestMethod]
    public void ANumberAndItsUnitAreJoinedByANoBreakSpaceSoThatNoLineEndsBetweenThem()
    {
        // A narrow dialog wraps a line at every ordinary space: "18.7" at the end of one line and "MiB" at the start of the next reads
        // as two things (the hosted picture of the question at 1000x720 did exactly that before).
        foreach (var language in Languages)
        {
            var sentences = new List<(string Where, string Text)>
            {
                ($"{language}/StemExportTooLong", string.Format(CultureInfo.InvariantCulture, Get(language, "StemExportTooLong"), Get(language, "ExportBars")))
            };
            foreach (var bytes in new[] { 512L, 6_528_000L, 3_500_000_000L })
                sentences.Add(($"{language}/StemExportConfirm for {bytes} bytes", ConfirmIn(language, new StemExport.Summary(4, TimeSpan.FromSeconds(34), bytes))));
            foreach (var (where, text) in sentences)
            {
                Assert.IsFalse(Regex.IsMatch(text, "[0-9] (KiB|MiB|GiB)"), $"{where}: a number is separated from its unit by a space that can break: {text}");
                Assert.IsTrue(Regex.IsMatch(text, "[0-9]\u00A0(KiB|MiB|GiB)"), $"{where}: no number is joined to its unit: {text}");
            }
        }
    }

    [TestMethod]
    public void TheTextIsShortEnoughForTheRoomItHas()
    {
        foreach (var language in Languages)
        {
            // A command in a row of three beside Export WAV and Export MIDI: its label cannot be long.
            Assert.IsTrue(Get(language, "ExportStems").Length <= 34, $"{language}/ExportStems: {Get(language, "ExportStems")}");
            Assert.IsTrue(Get(language, "StemExportHint").Length <= 200, $"{language}/StemExportHint: {Get(language, "StemExportHint").Length}");
            Assert.IsTrue(Get(language, "StemExportConfirm").Length <= 360, $"{language}/StemExportConfirm: {Get(language, "StemExportConfirm").Length}");
            // The notice is one row: it says only that it is saving.
            Assert.IsTrue(Get(language, "StemExportSaving").Length <= 36, $"{language}/StemExportSaving: {Get(language, "StemExportSaving")}");
            Assert.IsTrue(Get(language, "StemExportSlow").Length <= 40, $"{language}/StemExportSlow: {Get(language, "StemExportSlow")}");
            Assert.IsTrue(Get(language, "StemExportNotSaved").Length <= 260, $"{language}/StemExportNotSaved: {Get(language, "StemExportNotSaved").Length}");
            Assert.IsTrue(Get(language, "StemExportLeftover").Length <= 300, $"{language}/StemExportLeftover: {Get(language, "StemExportLeftover").Length}");
            Assert.IsTrue(Get(language, "StemExportTooLong").Length <= 220, $"{language}/StemExportTooLong: {Get(language, "StemExportTooLong").Length}");
        }
        Assert.IsTrue(Get("en", "StemExportHint").Length <= 160, $"en/StemExportHint: {Get("en", "StemExportHint").Length}");
    }

    [TestMethod]
    public void TheLabelNamesWhatThePersonGetsAndTheFileType()
    {
        var label = new Dictionary<string, string>
        {
            ["en"] = "separate tracks", ["nl"] = "Losse sporen", ["de"] = "Einzelspuren", ["fr"] = "pistes séparées", ["es"] = "pistas separadas"
        };
        foreach (var language in Languages)
        {
            var text = Get(language, "ExportStems");
            StringAssert.Contains(text, label[language], language);
            Assert.IsTrue(text.EndsWith("(ZIP)", StringComparison.Ordinal), $"{language}: the file type is part of the label, as it is for (CSV)");
        }
    }
}
