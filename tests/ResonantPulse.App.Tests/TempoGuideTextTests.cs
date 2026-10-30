using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Tests;

/// <summary>
/// The sentences around the Italian names of the tempo guide, in every language: what the line says it names, what it counts in, and
/// what it says when there is nothing to name. The names themselves are Italian terms that musicians write the same way everywhere;
/// they are code (TempoGuide.Names), not entries of the translated text, so the translation tests (every string in five languages,
/// none copied from English) apply to the sentences and never meet them.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TempoGuideTextTests
{
    private static readonly string[] Languages = ["en", "nl", "de", "fr", "es"];

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

    [TestMethod]
    public void TheLineSaysWhatItNamesAndWhatItCountsInInEveryLanguage()
    {
        // The word for "approximate" is a condition of every sentence the guide shows: the bands are background, not a standard.
        var approximate = new Dictionary<string, string>
        {
            ["en"] = "Approximate", ["nl"] = "Globale", ["de"] = "Ungefähre", ["fr"] = "approximatives", ["es"] = "aproximadas"
        };
        foreach (var language in Languages)
        {
            var line = InLanguage(language, () => TempoGuide.Describe(120, 1));
            StringAssert.Contains(line, "Moderato / Allegro", language);
            StringAssert.Contains(line, "120", language);
            StringAssert.Contains(line, "BPM", language);
            StringAssert.Contains(line, approximate[language], language);
            // What it counts in is named with the app's own word for the note (the one beside "Position" in Studio), so a renamed
            // word renames the guide, and a speed counted in another note is said in quarter notes.
            StringAssert.Contains(line, InLanguage(language, () => Text.Get("UnitQuarterNotes")), language);
            Assert.IsFalse(line.Contains('{') || line.Contains('}'), $"{language}: {line}");
            // At 400 DIP wide a panel line of 90 characters is three lines; a longer one would push the commands under it out of reach.
            Assert.IsTrue(line.Length <= 90, $"{language}: {line.Length} characters: {line}");
        }
    }

    [TestMethod]
    public void ATempoCountedInAnotherNoteIsSaidInQuarterNotesInEveryLanguage()
    {
        foreach (var language in Languages)
        {
            // 80 BPM counted in dotted quarters and 120 counted in eighths: 120 and 60 quarter notes a minute.
            var dotted = InLanguage(language, () => TempoGuide.Describe(80, 1.5));
            StringAssert.Contains(dotted, "120", language);
            StringAssert.Contains(dotted, "Moderato / Allegro", language);
            var eighths = InLanguage(language, () => TempoGuide.Describe(120, 0.5));
            StringAssert.Contains(eighths, " 60 ", language);
            StringAssert.Contains(eighths, "Largo / Larghetto", language);
        }
    }

    [TestMethod]
    public void ASpeedThatIsNotWholeIsPrintedWithTheLanguagesOwnDecimalMark()
    {
        foreach (var language in Languages)
        {
            var line = InLanguage(language, () => TempoGuide.Describe(33.3, 1));
            StringAssert.Contains(line, language == "en" ? "33.3" : "33,3", language);
            StringAssert.Contains(line, "Grave", language);
        }
    }

    [TestMethod]
    public void WhenThereIsNothingToNameTheLineSaysWhatToEnterInEveryLanguage()
    {
        foreach (var language in Languages)
        {
            var expected = InLanguage(language, () => Text.Get("TempoGuideInput"));
            Assert.AreNotEqual("TempoGuideInput", expected, $"{language}: the text is missing");
            foreach (var (tempo, unit) in new[] { (double.NaN, 1d), (0d, 1d), (1000.1, 1d), (-3d, 1d), (120d, 0d), (120d, double.NaN) })
                Assert.AreEqual(expected, InLanguage(language, () => TempoGuide.Describe(tempo, unit)), $"{language}: {tempo} at {unit}");
            // The tempos that the box takes are named, and no marking is, because there is no tempo.
            StringAssert.Contains(expected, "1", language);
            StringAssert.Contains(expected, "1000", language);
            StringAssert.Contains(expected, "BPM", language);
            foreach (var name in TempoGuide.Names) Assert.IsFalse(expected.Contains(name, StringComparison.Ordinal), $"{language}: {expected}");
            Assert.IsFalse(expected.Contains('{') || expected.Contains('}'), $"{language}: {expected}");
            Assert.IsTrue(expected.Length <= 90, $"{language}: {expected.Length} characters: {expected}");
        }
    }

    [TestMethod]
    public void TheItalianTermsAreTheSameInEveryLanguageAndAreNotTranslatedText()
    {
        // One speed inside each band, slow to fast.
        var inside = new (double Tempo, string Name)[]
        {
            (10, "Larghissimo"), (30, "Grave"), (50, "Largo"), (63, "Larghetto"), (70, "Adagio"), (90, "Andante"),
            (110, "Moderato"), (140, "Allegro"), (160, "Vivace"), (180, "Presto"), (250, "Prestissimo")
        };
        CollectionAssert.AreEqual(TempoGuide.Names.ToArray(), inside.Select(item => item.Name).ToArray());
        foreach (var language in Languages)
        foreach (var (tempo, name) in inside)
            StringAssert.Contains(InLanguage(language, () => TempoGuide.Describe(tempo, 1)), $": {name}", $"{language}: {name}");
        // They are not strings of the translated text: the tests of that text ask every string to differ from English in four
        // languages, which is exactly what an Italian term must not do.
        var english = InLanguage("en", () => Text.Keys.Select(Text.Get).ToArray());
        foreach (var name in TempoGuide.Names)
            CollectionAssert.DoesNotContain(english, name, $"{name} is in the translated text");
    }
}
