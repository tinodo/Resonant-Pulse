using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

/// <summary>
/// What the Train page decides about exporting the last finished timing check, apart from the window: when it is offered, what the
/// save dialog suggests, how the finished check is described. The CSV file itself is held by the Core tests.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TimingExportTests
{
    private static readonly string[] Languages = ["en", "nl", "de", "fr", "es"];

    private static readonly DateTimeOffset Done = new(2026, 10, 31, 0, 46, 32, TimeSpan.Zero);

    private static TimingSession Session(string project = "Practice", string target = TimingSession.MainBeatName, int lane = -1,
        double[]? expected = null, double[]? observed = null) =>
        new(new TimingSessionContext(project, target, lane, 8000, 0, 40000, true), expected ?? [0, 1, 2, 3], observed ?? [0.01, 1.02, 2.03, 3.04], 0.15, Done);

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
    public void TheExportIsOfferedOnlyForAFinishedCheckThatIsNotRunning()
    {
        var finished = Session();
        Assert.IsFalse(TimingExport.CanExport(false, null), "nothing has finished");
        Assert.IsFalse(TimingExport.CanExport(true, null), "a check is running and nothing has finished");
        Assert.IsFalse(TimingExport.CanExport(true, finished), "a check is running: its result does not exist yet");
        Assert.IsTrue(TimingExport.CanExport(false, finished));
    }

    [TestMethod]
    public void TheSuggestedNameIsTheProjectThenTiming()
    {
        Assert.AreEqual("Practice-timing", TimingExport.SuggestedFileName("Practice"));
        Assert.AreEqual("Étude no. 3-timing", TimingExport.SuggestedFileName("Étude no. 3"));
        // What a file name cannot hold is left out, and so are spaces at its ends.
        Assert.AreEqual("My groove-timing", TimingExport.SuggestedFileName("  My: groove?  "));
        Assert.AreEqual("ab-timing", TimingExport.SuggestedFileName("a/b"));
        Assert.AreEqual("ab-timing", TimingExport.SuggestedFileName("a\\b"));
        Assert.AreEqual("line one two-timing", TimingExport.SuggestedFileName("line one\r\n two"));
        // The same on every machine: each of the nine characters that Windows refuses is left out, wherever the test runs.
        Assert.AreEqual("abcdefghij-timing", TimingExport.SuggestedFileName("a<b>c:d\"e/f\\g|h?i*j"));
        // Nothing is left of a name that is only such characters, or of none at all.
        foreach (var name in new[] { "", "   ", "???", "<>:\"/\\|?*", "\0\u0001" })
            Assert.AreEqual("ResonantPulse-timing", TimingExport.SuggestedFileName(name), $"'{name}'");
    }

    [TestMethod]
    public void TheSuggestedNameKeepsItsEndingWhateverTheLengthOfTheProject()
    {
        // The dialog takes 100 characters of a suggestion: the ending must not be among those that are cut.
        foreach (var length in new[] { 1, 50, 89, 90, 91, 100, 101, 256 })
        {
            var name = TimingExport.SuggestedFileName(new string('x', length));
            Assert.IsTrue(name.EndsWith("-timing", StringComparison.Ordinal), $"{length}: {name}");
            Assert.IsTrue(name.Length <= 100, $"{length}: {name.Length} characters");
            Assert.AreEqual(Math.Min(length, 90) + "-timing".Length, name.Length);
        }
    }

    [TestMethod]
    public void TheSuggestedNameIsNeverCutBetweenTheHalvesOfACharacter()
    {
        // 89 letters, then a character written with two halves, then more: the cut at 90 falls between the halves.
        var name = TimingExport.SuggestedFileName(new string('x', 89) + "\U0001F941 and more");
        Assert.AreEqual(new string('x', 89) + "-timing", name);
        for (var cut = 80; cut < 96; cut++)
        {
            var suggestion = TimingExport.SuggestedFileName(new string('x', cut) + "\U0001F941\U0001F941\U0001F941");
            for (var index = 0; index < suggestion.Length; index++)
            {
                if (char.IsHighSurrogate(suggestion[index]))
                {
                    Assert.IsTrue(index + 1 < suggestion.Length && char.IsLowSurrogate(suggestion[index + 1]), $"{cut}: half a character in {suggestion}");
                    index++;
                }
                else Assert.IsFalse(char.IsLowSurrogate(suggestion[index]), $"{cut}: half a character in {suggestion}");
            }
        }
    }

    [TestMethod]
    public void TheMainBeatHasOneNameInEveryLanguageAndALaneKeepsItsOwn()
    {
        foreach (var language in Languages)
        {
            Assert.AreEqual("Main beat", InLanguage(language, () => TimingExport.TargetName(-1, Text.Get("MainTarget"))), language);
            Assert.AreEqual("Main beat", InLanguage(language, () => TimingExport.TargetName(-1, "")), language);
            Assert.AreEqual("Kick", InLanguage(language, () => TimingExport.TargetName(0, "Kick")), language);
            Assert.AreEqual("Lane 8", InLanguage(language, () => TimingExport.TargetName(7, "Lane 8")), language);
        }
    }

    [TestMethod]
    public void TheResultIsDescribedInEveryLanguageWithTheDriftOnlyWhenItWasMeasured()
    {
        foreach (var language in Languages)
        {
            // Four presses that are 10, 20, 30 and 40 ms late: a drift of 10 ms a second can be measured.
            var measured = InLanguage(language, () => TimingExport.ReportText(Session().Report));
            var drift = InLanguage(language, () => Text.Format("TimingAdditional", Session().Report.DriftMillisecondsPerSecond, 0));
            StringAssert.Contains(measured, drift, language);
            Assert.IsFalse(measured.Contains(InLanguage(language, () => Text.Get("TimingDriftUnavailable"))), language);

            // Four clicks at one moment cannot show a change over time: the page says so instead of a drift of 0.
            var coincident = Session(expected: [1, 1, 1, 1], observed: [1.01, 1.01, 1.01, 1.01]);
            Assert.AreEqual(4, coincident.Report.Matched);
            var unavailable = InLanguage(language, () => TimingExport.ReportText(coincident.Report));
            StringAssert.Contains(unavailable, InLanguage(language, () => Text.Get("TimingDriftUnavailable")), language);
            Assert.IsFalse(unavailable.Contains(InLanguage(language, () => Text.Format("TimingAdditional", 0.0, 0))), $"{language}: a drift of 0 is shown for clicks at one moment");

            // Fewer than four matches: the same sentence.
            var few = Session(expected: [0, 1], observed: [0.01, 1.01]);
            StringAssert.Contains(InLanguage(language, () => TimingExport.ReportText(few.Report)), InLanguage(language, () => Text.Get("TimingDriftUnavailable")), language);

            // No match at all: the page counts what was missed and what was extra, and has no offset to show.
            var none = Session(expected: [1], observed: [3]);
            var text = InLanguage(language, () => TimingExport.ReportText(none.Report));
            Assert.AreEqual(InLanguage(language, () => Text.Format("TimingNoMatches", 1, 1)), text, language);
            Assert.IsFalse(text.Contains('{') || text.Contains('}'), $"{language}: {text}");
        }
    }

    [TestMethod]
    public void TheReadyLineNamesWhatTheExportHoldsInTheLanguageInUse()
    {
        foreach (var language in Languages)
        {
            var session = Session("Étude no. 3", target: "Kick", lane: 0);
            var line = InLanguage(language, () => TimingExport.ReadyText(session));
            StringAssert.Contains(line, "Étude no. 3", language);
            StringAssert.Contains(line, "Kick", language);
            var when = InLanguage(language, () => Done.ToLocalTime().ToString("g", Text.Culture));
            StringAssert.Contains(line, when, language);
            Assert.IsFalse(line.Contains('{') || line.Contains('}'), $"{language}: {line}");

            // The main beat is named as the page names it, not as the file does.
            var main = InLanguage(language, () => TimingExport.ReadyText(Session()));
            StringAssert.Contains(main, InLanguage(language, () => Text.Get("MainTarget")), language);
        }
        Assert.AreNotEqual(
            InLanguage("nl", () => TimingExport.ReadyText(Session())),
            InLanguage("en", () => TimingExport.ReadyText(Session())));
    }

    [TestMethod]
    public void TheReadyLineShortensALongNameSoThatTheRhythmAndTheTimeAreAlwaysThere()
    {
        var longName = new string('x', 200);
        foreach (var language in Languages)
        {
            var line = InLanguage(language, () => TimingExport.ReadyText(Session(longName, target: longName, lane: 3)));
            var when = InLanguage(language, () => Done.ToLocalTime().ToString("g", Text.Culture));
            // The end of the line is what a narrow window cuts off, and the time is the part that cannot be spared.
            Assert.IsTrue(line.EndsWith(when + ".", StringComparison.Ordinal), $"{language}: {line}");
            StringAssert.Contains(line, new string('x', TimingExport.MaximumNameShown - 1) + "…", language);
            Assert.IsFalse(line.Contains(new string('x', TimingExport.MaximumNameShown)), $"{language}: a name is not cut");
            Assert.IsTrue(line.Length <= 2 * TimingExport.MaximumNameShown + 70, $"{language}: {line.Length} characters");
        }
        // A name of exactly the limit is shown whole; one more character and the end is an ellipsis.
        var whole = TimingExport.ReadyText(Session(new string('a', TimingExport.MaximumNameShown)));
        StringAssert.Contains(whole, new string('a', TimingExport.MaximumNameShown));
        Assert.IsFalse(whole.Contains('…'));
        var cut = TimingExport.ReadyText(Session(new string('a', TimingExport.MaximumNameShown + 1)));
        StringAssert.Contains(cut, new string('a', TimingExport.MaximumNameShown - 1) + "…");
        // The cut never falls between the halves of a character, and no space is left in front of the ellipsis.
        var pair = TimingExport.ReadyText(Session(new string('b', TimingExport.MaximumNameShown - 2) + "\U0001F941\U0001F941"));
        StringAssert.Contains(pair, new string('b', TimingExport.MaximumNameShown - 2) + "…");
        Assert.IsFalse(pair.Any(char.IsLowSurrogate) || pair.Any(char.IsHighSurrogate), pair);
        var spaced = TimingExport.ReadyText(Session(new string('c', TimingExport.MaximumNameShown - 3) + "    dddd"));
        StringAssert.Contains(spaced, new string('c', TimingExport.MaximumNameShown - 3) + "…");
    }

    [TestMethod]
    public void TheReadyLineIsOneLineWhateverIsInTheNames()
    {
        foreach (var language in Languages)
        {
            var line = InLanguage(language, () => TimingExport.ReadyText(Session("one\r\ntwo\tthree\u0085four", target: "a\nb", lane: 2)));
            Assert.IsFalse(line.Any(char.IsControl), $"{language}: a control character is in the line");
            StringAssert.Contains(line, "one  two three four", language);
            StringAssert.Contains(line, "a b", language);

            // A line separator and a paragraph separator are not control characters, and a text block breaks the line at them.
            var separated = InLanguage(language, () => TimingExport.ReadyText(Session("five\u2028six\u2029seven", target: "c\u2028d", lane: 2)));
            Assert.IsFalse(separated.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029'), $"{language}: a line break is in the line");
            StringAssert.Contains(separated, "five six seven", language);
            StringAssert.Contains(separated, "c d", language);
        }
        // Braces in a name are text, not a placeholder.
        var braces = InLanguage("en", () => TimingExport.ReadyText(Session("{0} {1}", target: "{2}", lane: 1)));
        StringAssert.Contains(braces, "{0} {1}");
        StringAssert.Contains(braces, "{2}");
    }

    [TestMethod]
    public void ANameWithNothingToShowIsSaidToHaveNoNameInsteadOfLeavingAGapInTheLine()
    {
        foreach (var language in Languages)
        {
            var none = InLanguage(language, () => Text.Get("TimingNoName"));
            // The project and the lane that was checked both have nothing to show: empty, spaces, a tab, line breaks, a no-break space.
            foreach (var blank in new[] { "", " ", "   ", "\t", "\r\n", "\u2028\u2029", "\u00A0", "\u0085" })
            {
                var line = InLanguage(language, () => TimingExport.ReadyText(Session(blank, target: blank, lane: 1)));
                Assert.AreEqual(2, line.Split(none).Length - 1, $"{language}, name '{blank}': {line}");
                // The main beat has a name of its own, so only the project is without one.
                var main = InLanguage(language, () => TimingExport.ReadyText(Session(blank)));
                Assert.AreEqual(1, main.Split(none).Length - 1, $"{language}, project '{blank}': {main}");
            }
            // A name with something in it is shown as it is, spaces at its ends included.
            var named = InLanguage(language, () => TimingExport.ReadyText(Session(" a ", target: "b", lane: 1)));
            Assert.IsFalse(named.Contains(none), $"{language}: {named}");
            StringAssert.Contains(named, " a ", language);
        }
    }
}
