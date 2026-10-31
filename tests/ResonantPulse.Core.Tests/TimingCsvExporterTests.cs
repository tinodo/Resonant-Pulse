using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TimingCsvExporterTests
{
    private static readonly double[] KnownExpected = [0, 1, 2, 3, 4];
    private static readonly double[] KnownObserved = [0.015625, 1.03125, 2.046875, 3.0625, 4.75];

    /// <summary>Four presses 15.625, 31.25, 46.875 and 62.5 ms late (all exact in binary), a click that nobody pressed and a press that no click wanted.</summary>
    private static TimingSession Known() => TimingTestKit.Session(KnownExpected, KnownObserved, 0.125);

    private static List<string[]> Rows(TimingSession session)
    {
        using var stream = new MemoryStream();
        TimingCsvExporter.WriteAsync(stream, session).GetAwaiter().GetResult();
        return TimingTestKit.Strict(stream.ToArray());
    }

    private static string Value(List<string[]> rows, string key) => TimingTestKit.Value(rows, key);

    [TestMethod]
    public async Task Export_WritesExactlyThisFileForAKnownRun()
    {
        using var temp = new TempDirectory();
        var path = temp.File("known.csv");
        await TimingCsvExporter.ExportAsync(path, Known());
        // The one number that comes from a formula (1.4826 times the median absolute deviation, 15.625 ms) is worked out here.
        var spread = (1.4826 * 15.625).ToString("R", CultureInfo.InvariantCulture);
        string[] lines =
        [
            "\"record_type\",\"expected_index\",\"observed_index\",\"expected_seconds\",\"observed_seconds\",\"offset_milliseconds\",\"name\",\"value\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"schema_version\",\"1\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"source\",\"keyboard-ui-arrival\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"limitation\",\"Key presses as the app received them, compared with the scheduled clicks. Keyboard, system, speaker and device delays are not corrected for. Not a recording.\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"clock_reference\",\"start_frame and end_frame are audio device frames the app had processed (not a calibrated acoustic position); observed_seconds come from the system timer, counted from the start of the check.\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"index_base\",\"0\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"window_policy\",\"start frame included, end frame excluded; expected_seconds and observed_seconds are counted from the start of the check.\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"completed_at_utc\",\"2026-10-31T00:46:32.2420000+00:00\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"project_name_json\",\"\"\"Practice\"\"\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"target_name_json\",\"\"\"Main beat\"\"\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"label_encoding\",\"JSON string; decode once to recover the exact name.\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"target_lane\",\"-1\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"sample_rate\",\"8000\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"start_frame\",\"1234\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"end_frame\",\"41234\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"duration_seconds\",\"5\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"tolerance_seconds\",\"0.125\"",
            "\"metadata\",\"\",\"\",\"\",\"\",\"\",\"includes_silent_targets\",\"true\"",
            "\"summary\",\"\",\"\",\"\",\"\",\"\",\"expected_count\",\"5\"",
            "\"summary\",\"\",\"\",\"\",\"\",\"\",\"observed_count\",\"5\"",
            "\"summary\",\"\",\"\",\"\",\"\",\"\",\"matched\",\"4\"",
            "\"summary\",\"\",\"\",\"\",\"\",\"\",\"missed\",\"1\"",
            "\"summary\",\"\",\"\",\"\",\"\",\"\",\"extra\",\"1\"",
            "\"summary\",\"\",\"\",\"\",\"\",\"\",\"median_offset_milliseconds\",\"39.0625\"",
            $"\"summary\",\"\",\"\",\"\",\"\",\"\",\"spread_milliseconds\",\"{spread}\"",
            "\"summary\",\"\",\"\",\"\",\"\",\"\",\"coverage\",\"0.8\"",
            "\"summary\",\"\",\"\",\"\",\"\",\"\",\"ambiguous_expected\",\"0\"",
            "\"summary\",\"\",\"\",\"\",\"\",\"\",\"drift_milliseconds_per_second\",\"15.625\"",
            "\"match\",\"0\",\"0\",\"0\",\"0.015625\",\"15.625\",\"\",\"\"",
            "\"match\",\"1\",\"1\",\"1\",\"1.03125\",\"31.25\",\"\",\"\"",
            "\"match\",\"2\",\"2\",\"2\",\"2.046875\",\"46.875\",\"\",\"\"",
            "\"match\",\"3\",\"3\",\"3\",\"3.0625\",\"62.5\",\"\",\"\"",
            "\"miss\",\"4\",\"\",\"4\",\"\",\"\",\"\",\"\"",
            "\"extra\",\"\",\"4\",\"\",\"4.75\",\"\",\"\",\"\""
        ];
        var expected = new List<byte> { 0xEF, 0xBB, 0xBF };
        foreach (var line in lines) expected.AddRange(new UTF8Encoding(false).GetBytes(line + "\r\n"));
        var actual = await File.ReadAllBytesAsync(path);
        // Compared as text first so that a difference is readable, then byte for byte.
        Assert.AreEqual(string.Join("\n", lines), string.Join("\n", Encoding.UTF8.GetString(actual, 3, actual.Length - 3)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)));
        Check.Sequence(expected, actual);
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_IndependentReadersAgreeOnEveryRowAndItsOriginalIndices()
    {
        using var temp = new TempDirectory();
        // Out of order on purpose: indices are positions in the lists as they were given, not in time.
        var session = TimingTestKit.Session([2, 0, 1, 4], [0.02, 2.01, 3], 0.1);
        var path = temp.File("report.csv");
        await TimingCsvExporter.ExportAsync(path, session);
        var rows = TimingTestKit.Read(path);
        Assert.IsTrue(rows.All(row => row.Length == 8));
        Check.Sequence(new[] { "record_type", "expected_index", "observed_index", "expected_seconds", "observed_seconds", "offset_milliseconds", "name", "value" }, rows[0]);
        var matches = rows.Where(row => row[0] == "match").ToArray();
        Assert.AreEqual(2, matches.Length);
        Check.Sequence(new[] { "1", "0" }, matches.Select(row => row[1]));
        Check.Sequence(new[] { "0", "1" }, matches.Select(row => row[2]));
        Check.Sequence(new[] { 0d, 2d }, matches.Select(row => TimingTestKit.Number(row[3])));
        Check.Sequence(new[] { 0.02, 2.01 }, matches.Select(row => TimingTestKit.Number(row[4])));
        for (var index = 0; index < matches.Length; index++)
            Assert.AreEqual(session.Report.Matches[index].OffsetMilliseconds, TimingTestKit.Number(matches[index][5]));
        Check.Sequence(new[] { "2", "3" }, rows.Where(row => row[0] == "miss").Select(row => row[1]));
        Check.Sequence(new[] { 1d, 4d }, rows.Where(row => row[0] == "miss").Select(row => TimingTestKit.Number(row[3])));
        Assert.IsTrue(rows.Where(row => row[0] == "miss").All(row => row[2] == "" && row[4] == "" && row[5] == ""));
        var extra = rows.Single(row => row[0] == "extra");
        Assert.AreEqual("2", extra[2]);
        Assert.AreEqual(3d, TimingTestKit.Number(extra[4]));
        Assert.IsTrue(extra[1] == "" && extra[3] == "" && extra[5] == "");
        Assert.AreEqual("2", Value(rows, "matched"));
        Assert.AreEqual("2", Value(rows, "missed"));
        Assert.AreEqual("1", Value(rows, "extra"));
        Assert.AreEqual("0.5", Value(rows, "coverage"));
        Assert.AreEqual("4", Value(rows, "expected_count"));
        Assert.AreEqual("3", Value(rows, "observed_count"));
        Assert.AreEqual("", Value(rows, "drift_milliseconds_per_second"));
        // The reader that ships with .NET (not written for this file) reads the same rows.
        using var parser = new TextFieldParser(new StringReader(await File.ReadAllTextAsync(path)))
        {
            TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var bcl = new List<string[]>();
        while (!parser.EndOfData) bcl.Add(parser.ReadFields()!);
        Assert.AreEqual(rows.Count, bcl.Count);
        for (var index = 0; index < rows.Count; index++) Check.Sequence(rows[index], bcl[index]);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_StatesTheContextOfTheRunInTheOrderMetadataSummaryDetails()
    {
        using var temp = new TempDirectory();
        var context = new TimingSessionContext("Étude no. 3", "Lane 3", 2, 48000, 96_000, 96_000 + 48000 * 12, false);
        var session = new TimingSession(context, [0.5, 1.5], [0.5, 1.5], 0.05, TimingTestKit.Completed);
        await TimingCsvExporter.ExportAsync(temp.File("context.csv"), session);
        var rows = TimingTestKit.Read(temp.File("context.csv"));
        Assert.AreEqual("2", Value(rows, "target_lane"));
        Assert.AreEqual("48000", Value(rows, "sample_rate"));
        Assert.AreEqual("96000", Value(rows, "start_frame"));
        Assert.AreEqual((96_000 + 48000 * 12).ToString(CultureInfo.InvariantCulture), Value(rows, "end_frame"));
        Assert.AreEqual("12", Value(rows, "duration_seconds"));
        Assert.AreEqual("0.05", Value(rows, "tolerance_seconds"));
        Assert.AreEqual("false", Value(rows, "includes_silent_targets"));
        Assert.AreEqual("0", Value(rows, "index_base"));
        Assert.AreEqual("1", Value(rows, "schema_version"));
        Assert.AreEqual("2026-10-31T00:46:32.2420000+00:00", Value(rows, "completed_at_utc"));
        Assert.AreEqual("Étude no. 3", JsonSerializer.Deserialize<string>(Value(rows, "project_name_json")));
        Assert.AreEqual("Lane 3", JsonSerializer.Deserialize<string>(Value(rows, "target_name_json")));
        // Metadata, then the summary, then the details, in that order and nothing in between.
        var types = rows.Skip(1).Select(row => row[0]).ToArray();
        var firstSummary = Array.IndexOf(types, "summary");
        var firstDetail = Array.FindIndex(types, type => type is "match" or "miss" or "extra");
        Assert.IsTrue(types.Take(firstSummary).All(type => type == "metadata"));
        Assert.IsTrue(types.Skip(firstSummary).Take(firstDetail - firstSummary).All(type => type == "summary"));
        Assert.IsTrue(types.Skip(firstDetail).All(type => type is "match" or "miss" or "extra"));
        // Every metadata and summary row has its name once.
        var names = rows.Where(row => row[0] is "metadata" or "summary").Select(row => row[6]).ToArray();
        Assert.AreEqual(names.Length, names.Distinct().Count());
        Assert.IsTrue(rows.Where(row => row[0] is "metadata" or "summary").All(row => row[1..6].All(cell => cell == "")));
    }

    [TestMethod]
    public async Task Export_DuplicateTimesRetainDistinctIndicesAndNegativeOffsets()
    {
        using var temp = new TempDirectory();
        var session = TimingTestKit.Session([2, 1, 1, 4], [1, 1, 1.99], 0.1);
        var path = temp.File("duplicates.csv");
        await TimingCsvExporter.ExportAsync(path, session);
        var rows = TimingTestKit.Read(path);
        var matches = rows.Where(row => row[0] == "match").ToArray();
        Check.Sequence(new[] { "1", "2", "0" }, matches.Select(row => row[1]));
        Check.Sequence(new[] { "0", "1", "2" }, matches.Select(row => row[2]));
        Assert.IsTrue(TimingTestKit.Number(matches[2][5]) < 0);
        Assert.AreEqual(session.Report.Matches[2].OffsetMilliseconds, TimingTestKit.Number(matches[2][5]));
        Assert.AreEqual("3", rows.Single(row => row[0] == "miss")[1]);
        Assert.AreEqual(0, rows.Count(row => row[0] == "extra"));
        Assert.AreEqual("2", Value(rows, "ambiguous_expected"));
    }

    [TestMethod]
    [DataRow("=SUM(1,2)")]
    [DataRow("+1+2")]
    [DataRow("-1+2")]
    [DataRow("@SUM(1,2)")]
    [DataRow("=cmd|' /C calc'!A0")]
    [DataRow("\t=1+1")]
    [DataRow("\r=1+1")]
    [DataRow("  =1+1")]
    [DataRow("a\r\nb\nc\rd")]
    [DataRow("say \"hi\", \"\"there\"\"")]
    [DataRow("one,two;three\tfour")]
    [DataRow("\0\u0001\u001f\u007f\u0085\u2028\u2029")]
    [DataRow("été, Ärger, 日本語, Привет, ñ, \U0001F941")]
    [DataRow("back\\slash \\n not a newline \\u0041")]
    public async Task Export_UserLabelsCannotInjectRecordsOrBecomeFormulaCells(string name)
    {
        using var temp = new TempDirectory();
        var plain = Rows(TimingTestKit.Session([1], [1.01]));
        var session = TimingTestKit.Session([1], [1.01], 0.1, name, name);
        var path = temp.File("labels.csv");
        await TimingCsvExporter.ExportAsync(path, session);
        var rows = TimingTestKit.Read(path);
        foreach (var key in new[] { "project_name_json", "target_name_json" })
        {
            var cell = Value(rows, key);
            Assert.AreEqual('"', cell[0], "a label cell starts with a quote, so it is text and never a formula");
            Assert.AreEqual(name, JsonSerializer.Deserialize<string>(cell), key);
        }
        // A label cannot add, remove or move a record or a column.
        Assert.AreEqual(plain.Count, rows.Count);
        Assert.IsTrue(rows.All(row => row.Length == 8));
        Check.Sequence(plain.Select(row => row[0]), rows.Select(row => row[0]));
        // Nothing in the file can be read as a formula or carries a line break.
        var text = await File.ReadAllTextAsync(path);
        Assert.IsFalse(text.Replace("\r\n", "").Any(c => c is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029'), "a raw line break is in the file");
        foreach (var cell in rows.SelectMany(row => row).Where(cell => cell.Length > 0))
        {
            Assert.IsFalse("=+@\t\r".Contains(cell[0]), $"a cell starts like a formula: {cell}");
            if (cell[0] == '-') Assert.IsTrue(double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out _), $"a cell starts with a minus but is not a number: {cell}");
        }
        // The reader that ships with .NET sees the same fields.
        using var parser = new TextFieldParser(new StringReader(text)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var bcl = new List<string[]>();
        while (!parser.EndOfData) bcl.Add(parser.ReadFields()!);
        Assert.AreEqual(rows.Count, bcl.Count);
        for (var index = 0; index < rows.Count; index++) Check.Sequence(rows[index], bcl[index]);
    }

    [TestMethod]
    public void Export_EveryCharacterOfAnyNameComesBackExactlyFromAJsonReader()
    {
        // The whole Basic Multilingual Plane except the halves of surrogate pairs, in names of the longest length allowed.
        var characters = Enumerable.Range(0, 0x10000).Where(code => code is < 0xD800 or > 0xDFFF).Select(code => (char)code).ToArray();
        var chunks = characters.Chunk(TimingSession.MaximumNameLength).Select(chunk => new string(chunk)).ToList();
        // Characters outside the plane, as the pairs they are written with, and a mix.
        chunks.Add("\U00010000\U0001F941\U0010FFFF");
        chunks.Add("e\u0301 \u200D\uFE0F \uFEFF");
        foreach (var chunk in chunks)
        {
            var rows = Rows(TimingTestKit.Session([], [], 0.1, chunk, chunk));
            Assert.AreEqual(chunk, JsonSerializer.Deserialize<string>(Value(rows, "project_name_json")));
            Assert.AreEqual(chunk, JsonSerializer.Deserialize<string>(Value(rows, "target_name_json")));
        }
    }

    [TestMethod]
    public void Export_TextThatTextToolsSplitLinesAtIsWrittenAsEscapes()
    {
        using var stream = new MemoryStream();
        TimingCsvExporter.WriteAsync(stream, TimingTestKit.Session([], [], 0.1, "a\u0085b\u2028c\u2029d\u007fe", "é")).GetAwaiter().GetResult();
        var text = Encoding.UTF8.GetString(stream.ToArray());
        foreach (var raw in new[] { '\u0085', '\u2028', '\u2029', '\u007f' }) Assert.IsFalse(text.Contains(raw), $"U+{(int)raw:X4} is written raw");
        StringAssert.Contains(text, "\\u0085");
        StringAssert.Contains(text, "\\u2028");
        StringAssert.Contains(text, "\\u2029");
        // Letters are not escaped: a person who opens the file reads the name, not a code.
        var rows = TimingTestKit.Strict(stream.ToArray());
        Assert.AreEqual("\"é\"", Value(rows, "target_name_json"));
    }

    [TestMethod]
    public async Task Export_ZeroMatchesAndMissingTargetsLeaveUnavailableMetricsBlank()
    {
        using var temp = new TempDirectory();
        var none = TimingTestKit.Session([1], [2], 0.01);
        await TimingCsvExporter.ExportAsync(temp.File("none.csv"), none);
        var rows = TimingTestKit.Read(temp.File("none.csv"));
        foreach (var key in new[] { "median_offset_milliseconds", "spread_milliseconds", "drift_milliseconds_per_second" })
            Assert.AreEqual("", Value(rows, key), key);
        Assert.AreEqual("0", Value(rows, "coverage"));
        Assert.AreEqual("0", Value(rows, "matched"));
        Assert.AreEqual("1", Value(rows, "missed"));
        Assert.AreEqual("1", Value(rows, "extra"));
        Assert.AreEqual(1, rows.Count(row => row[0] == "miss"));
        Assert.AreEqual(1, rows.Count(row => row[0] == "extra"));
        Assert.AreEqual(0, rows.Count(row => row[0] == "match"));

        // Presses but no clicks to compare them with: coverage has nothing to be a share of.
        await TimingCsvExporter.ExportAsync(temp.File("no-targets.csv"), TimingTestKit.Session([], [2], 0.01));
        rows = TimingTestKit.Read(temp.File("no-targets.csv"));
        Assert.AreEqual("", Value(rows, "coverage"));
        Assert.AreEqual("0", Value(rows, "expected_count"));
        Assert.AreEqual("1", Value(rows, "extra"));

        // Nothing at all, in a window that has no length, with a tolerance of zero.
        var empty = new TimingSession(TimingTestKit.Context() with { EndFrame = 1234 }, [], [], 0, TimingTestKit.Completed);
        await TimingCsvExporter.ExportAsync(temp.File("empty.csv"), empty);
        rows = TimingTestKit.Read(temp.File("empty.csv"));
        Assert.AreEqual("0", Value(rows, "duration_seconds"));
        Assert.AreEqual("0", Value(rows, "tolerance_seconds"));
        Assert.AreEqual("", Value(rows, "coverage"));
        Assert.AreEqual(0, rows.Count(row => row[0] is "match" or "miss" or "extra"));
        Assert.AreEqual(1 + 17 + 10, rows.Count);
    }

    [TestMethod]
    public async Task Export_DriftIsBlankWithoutATimeSpreadButNumericWhenMeasuredAsZero()
    {
        using var temp = new TempDirectory();
        await TimingCsvExporter.ExportAsync(temp.File("coincident.csv"), TimingTestKit.Session([1, 1, 1, 1], [1, 1, 1, 1], 0));
        Assert.AreEqual("", Value(TimingTestKit.Read(temp.File("coincident.csv")), "drift_milliseconds_per_second"));
        await TimingCsvExporter.ExportAsync(temp.File("three.csv"), TimingTestKit.Session([0, 1, 2], [0, 1, 2], 0));
        Assert.AreEqual("", Value(TimingTestKit.Read(temp.File("three.csv")), "drift_milliseconds_per_second"));
        await TimingCsvExporter.ExportAsync(temp.File("separated.csv"), TimingTestKit.Session([0, 1, 2, 3], [0, 1, 2, 3], 0));
        Assert.AreEqual("0", Value(TimingTestKit.Read(temp.File("separated.csv")), "drift_milliseconds_per_second"));
        var overflow = new TimingSession(TimingTestKit.Context(), [0, 1e-320, 2e-320, 3e-320], [1, 1.01, 1.02, 1.03], 2, TimingTestKit.Completed);
        await TimingCsvExporter.ExportAsync(temp.File("overflow.csv"), overflow);
        var rows = TimingTestKit.Read(temp.File("overflow.csv"));
        Assert.AreEqual("", Value(rows, "drift_milliseconds_per_second"));
        Assert.AreEqual("4", Value(rows, "matched"));
    }

    [TestMethod]
    public async Task Export_WritesTheSameBytesWhateverTheCultureOfTheThreadThatWritesIt()
    {
        using var temp = new TempDirectory();
        var reference = temp.File("invariant.csv");
        await TimingCsvExporter.ExportAsync(reference, Known());
        var expected = await File.ReadAllBytesAsync(reference);
        var before = CultureInfo.CurrentCulture;
        var beforeUi = CultureInfo.CurrentUICulture;
        var tried = new List<string>();
        try
        {
            // Comma as the decimal mark, a space or a dot between thousands, other digits and another calendar.
            foreach (var name in new[] { "nl-NL", "fr-FR", "de-DE", "es-ES", "ar-SA", "th-TH", "fa-IR" })
            {
                CultureInfo culture;
                try { culture = CultureInfo.GetCultureInfo(name); }
                catch (CultureNotFoundException) { continue; }
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                var path = temp.File($"culture-{name}.csv");
                await TimingCsvExporter.ExportAsync(path, Known());
                Check.Sequence(expected, await File.ReadAllBytesAsync(path));
                tried.Add(name);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
            CultureInfo.CurrentUICulture = beforeUi;
        }
        // A machine without culture data would otherwise make this pass without trying anything.
        CollectionAssert.Contains(tried, "nl-NL");
        Assert.IsTrue(tried.Count >= 3, string.Join(", ", tried));
    }

    [TestMethod]
    public async Task Export_NumbersComeBackExactlyAsTheyWentIn()
    {
        using var temp = new TempDirectory();
        double[] expected = [0.1 + 0.2, 1.0 / 3, 1e-7, 123.456789012345678, 599.9999999999999];
        double[] observed = [0.1 + 0.2 + 1e-9, 1.0 / 3 - 0.0123456789, 1e-7 + 0.001, 123.456789012345678 + 0.5, 599.9999999999999 - 0.25];
        var context = TimingTestKit.Context() with { EndFrame = 1234 + 600L * 8000 };
        var session = new TimingSession(context, expected, observed, 1, TimingTestKit.Completed);
        Assert.AreEqual(5, session.Report.Matched);
        var path = temp.File("precision.csv");
        await TimingCsvExporter.ExportAsync(path, session);
        var rows = TimingTestKit.Read(path);
        var matches = rows.Where(row => row[0] == "match").ToArray();
        for (var index = 0; index < 5; index++)
        {
            var match = session.Report.Matches[index];
            Assert.AreEqual(match.ExpectedSeconds, TimingTestKit.Number(matches[index][3]), "expected_seconds");
            Assert.AreEqual(match.ObservedSeconds, TimingTestKit.Number(matches[index][4]), "observed_seconds");
            Assert.AreEqual(match.OffsetMilliseconds, TimingTestKit.Number(matches[index][5]), "offset_milliseconds");
        }
        Assert.AreEqual(session.Report.MedianOffsetMilliseconds, TimingTestKit.Number(Value(rows, "median_offset_milliseconds")));
        Assert.AreEqual(session.Report.SpreadMilliseconds, TimingTestKit.Number(Value(rows, "spread_milliseconds")));
        Assert.AreEqual(session.Report.Coverage, TimingTestKit.Number(Value(rows, "coverage")));
        Assert.AreEqual(session.Report.DriftMillisecondsPerSecond, TimingTestKit.Number(Value(rows, "drift_milliseconds_per_second")));
        Assert.AreEqual(600d, TimingTestKit.Number(Value(rows, "duration_seconds")));
        // A number never has a thousands separator or a comma, and a zero is never negative.
        Assert.IsTrue(rows.SelectMany(row => row[1..6]).All(cell => !cell.Contains(',') && !cell.Contains(' ') && cell != "-0"));
    }

    [TestMethod]
    public async Task Export_AFileIsBomUtf8WithCrlfAfterEveryRecordAndEveryFieldQuoted()
    {
        using var temp = new TempDirectory();
        var path = temp.File("shape.csv");
        await TimingCsvExporter.ExportAsync(path, Known());
        var bytes = await File.ReadAllBytesAsync(path);
        Check.Sequence(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        Check.Sequence(new byte[] { 0x0D, 0x0A }, bytes.TakeLast(2));
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.IsFalse(text.Replace("\r\n", "").Any(c => c is '\r' or '\n'));
        var lines = text.Split("\r\n");
        Assert.AreEqual("", lines[^1]);
        Assert.IsTrue(lines[..^1].All(line => line.Length > 1 && line[0] == '"' && line[^1] == '"'));
        Assert.AreEqual(1 + 17 + 10 + 4 + 1 + 1, lines.Length - 1);
        // Strict also fails on a bare quote, a missing quote or a field that is not followed by a comma or CRLF.
        Assert.AreEqual(lines.Length - 1, TimingTestKit.Strict(bytes).Count);
    }

    [TestMethod]
    public async Task Export_TheLargestRunTheAnalyzerTakesRoundTripsEveryRow()
    {
        using var temp = new TempDirectory();
        const int count = 1999;
        var context = TimingTestKit.Context() with { EndFrame = 1234 + 600L * 8000 };
        double[] expected = Enumerable.Range(0, count).Select(index => index * 0.25).ToArray();
        // Every fourth click is not pressed and every seventh press is a stray one.
        var observed = new List<double>();
        for (var index = 0; index < count; index++)
        {
            if (index % 4 != 3) observed.Add(expected[index] + 0.0078125);
            if (index % 7 == 0) observed.Add(expected[index] + 0.125 + 1e-3);
        }
        var session = new TimingSession(context, expected, observed.OrderBy(time => time).ToArray(), 0.05, TimingTestKit.Completed);
        var path = temp.File("largest.csv");
        await TimingCsvExporter.ExportAsync(path, session);
        var rows = TimingTestKit.Read(path);
        var matches = rows.Where(row => row[0] == "match").ToArray();
        var misses = rows.Where(row => row[0] == "miss").ToArray();
        var extras = rows.Where(row => row[0] == "extra").ToArray();
        Assert.AreEqual(session.Report.Matched, matches.Length);
        Assert.AreEqual(session.Report.Missed, misses.Length);
        Assert.AreEqual(session.Report.Extra, extras.Length);
        Assert.AreEqual(count, matches.Length + misses.Length);
        Assert.AreEqual(session.ObservedSeconds.Count, matches.Length + extras.Length);
        // Every click and every press appears once, in exactly one outcome, with its own time.
        var seenExpected = matches.Select(row => int.Parse(row[1], CultureInfo.InvariantCulture)).Concat(misses.Select(row => int.Parse(row[1], CultureInfo.InvariantCulture))).Order().ToArray();
        Check.Sequence(Enumerable.Range(0, count), seenExpected);
        var seenObserved = matches.Select(row => int.Parse(row[2], CultureInfo.InvariantCulture)).Concat(extras.Select(row => int.Parse(row[2], CultureInfo.InvariantCulture))).Order().ToArray();
        Check.Sequence(Enumerable.Range(0, session.ObservedSeconds.Count), seenObserved);
        foreach (var row in matches.Concat(misses))
            Assert.AreEqual(expected[int.Parse(row[1], CultureInfo.InvariantCulture)], TimingTestKit.Number(row[3]));
        foreach (var row in matches.Concat(extras))
            Assert.AreEqual(session.ObservedSeconds[int.Parse(row[2], CultureInfo.InvariantCulture)], TimingTestKit.Number(row[4]));
        temp.NoTemps();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Export_PreCancellationRetainsExistingDestinationOrAbsence(bool existing)
    {
        using var temp = new TempDirectory();
        var path = temp.File("cancel.csv");
        if (existing) await File.WriteAllTextAsync(path, "retained");
        await Check.ThrowsAsync<OperationCanceledException>(() =>
            TimingCsvExporter.ExportAsync(path, TimingTestKit.Session([], []), new CancellationToken(true)));
        if (existing) Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        else Assert.IsFalse(File.Exists(path));
        Assert.AreEqual(existing ? 1 : 0, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Export_CancelledWhileWritingNeverLeavesAHalfWrittenFile(bool existing)
    {
        using var temp = new TempDirectory();
        var path = temp.File("midway.csv");
        if (existing) await File.WriteAllTextAsync(path, "retained");
        // A hundred thousand clicks that nobody pressed: a file of megabytes, written row by row.
        var context = TimingTestKit.Context() with { EndFrame = 1234 + 600L * 8000 };
        var session = new TimingSession(context, new double[TimingAnalyzer.MaximumTimestamps], [], 0.1, TimingTestKit.Completed);
        using var cancel = new CancellationTokenSource();
        var export = Task.Run(() => TimingCsvExporter.ExportAsync(path, session, cancel.Token));
        cancel.Cancel();
        var cancelled = false;
        try
        {
            await export;
            // The export won the race: then the file is whole.
            var rows = TimingTestKit.Read(path);
            Assert.AreEqual(TimingAnalyzer.MaximumTimestamps, rows.Count(row => row[0] == "miss"));
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            if (existing) Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
            else Assert.IsFalse(File.Exists(path));
        }
        Assert.AreEqual(!cancelled || existing ? 1 : 0, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_InvalidDestinationsAndNullSessionRetainExistingFiles()
    {
        using var temp = new TempDirectory();
        var retained = temp.File("retained.csv");
        await File.WriteAllTextAsync(retained, "retained");
        var session = TimingTestKit.Session([], []);
        foreach (var path in new[] { "", "   ", "relative.csv", temp.File("wildcard?.csv"), temp.File("star*.csv"), temp.Root })
            await Check.ThrowsAsync<ArgumentException>(() => TimingCsvExporter.ExportAsync(path, session));
        await Check.ThrowsAsync<DirectoryNotFoundException>(() => TimingCsvExporter.ExportAsync(Path.Combine(temp.Root, "missing", "x.csv"), session));
        await Check.ThrowsAsync<ArgumentNullException>(() => TimingCsvExporter.ExportAsync(retained, null!));
        await Check.ThrowsAsync<ArgumentNullException>(() => TimingCsvExporter.WriteAsync(new MemoryStream(), null!));
        await Check.ThrowsAsync<ArgumentNullException>(() => TimingCsvExporter.WriteAsync(null!, session));
        Assert.AreEqual("retained", await File.ReadAllTextAsync(retained));
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_ReplacesAnExistingFileCompletely()
    {
        using var temp = new TempDirectory();
        var path = temp.File("again.csv");
        await File.WriteAllTextAsync(path, new string('x', 100_000));
        await TimingCsvExporter.ExportAsync(path, Known());
        var rows = TimingTestKit.Read(path);
        Assert.AreEqual("4", Value(rows, "matched"));
        // A second export of a smaller run leaves no tail of the first.
        await TimingCsvExporter.ExportAsync(path, TimingTestKit.Session([], []));
        rows = TimingTestKit.Read(path);
        Assert.AreEqual("0", Value(rows, "matched"));
        Assert.AreEqual(1 + 17 + 10, rows.Count);
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_ConcurrentReplacementRetainsOneCompleteDistinguishableRun()
    {
        using var temp = new TempDirectory();
        var path = temp.File("shared.csv");
        var sessions = Enumerable.Range(1, 4).Select(index =>
            TimingTestKit.Session([index * 0.25], [index * 0.25 + 0.01], 0.1, "Run " + index)).ToArray();
        await Task.WhenAll(sessions.Select(session => TimingCsvExporter.ExportAsync(path, session)));
        var rows = TimingTestKit.Read(path);
        var name = JsonSerializer.Deserialize<string>(Value(rows, "project_name_json"))!;
        var winner = sessions.Single(session => session.Context.ProjectName == name);
        // One whole run, not a mix of two: its click and its press are the winner's.
        Assert.AreEqual(winner.ExpectedSeconds[0], TimingTestKit.Number(rows.Single(row => row[0] == "match")[3]));
        Assert.AreEqual(winner.ObservedSeconds[0], TimingTestKit.Number(rows.Single(row => row[0] == "match")[4]));
        Assert.AreEqual("1", Value(rows, "matched"));
        Assert.IsTrue(rows.All(row => row.Length == 8));
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_ConcurrentExportsToDifferentFilesDoNotDisturbEachOther()
    {
        using var temp = new TempDirectory();
        var sessions = Enumerable.Range(1, 6).Select(index => TimingTestKit.Session([index * 0.25], [index * 0.25 + 0.01], 0.1, "Run " + index)).ToArray();
        await Task.WhenAll(sessions.Select((session, index) => TimingCsvExporter.ExportAsync(temp.File($"run-{index}.csv"), session)));
        for (var index = 0; index < sessions.Length; index++)
            Assert.AreEqual(sessions[index].Context.ProjectName, JsonSerializer.Deserialize<string>(Value(TimingTestKit.Read(temp.File($"run-{index}.csv")), "project_name_json")));
        Assert.AreEqual(sessions.Length, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_WindowsSharingFailureRetainsOldContentsAndAllowsRetry()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows delete-sharing enforcement.");
        using var temp = new TempDirectory();
        var path = temp.File("locked.csv");
        await File.WriteAllTextAsync(path, "retained");
        var session = TimingTestKit.Session([1], [1.01]);
        Exception? failure = null;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { await TimingCsvExporter.ExportAsync(path, session); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failure = error; }
        }
        Assert.IsNotNull(failure, "the export of a file that is open elsewhere did not fail");
        Assert.IsTrue((failure.HResult & 0xFFFF) is 5 or 32 or 33, failure.Message);
        Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
        await TimingCsvExporter.ExportAsync(path, session);
        Assert.AreEqual("1", Value(TimingTestKit.Read(path), "matched"));
    }

    [TestMethod]
    public async Task Export_ReadOnlyDestinationKeepsTheOldFileAndWorksOnceItIsWritable()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires the Windows read-only attribute to block replacement.");
        using var temp = new TempDirectory();
        var path = temp.File("readonly.csv");
        await File.WriteAllTextAsync(path, "retained");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var session = TimingTestKit.Session([1], [1.01]);
        try
        {
            var failure = await Check.ThrowsAsync<Exception>(() => TimingCsvExporter.ExportAsync(path, session));
            Assert.IsTrue(failure is IOException or UnauthorizedAccessException, failure.GetType().Name + ": " + failure.Message);
            Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
            Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
            temp.NoTemps();
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        await TimingCsvExporter.ExportAsync(path, session);
        Assert.AreEqual("1", Value(TimingTestKit.Read(path), "matched"));
    }
}
