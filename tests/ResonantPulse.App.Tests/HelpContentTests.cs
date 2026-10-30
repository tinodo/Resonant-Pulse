using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Tests;

/// <summary>
/// The words of the Help panel on Play (docs/design/pages-design.md, Play panels): a quick start that takes a beginner from the
/// first launch to a steady click at their own tempo and time signature, what plays at once and what waits for Stop and Start, the
/// shortcuts and where they work, and a short musical reference with one example for each idea. HelpFactsTests checks what these
/// sentences claim against the engine; this class checks the text itself, in all five languages.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HelpContentTests
{
    private static readonly string[] Languages = ["en", "nl", "de", "fr", "es"];

    /// <summary>
    /// The entries in the order they are read. packaging/Verify-FirstUse.ps1 finds each one on the real window by this id, so the
    /// list and the script change together.
    /// </summary>
    private static readonly string[] ExpectedIds =
    [
        "HelpQuickStartHeading", "HelpStep1", "HelpStep2", "HelpStep3", "HelpStep4", "HelpStep5",
        "HelpTapStartHeading", "HelpTapStartText", "HelpTapStartLimits",
        "HelpPlayingHeading", "HelpPlaying", "HelpLive", "HelpProgram",
        "HelpShortcutsHeading", "HelpShortcuts", "HelpShortcutScope",
        "HelpReferenceHeading",
        "HelpBpmTitle", "HelpBpm", "HelpBpmExample",
        "HelpMeterTitle", "HelpMeter", "HelpMeterExample",
        "HelpFeelTitle", "HelpFeel", "HelpFeelExample",
        "HelpStrengthTitle", "HelpStrength", "HelpStrengthExample"
    ];

    /// <summary>Words that mean nothing to a beginner (docs/design/pages-design.md, copy rules, and the words of the engine).</summary>
    private static readonly string[] BannedEnglish =
    [
        "onset", "armed", "deterministic", "graph", "flush", "document", "frame", "quantum", "splice", "denominator", "numerator",
        "attack", "tuplet", "revision", "transport", "endpoint", "buffer", "sample", "latency", "schedul", "compile", "queue",
        "phase", "accent", "pulse"
    ];

    /// <summary>The same ideas in the other languages: the words of the engine that a translation could carry over.</summary>
    private static readonly Dictionary<string, string[]> BannedTranslated = new()
    {
        ["nl"] = ["noemer", "aanslag", "transport", "accent", "puls"],
        ["de"] = ["Nenner", "Anschlag", "Transport", "Akzent", "Puls"],
        ["fr"] = ["dénominateur", "attaque", "transport", "accent", "pulsation"],
        ["es"] = ["denominador", "ataque", "transporte", "acento"]
    };

    private static T InLanguage<T>(string language, Func<T> read)
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            Text.Select(language);
            return read();
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static IReadOnlyList<HelpEntry> Entries(string language) => InLanguage(language, HelpContent.Entries);

    private static string Get(string language, string key) => InLanguage(language, () => Text.Get(key));

    private static IEnumerable<HelpEntry> Written(string language) => Entries(language).Where(entry => entry.Kind != HelpKind.Shortcuts);

    [TestMethod]
    public void TheHelpReadsInTheOrderOfUseAndEveryEntryHasItsOwnId()
    {
        CollectionAssert.AreEqual(ExpectedIds, Entries("en").Select(entry => entry.Id).ToArray());
    }

    [TestMethod]
    public void TheHelpHasTheSameEntriesInEveryLanguage()
    {
        var english = Entries("en").Select(entry => (entry.Kind, entry.Id)).ToArray();
        foreach (var language in Languages.Skip(1))
            CollectionAssert.AreEqual(english, Entries(language).Select(entry => (entry.Kind, entry.Id)).ToArray(), language);
    }

    [TestMethod]
    public void EveryEntryHasTextInEveryLanguageWithNoPlaceholderLeftOver()
    {
        foreach (var language in Languages)
        foreach (var entry in Written(language))
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Text), $"{language}/{entry.Id} is empty");
            Assert.IsFalse(entry.Text.Contains('{') || entry.Text.Contains('}'), $"{language}/{entry.Id} has a placeholder left: {entry.Text}");
            // Text.Get answers with the key itself when a string is missing.
            Assert.IsFalse(Regex.IsMatch(entry.Text, @"^Help[A-Za-z]+$"), $"{language}/{entry.Id} has no text: {entry.Text}");
        }
    }

    [TestMethod]
    public void TheQuickStartIsFiveShortStepsFromTheOutputToTheStrengthOfABeat()
    {
        foreach (var language in Languages)
        {
            var steps = Written(language).Where(entry => entry.Kind == HelpKind.Step).ToArray();
            Assert.AreEqual(5, steps.Length, language);
            // A step is one thing to do. English stays under 170 characters; the longest translation under 240.
            foreach (var step in steps)
                Assert.IsTrue(step.Text.Length <= (language == "en" ? 170 : 240), $"{language}/{step.Id}: {step.Text.Length} characters: {step.Text}");
        }
        // Output first, then Start, then the tempo, the time signature and the strength of a beat.
        var english = Written("en").Where(entry => entry.Kind == HelpKind.Step).Select(entry => entry.Text).ToArray();
        StringAssert.Contains(english[0], "output");
        StringAssert.Contains(english[1], "Start");
        StringAssert.Contains(english[2], "tempo");
        StringAssert.Contains(english[3], "time signature");
        StringAssert.Contains(english[4], "beat");
    }

    [TestMethod]
    public void EveryParagraphStaysShort()
    {
        // One idea each: at most 320 characters in English, 420 in the longer translations. The limits paragraph of Tap to start (a list
        // of its conditions) is the longest.
        foreach (var language in Languages)
        foreach (var entry in Written(language).Where(entry => entry.Kind is HelpKind.Paragraph or HelpKind.Example))
            Assert.IsTrue(entry.Text.Length <= (language == "en" ? 320 : 420), $"{language}/{entry.Id}: {entry.Text.Length} characters: {entry.Text}");
    }

    [TestMethod]
    public void TheWholeHelpStaysCompact()
    {
        // Short enough to read in a few screens of a small panel: about 3,500 characters in English, which states the conditions that
        // its claims depend on (a shorter text said less than was true).
        foreach (var language in Languages)
        {
            var total = Written(language).Sum(entry => entry.Text.Length);
            Assert.IsTrue(total <= (language == "en" ? 3600 : 4700), $"{language}: {total} characters");
        }
    }

    [TestMethod]
    public void TheTextNamesEachControlByTheLabelItHasInThatLanguage()
    {
        // A control is named with the word on its own button, tab or field, so a person can find it, in every language. The
        // sentences take the label from the same string the control shows, so a renamed control renames the Help with it.
        var named = new Dictionary<string, string[]>
        {
            ["HelpStep1"] = ["AudioOptions", "Start"],
            ["HelpStep2"] = ["Start", "KeySpace"],
            ["HelpStep3"] = ["KeyCtrl", "TapShort", "CloseOptions"],
            ["HelpStep4"] = ["MeterCustom"],
            ["HelpStep5"] = ["PulseStrengths", "LevelStrong", "LevelNormal", "LevelSoft", "LevelSilent"],
            ["HelpTapStartHeading"] = ["TapStart"],
            ["HelpTapStartText"] = ["TapStart", "TapShort"],
            ["HelpTapStartLimits"] = ["TempoTab", "Stop"],
            ["HelpPlaying"] = ["Pause", "Start", "Stop", "Mute"],
            ["HelpLive"] = ["Stop", "Start", "PracticeTab", "Studio"],
            ["HelpProgram"] = ["Studio"],
            ["HelpShortcutScope"] = ["KeySpace", "KeyCtrl", "Play"],
            ["HelpBpm"] = ["Unit", "TempoTab"],
            ["HelpMeterExample"] = ["TempoTab"],
            ["HelpFeel"] = ["MeterCustom", "SectionFeel"],
            ["HelpStrengthTitle"] = ["PulseStrengths"],
            ["HelpStrength"] = ["LevelStrong", "LevelNormal", "LevelSoft", "LevelSilent"],
            ["HelpStrengthExample"] = ["LevelSilent"]
        };
        foreach (var language in Languages)
        {
            var entries = Written(language).ToDictionary(entry => entry.Id, entry => entry.Text);
            foreach (var (id, keys) in named)
            foreach (var key in keys)
                StringAssert.Contains(entries[id], Get(language, key), $"{language}/{id} does not name “{Get(language, key)}” ({key})");
        }
    }

    [TestMethod]
    public void TheTextCallsTheSubdivisionAndSwingFieldsByTheFirstWordsOfTheirLabels()
    {
        // The labels of these fields are long ("Subdivision: clicks between beats (1 = none)", "Swing · first share (0.5 = straight)"),
        // so the sentences name them by the words that start them instead of inserting them. This keeps those words the ones the
        // fields show, in every language. The click counts of a time signature hold while Subdivision is 1 (HelpFactsTests), so the two
        // entries about the click say so, with the field's own word.
        foreach (var language in Languages)
        {
            var subdivision = Get(language, "SubdivisionLabel").Split(':')[0].Trim();
            var swing = Get(language, "Swing").Split('·')[0].Trim();
            var entries = Written(language).ToDictionary(entry => entry.Id, entry => entry.Text);
            foreach (var id in new[] { "HelpMeter", "HelpMeterExample", "HelpFeel", "HelpFeelExample" })
                Assert.IsTrue(entries[id].Contains(subdivision, StringComparison.OrdinalIgnoreCase), $"{language}/{id} does not use “{subdivision}”: {entries[id]}");
            foreach (var id in new[] { "HelpFeel", "HelpFeelExample" })
                Assert.IsTrue(entries[id].Contains(swing, StringComparison.OrdinalIgnoreCase), $"{language}/{id} does not use “{swing}”: {entries[id]}");
        }
    }

    [TestMethod]
    public void TheTextStatesWhatItsClaimsDependOn()
    {
        // A sentence that is only true under a condition says the condition, in every language: a new project's count-in, the output
        // that Tap to start needs, a count-in that Start plays again (a project can have none), the note value that makes Subdivision 2
        // swing eighth notes (HelpFactsTests computes each of these), and the keyboard's own conditions, which OnKeyDown decides: Space
        // does nothing while a button has the focus (it presses that button), and Esc and T need no panel open.
        var words = new Dictionary<string, string[][]>
        {
            ["HelpStep2"] = [["new project", "nieuw project", "neues Projekt", "nouveau projet", "proyecto nuevo"], ["focus", "focus", "Fokus", "focus", "foco"]],
            ["HelpTapStartLimits"] = [["output", "uitvoer", "Ausgabe", "sortie", "salida"], ["panel", "paneel", "Zusatzfenster", "panneau", "panel"]],
            ["HelpPlaying"] = [["count-in", "aftellen", "Einzählen", "décompte", "entrada"]],
            ["HelpShortcutScope"] = [["focus", "focus", "Fokus", "focus", "foco"], ["panel", "paneel", "Zusatzfenster", "panneau", "panel"]]
        };
        for (var index = 0; index < Languages.Length; index++)
        {
            var language = Languages[index];
            var entries = Written(language).ToDictionary(entry => entry.Id, entry => entry.Text);
            foreach (var (id, required) in words)
            foreach (var perLanguage in required)
                Assert.IsTrue(entries[id].Contains(perLanguage[index], StringComparison.OrdinalIgnoreCase), $"{language}/{id} does not say “{perLanguage[index]}”: {entries[id]}");
            var quarters = Get(language, "UnitQuarterNotes");
            Assert.IsTrue(entries["HelpFeelExample"].Contains(quarters, StringComparison.OrdinalIgnoreCase), $"{language}/HelpFeelExample does not name the note value it assumes (“{quarters}”): {entries["HelpFeelExample"]}");
        }
    }

    [TestMethod]
    public void TheHelpAvoidsTheWordsOfTheEngine()
    {
        foreach (var entry in Written("en"))
            foreach (var word in BannedEnglish)
                Assert.IsFalse(Regex.IsMatch(entry.Text, @"\b" + Regex.Escape(word), RegexOptions.IgnoreCase), $"en/{entry.Id} uses “{word}”: {entry.Text}");
        foreach (var (language, words) in BannedTranslated)
        foreach (var entry in Written(language))
        foreach (var word in words)
            Assert.IsFalse(Regex.IsMatch(entry.Text, @"\b" + Regex.Escape(word), RegexOptions.IgnoreCase), $"{language}/{entry.Id} uses “{word}”: {entry.Text}");
    }

    [TestMethod]
    public void EveryTranslationIsTranslatedNotCopiedFromEnglish()
    {
        var english = Written("en").ToDictionary(entry => entry.Id, entry => entry.Text);
        foreach (var language in Languages.Skip(1))
        foreach (var entry in Written(language))
            Assert.AreNotEqual(english[entry.Id], entry.Text, $"{language}/{entry.Id} is the English text");
    }
}
