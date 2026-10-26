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
    private static TimingSessionContext Context(string project = "Practice", string target = "Main") =>
        new(project, target, -1, 8000, 1234, 41234, true);

    [TestMethod]
    public void Session_FreezesArraysStatisticsContextAndReadOnlyMatches()
    {
        double[] expected = [1, 2], observed = [1.01, 2.02];
        var context = Context();
        var session = new TimingSession(context, expected, observed, 0.1);
        expected[0] = 100; observed[0] = 200;
        context = context with { ProjectName = "Changed" };
        Assert.AreEqual(1d, session.ExpectedSeconds[0]);
        Assert.AreEqual(1.01, session.ObservedSeconds[0]);
        Assert.AreEqual("Practice", session.Context.ProjectName);
        Assert.AreEqual(2, session.Report.Matched);
        Assert.AreEqual(5d, session.DurationSeconds);
        Check.Throws<NotSupportedException>(() => ((IList<double>)session.ExpectedSeconds)[0] = 9);
        Check.Throws<NotSupportedException>(() => ((IList<TimingMatch>)session.Report.Matches).Clear());
    }

    [TestMethod]
    public async Task Export_IndependentCsvParserReadsAllEightColumnsAndOriginalIndexOutcomes()
    {
        using var temp = new TempDirectory();
        var session = new TimingSession(Context(), [2, 0, 1, 4], [0.02, 2.01, 3], 0.1);
        string path = temp.File("report.csv");
        await TimingCsvExporter.ExportAsync(path, session);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        Check.Sequence(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        var rows = await ReadAsync(path);
        Assert.IsTrue(rows.All(row => row.Length == 8));
        Check.Sequence(new[] { "record_type", "expected_index", "observed_index", "expected_seconds",
            "observed_seconds", "offset_milliseconds", "name", "value" }, rows[0]);
        var matches = rows.Where(row => row[0] == "match").ToArray();
        Assert.AreEqual(2, matches.Length);
        Check.Sequence(new[] { "1", "0" }, matches.Select(row => row[1]));
        Check.Sequence(new[] { "0", "1" }, matches.Select(row => row[2]));
        for (int index = 0; index < matches.Length; index++)
            Assert.AreEqual(session.Report.Matches[index].OffsetMilliseconds,
                double.Parse(matches[index][5], CultureInfo.InvariantCulture));
        Check.Sequence(new[] { "2", "3" }, rows.Where(row => row[0] == "miss").Select(row => row[1]));
        Assert.AreEqual("2", rows.Single(row => row[0] == "extra")[2]);
        Assert.AreEqual("2", Value(rows, "matched"));
        Assert.AreEqual("2", Value(rows, "missed"));
        Assert.AreEqual("1", Value(rows, "extra"));
        Assert.AreEqual("0.5", Value(rows, "coverage"));
        Assert.AreEqual("", Value(rows, "drift_milliseconds_per_second"));
        Assert.AreEqual("keyboard-ui-arrival", Value(rows, "source"));
        Assert.AreEqual("1234", Value(rows, "start_frame"));
        Assert.AreEqual("41234", Value(rows, "end_frame"));
        Assert.AreEqual("true", Value(rows, "includes_silent_targets"));
        Assert.AreEqual("0.1", Value(rows, "tolerance_seconds"));
        StringAssert.Contains(Value(rows, "limitation"), "not calibrated");
        string text = Encoding.UTF8.GetString(bytes);
        Assert.IsFalse(text.Replace("\r\n", "").Contains('\n'));
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
    }

    [TestMethod]
    [DataRow("=SUM(1,2)")]
    [DataRow("+1+2")]
    [DataRow("-1+2")]
    [DataRow("@SUM(1,2)")]
    [DataRow(" \t=1+1\r\n\"été\"\0")]
    public async Task Export_JsonEncodedUserLabelsCannotInjectRecordsOrBecomeFormulaCells(string name)
    {
        using var temp = new TempDirectory();
        var session = new TimingSession(Context(name, name), [], [], 0.1);
        string path = temp.File("labels.csv");
        await TimingCsvExporter.ExportAsync(path, session);
        var rows = await ReadAsync(path);
        foreach (string key in new[] { "project_name_json", "target_name_json" })
        {
            string cell = Value(rows, key);
            Assert.AreEqual('"', cell[0]);
            Assert.AreEqual(name, JsonSerializer.Deserialize<string>(cell));
        }
        Assert.IsTrue(rows.All(row => row.Length == 8));
        Assert.AreEqual(0, rows.Count(row => row[0] is "match" or "miss" or "extra"));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_ZeroMatchesAndMissingTargetsLeaveUnavailableMetricsBlank()
    {
        using var temp = new TempDirectory();
        var noMatch = new TimingSession(Context(), [1], [2], 0.01);
        await TimingCsvExporter.ExportAsync(temp.File("none.csv"), noMatch);
        var rows = await ReadAsync(temp.File("none.csv"));
        foreach (string key in new[] { "median_offset_milliseconds", "spread_milliseconds", "drift_milliseconds_per_second" })
            Assert.AreEqual("", Value(rows, key));
        Assert.AreEqual("0", Value(rows, "coverage"));
        var noTargets = new TimingSession(Context(), [], [2], 0.01);
        await TimingCsvExporter.ExportAsync(temp.File("no-targets.csv"), noTargets);
        Assert.AreEqual("", Value(await ReadAsync(temp.File("no-targets.csv")), "coverage"));
        var empty = new TimingSession(Context() with { EndFrame = 1234 }, [], [], 0);
        await TimingCsvExporter.ExportAsync(temp.File("empty.csv"), empty);
        Assert.AreEqual("0", Value(await ReadAsync(temp.File("empty.csv")), "duration_seconds"));
    }

    [TestMethod]
    public async Task Export_RoundTripNumericPrecisionIsInvariantUnderCommaDecimalCulture()
    {
        using var temp = new TempDirectory();
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
            var session = new TimingSession(Context(), [0, 1, 2, 3], [0.001, 1.012345678901, 2.02, 3.03], 0.1);
            await TimingCsvExporter.ExportAsync(temp.File("culture.csv"), session);
            var rows = await ReadAsync(temp.File("culture.csv"));
            Assert.AreEqual("0.1", Value(rows, "tolerance_seconds"));
            foreach (var row in rows.Where(row => row[0] == "match"))
            {
                int index = int.Parse(row[2], CultureInfo.InvariantCulture);
                Assert.AreEqual(session.ObservedSeconds[index], double.Parse(row[4], CultureInfo.InvariantCulture));
            }
            Assert.AreEqual(session.Report.DriftMillisecondsPerSecond,
                double.Parse(Value(rows, "drift_milliseconds_per_second"), CultureInfo.InvariantCulture));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [TestMethod]
    public void Session_InvalidContextWindowTimestampsAndNumericRangeAreRejected()
    {
        foreach (var context in new[] { Context() with { ProjectName = null! }, Context() with { TargetName = new string('x', 257) },
            Context() with { LaneIndex = -2 }, Context() with { LaneIndex = 8 }, Context() with { SampleRate = 0 },
            Context() with { StartFrame = -1 }, Context() with { EndFrame = 1233 } })
            Check.Throws<ArgumentException>(() => new TimingSession(context, [], [], 0.1));
        foreach (double time in new[] { -1d, double.NaN, double.PositiveInfinity, 5 })
            Check.Throws<ArgumentException>(() => new TimingSession(Context(), [time], [], 0.1));
        Check.Throws<ArgumentException>(() => new TimingSession(Context(), [], [5], 0.1));
        Check.Throws<ArgumentException>(() => new TimingSession(Context(), [], [], double.NaN));
        Check.Throws<ArgumentException>(() => new TimingSession(Context(), new double[2000], new double[2000], 0.1));
        Check.Throws<ArgumentNullException>(() => new TimingSession(null!, [], [], 0.1));
        Check.Throws<ArgumentNullException>(() => new TimingSession(Context(), null!, [], 0.1));
        Check.Throws<ArgumentException>(() => new TimingSession(Context(), [0, 1e-320, 2e-320, 3e-320], [1, 1.01, 1.02, 1.03], 2));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Export_PreCancellationRetainsExistingDestinationOrAbsence(bool existing)
    {
        using var temp = new TempDirectory();
        string path = temp.File("cancel.csv");
        if (existing) await File.WriteAllTextAsync(path, "retained");
        await Check.ThrowsAsync<OperationCanceledException>(() =>
            TimingCsvExporter.ExportAsync(path, new TimingSession(Context(), [], [], 0.1), new CancellationToken(true)));
        if (existing) Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        else Assert.IsFalse(File.Exists(path));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_ConcurrentReplacementRetainsOneCompleteDistinguishableSession()
    {
        using var temp = new TempDirectory();
        string path = temp.File("shared.csv");
        var sessions = Enumerable.Range(1, 4).Select(index =>
            new TimingSession(Context("Run " + index), [index * 0.25], [index * 0.25 + 0.01], 0.1)).ToArray();
        await Task.WhenAll(sessions.Select(session => TimingCsvExporter.ExportAsync(path, session)));
        var rows = await ReadAsync(path);
        string name = JsonSerializer.Deserialize<string>(Value(rows, "project_name_json"))!;
        var winner = sessions.Single(session => session.Context.ProjectName == name);
        Assert.AreEqual(winner.ExpectedSeconds[0],
            double.Parse(rows.Single(row => row[0] == "match")[3], CultureInfo.InvariantCulture));
        Assert.AreEqual("1", Value(rows, "matched"));
        Assert.IsTrue(rows.All(row => row.Length == 8));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_WindowsSharingFailureRetainsOldContentsAndAllowsRetry()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows delete-sharing enforcement.");
        using var temp = new TempDirectory();
        string path = temp.File("locked.csv");
        await File.WriteAllTextAsync(path, "retained");
        var session = new TimingSession(Context(), [1], [1.01], 0.1);
        Exception? failure = null;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { await TimingCsvExporter.ExportAsync(path, session); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failure = error; }
        }
        Assert.IsNotNull(failure);
        Assert.IsTrue((failure.HResult & 0xFFFF) is 5 or 32 or 33, failure.Message);
        Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        temp.NoTemps();
        await TimingCsvExporter.ExportAsync(path, session);
        Assert.AreEqual("1", Value(await ReadAsync(path), "matched"));
    }

    private static string Value(List<string[]> rows, string key) => rows.Single(row => row[6] == key)[7];

    private static async Task<List<string[]>> ReadAsync(string path)
    {
        using var parser = new TextFieldParser(new StringReader(await File.ReadAllTextAsync(path)))
        {
            TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields();
            Assert.IsNotNull(row);
            rows.Add(row);
        }
        return rows;
    }
}
