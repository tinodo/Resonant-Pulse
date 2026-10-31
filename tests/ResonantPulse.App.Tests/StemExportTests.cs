using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

/// <summary>What the Library page decides about saving separate tracks, apart from the window: the numbers the confirmation shows.</summary>
[TestClass]
[DoNotParallelize]
public sealed class StemExportTests
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
    [DataRow(512L, "1 KiB")]
    [DataRow(19_200L, "19 KiB")]
    [DataRow(60_000L, "59 KiB")]
    [DataRow(104_857L, "102 KiB")]
    [DataRow(104_858L, "0.1 MiB")]
    [DataRow(1_048_576L, "1.0 MiB")]
    [DataRow(6_528_000L, "6.2 MiB")]
    [DataRow(1_072_000_000L, "1,022.3 MiB")]
    // 1,023.95 MiB is 1,073,689,395.2 bytes: one byte below it still reads in MiB, one above it already reads as 1.0 GiB, so a size
    // is never written as "1,024.0 MiB".
    [DataRow(1_073_689_395L, "1,023.9 MiB")]
    [DataRow(1_073_689_396L, "1.0 GiB")]
    [DataRow(1_073_741_824L, "1.0 GiB")]
    [DataRow(3_500_000_000L, "3.3 GiB")]
    [DataRow(38_654_705_664L, "36.0 GiB")]
    public void TheSizeIsInTheLargestUnitThatKeepsItAboveOne(long bytes, string expected) =>
        Assert.AreEqual(expected, StemExport.FormatSize(bytes, CultureInfo.GetCultureInfo("en")));

    [TestMethod]
    public void TheSizeFollowsTheLanguageForItsDecimalSeparatorAndKeepsTheUnitSymbols()
    {
        foreach (var (language, expected) in new[] { ("en", "6.2 MiB"), ("nl", "6,2 MiB"), ("de", "6,2 MiB"), ("fr", "6,2 MiB"), ("es", "6,2 MiB") })
            Assert.AreEqual(expected, InLanguage(language, () => StemExport.FormatSize(6_528_000, Text.Culture)), language);
        Assert.AreEqual("1.022,3 MiB", StemExport.FormatSize(1_072_000_000, CultureInfo.GetCultureInfo("nl")));
    }

    [TestMethod]
    [DataRow(34.0, "0:34")]
    [DataRow(33.2, "0:34")]
    [DataRow(0.1, "0:01")]
    [DataRow(59.0, "0:59")]
    [DataRow(60.0, "1:00")]
    [DataRow(600.0, "10:00")]
    [DataRow(3599.5, "1:00:00")]
    [DataRow(3661.0, "1:01:01")]
    [DataRow(86400.0, "24:00:00")]
    public void ALengthIsMinutesAndSecondsRoundedUpAndHoursFromAnHour(double seconds, string expected) =>
        Assert.AreEqual(expected, StemExport.FormatLength(TimeSpan.FromSeconds(seconds)));

    [TestMethod]
    public void ALengthHasTheSameDigitsInEveryLanguage()
    {
        foreach (var language in Languages)
            Assert.AreEqual("1:01:01", InLanguage(language, () => StemExport.FormatLength(TimeSpan.FromSeconds(3661))), language);
    }

    [TestMethod]
    public void TheSuggestedNameIsTheProjectThenTracksWithoutWhatAFileNameCannotHold()
    {
        Assert.AreEqual("Compound 6-8-tracks", StemExport.SuggestedFileName("Compound 6-8"));
        Assert.AreEqual("What is this-tracks", StemExport.SuggestedFileName("What: is *this*?"));
        Assert.AreEqual("abc-tracks", StemExport.SuggestedFileName("a\r\nb\tc"));
        Assert.AreEqual("ResonantPulse-tracks", StemExport.SuggestedFileName(""));
        Assert.AreEqual("ResonantPulse-tracks", StemExport.SuggestedFileName("  <>:\"/\\|?*  "));
        Assert.AreEqual("a b-tracks", StemExport.SuggestedFileName("  a b  "));
        var long100 = new string('x', 300);
        Assert.AreEqual(new string('x', 90) + "-tracks", StemExport.SuggestedFileName(long100));
    }

    [TestMethod]
    public void TheSuggestedNameIsNeverCutBetweenTheHalvesOfACharacter()
    {
        // 89 letters, then a character made of two halves: the cut after 90 would keep only the first half.
        var name = new string('x', 89) + "\uD83D\uDE00" + "tail";
        var suggested = StemExport.SuggestedFileName(name);
        Assert.AreEqual(new string('x', 89) + "-tracks", suggested);
        Assert.IsFalse(suggested.Any(char.IsSurrogate));
        var kept = StemExport.SuggestedFileName(new string('x', 88) + "\uD83D\uDE00" + "tail");
        Assert.AreEqual(new string('x', 88) + "\uD83D\uDE00-tracks", kept);
    }

    [TestMethod]
    public void TheSuggestedNameIsTheSameOnEveryMachine()
    {
        // The characters left out are a fixed set, not the running system's list: Path.GetInvalidFileNameChars lists only NUL and the
        // slash on Linux, which is where the tests of the hosted run.
        foreach (var invalid in "<>:\"/\\|?*" + string.Concat(Enumerable.Range(0, 32).Select(code => (char)code)))
            Assert.AreEqual("ab-tracks", StemExport.SuggestedFileName("a" + invalid + "b"), $"U+{(int)invalid:X4}");
    }

    [TestMethod]
    public void TheSummaryCountsTheMainTrackAndEveryLaneAndTheBytesTheExporterWillWrite()
    {
        var none = ScheduleCompiler.Compile(new PulseDocument(), 48000, 16, true);
        var summary = StemExport.Summarize(none);
        Assert.AreEqual(1, summary.Tracks);
        Assert.AreEqual(34, summary.Length.TotalSeconds, "16 bars of 2 seconds and the count-in bar");
        Assert.AreEqual(6_528_000L, summary.Bytes);
        Assert.AreEqual("0:34", StemExport.FormatLength(summary.Length));
        Assert.AreEqual("6.2 MiB", StemExport.FormatSize(summary.Bytes, CultureInfo.GetCultureInfo("en")));

        var document = new PulseDocument { Lanes = [new(), new(), new()] };
        document.Settings.CountInBars = 0;
        var three = ScheduleCompiler.Compile(document, 48000, 4, true);
        summary = StemExport.Summarize(three);
        Assert.AreEqual(4, summary.Tracks);
        Assert.AreEqual(8, summary.Length.TotalSeconds);
        Assert.AreEqual(WaveStemExporter.GetUncompressedAudioBytes(three), summary.Bytes);
        Assert.AreEqual(3 * 4 * 8 * 48000L + 8 * 48000L * 4, summary.Bytes, "Four tracks of 8 seconds: four bytes a frame each");
    }

    [TestMethod]
    public void ATrackThatCouldNotBeAWaveFileIsRefusedInThePersonsOwnLanguageBeforeAnythingIsAsked()
    {
        var document = new PulseDocument();
        document.Settings.Tempo = 1;
        document.Settings.BeatUnitQuarters = 16;
        document.Settings.CountInBars = 0;
        var plan = ScheduleCompiler.Compile(document, 192000, 5760, false);
        foreach (var language in Languages)
        {
            var error = Assert.ThrowsExactly<UserMessageException>(() => InLanguage(language, () => StemExport.Summarize(plan)));
            var expected = InLanguage(language, () => Text.Format("StemExportTooLong", Text.Get("ExportBars")));
            Assert.AreEqual(expected, error.Message, language);
            StringAssert.Contains(error.Message, "4 GiB", language);
            Assert.IsFalse(error.Message.Contains('{'), language);
            // The sentence names the setting that can be changed, with the label that setting has in this language.
            StringAssert.Contains(error.Message, InLanguage(language, () => Text.Get("ExportBars")), language);
        }
    }

    [TestMethod]
    public void TheConfirmationOnlyWarnsThatSavingTakesAWhileFromHalfAGibUp()
    {
        // docs/WAV-STEMS.md names this limit (512 MiB); a change of the limit has to change that page too.
        var below = new StemExport.Summary(4, TimeSpan.FromSeconds(34), 536_870_911L);
        var atTheLimit = below with { Bytes = 536_870_912L };
        foreach (var language in Languages)
        {
            var slow = InLanguage(language, () => Text.Get("StemExportSlow"));
            Assert.IsFalse(InLanguage(language, () => StemExport.ConfirmText(below)).Contains(slow), $"{language}: below the limit nothing is said about the time");
            Assert.IsTrue(InLanguage(language, () => StemExport.ConfirmText(atTheLimit)).EndsWith(slow, StringComparison.Ordinal), $"{language}: from the limit up it is said last");
        }
        // The largest project the app allows (1,024 bars, eight lanes) is far above it; the default project is far below.
        var largest = StemExport.Summarize(ScheduleCompiler.Compile(new PulseDocument { Lanes = [.. Enumerable.Range(0, 8).Select(_ => new RhythmLane())] }, 48000, 1024, true));
        Assert.AreEqual(9, largest.Tracks);
        Assert.IsTrue(largest.Bytes > 6 * StemExport.SlowBytes, $"{largest.Bytes}");
        Assert.IsTrue(StemExport.Summarize(ScheduleCompiler.Compile(new PulseDocument(), 48000, 16, true)).Bytes < StemExport.SlowBytes / 50);
    }

    [TestMethod]
    public void TheConfirmationNamesTheTracksTheLengthAndTheSizeInEveryLanguage()
    {
        var plan = ScheduleCompiler.Compile(new PulseDocument { Lanes = [new(), new(), new()] }, 48000, 16, true);
        var summary = StemExport.Summarize(plan);
        Assert.AreEqual(4, summary.Tracks);
        foreach (var language in Languages)
        {
            var text = InLanguage(language, () => StemExport.ConfirmText(summary));
            StringAssert.Contains(text, "4", language);
            StringAssert.Contains(text, "0:34", language);
            StringAssert.Contains(text, InLanguage(language, () => StemExport.FormatSize(summary.Bytes, Text.Culture)), language);
            StringAssert.Contains(text, "ZIP", language);
            Assert.IsFalse(text.Contains('{') || text.Contains('}'), $"{language}: a placeholder is left in '{text}'");
        }
        // The size is the one the exporter will write: 4 tracks of 34 seconds at 48,000 Hz.
        Assert.AreEqual(4 * 34 * 48000L * 4, summary.Bytes);
    }
}
