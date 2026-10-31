using System.Globalization;
using System.Text;

namespace ResonantPulse.Core;

/// <summary>
/// Writes a completed keyboard timing check (a <see cref="TimingSession"/>) as a CSV file that a spreadsheet or a script can read.
/// The format is the same in every language and on every computer, by design: UTF-8 with a byte order mark, a CRLF after every
/// record, every field in quotes, numbers in invariant culture with every digit that is needed to read them back exactly, and
/// English column and row names. A project or lane name is written as a JSON string, so that no name can start a formula or add,
/// remove or move a record. A value that is not available is an empty field, never a zero.
/// </summary>
public static class TimingCsvExporter
{
    private static readonly string[] Header =
        ["record_type", "expected_index", "observed_index", "expected_seconds", "observed_seconds", "offset_milliseconds", "name", "value"];

    /// <summary>Writes the file atomically: the old file at <paramref name="path"/> stays whole if anything goes wrong.</summary>
    public static Task ExportAsync(string path, TimingSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return AtomicFile.WriteAsync(path, (stream, token) => WriteAsync(stream, session, token), cancellationToken);
    }

    /// <summary>Writes the same bytes to a stream. This is not atomic: use <see cref="ExportAsync"/> to write a file.</summary>
    public static async Task WriteAsync(Stream stream, TimingSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(session);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 16384, leaveOpen: true)
        { NewLine = "\r\n" };
        foreach (var row in Rows(session))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(Line(row).AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Line(string[] row)
    {
        var line = new StringBuilder();
        for (var index = 0; index < row.Length; index++)
        {
            if (index > 0) line.Append(',');
            line.Append('"').Append(row[index].Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }
        return line.ToString();
    }

    private static IEnumerable<string[]> Rows(TimingSession session)
    {
        yield return Header;
        var context = session.Context;
        var report = session.Report;
        var expectedCount = session.ExpectedSeconds.Count;
        yield return Fact("metadata", "schema_version", "1");
        yield return Fact("metadata", "source", "keyboard-ui-arrival");
        yield return Fact("metadata", "limitation",
            "Key presses as the app received them, compared with the scheduled clicks. Keyboard, system, speaker and device delays are not corrected for. Not a recording.");
        yield return Fact("metadata", "clock_reference",
            "start_frame is the audio frame the app had processed when the check began. end_frame is start_frame plus the time the system timer counted until the check ended, at the sample rate, or the frame where a program or a practice limit ended it first. Neither is a calibrated acoustic position. observed_seconds come from the system timer, counted from the start of the check.");
        yield return Fact("metadata", "index_base", "0");
        yield return Fact("metadata", "window_policy",
            "start frame included, end frame excluded; expected_seconds and observed_seconds are counted from the start of the check.");
        yield return Fact("metadata", "completed_at_utc", session.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        yield return Fact("metadata", "project_name_json", JsonText.Quote(context.ProjectName));
        yield return Fact("metadata", "target_name_json", JsonText.Quote(context.TargetName));
        yield return Fact("metadata", "label_encoding", "JSON string; decode once to recover the exact name.");
        yield return Fact("metadata", "target_lane", Number(context.LaneIndex));
        yield return Fact("metadata", "sample_rate", Number(context.SampleRate));
        yield return Fact("metadata", "start_frame", context.StartFrame.ToString(CultureInfo.InvariantCulture));
        yield return Fact("metadata", "end_frame", context.EndFrame.ToString(CultureInfo.InvariantCulture));
        yield return Fact("metadata", "duration_seconds", Number(session.DurationSeconds));
        yield return Fact("metadata", "tolerance_seconds", Number(session.ToleranceSeconds));
        yield return Fact("metadata", "includes_silent_targets", context.IncludesSilentTargets ? "true" : "false");
        yield return Fact("summary", "expected_count", Number(expectedCount));
        yield return Fact("summary", "observed_count", Number(session.ObservedSeconds.Count));
        yield return Fact("summary", "matched", Number(report.Matched));
        yield return Fact("summary", "missed", Number(report.Missed));
        yield return Fact("summary", "extra", Number(report.Extra));
        yield return Fact("summary", "median_offset_milliseconds", report.Matched == 0 ? "" : Number(report.MedianOffsetMilliseconds));
        yield return Fact("summary", "spread_milliseconds", report.Matched == 0 ? "" : Number(report.SpreadMilliseconds));
        yield return Fact("summary", "coverage", expectedCount == 0 ? "" : Number(report.Coverage));
        yield return Fact("summary", "ambiguous_expected", Number(report.AmbiguousExpected));
        yield return Fact("summary", "drift_milliseconds_per_second", report.HasDriftEstimate ? Number(report.DriftMillisecondsPerSecond) : "");

        var matchedExpected = new bool[expectedCount];
        var matchedObserved = new bool[session.ObservedSeconds.Count];
        foreach (var match in report.Matches)
        {
            matchedExpected[match.ExpectedIndex] = true;
            matchedObserved[match.ObservedIndex] = true;
            yield return ["match", Number(match.ExpectedIndex), Number(match.ObservedIndex), Number(match.ExpectedSeconds),
                Number(match.ObservedSeconds), Number(match.OffsetMilliseconds), "", ""];
        }
        for (var index = 0; index < expectedCount; index++)
            if (!matchedExpected[index])
                yield return ["miss", Number(index), "", Number(session.ExpectedSeconds[index]), "", "", "", ""];
        for (var index = 0; index < matchedObserved.Length; index++)
            if (!matchedObserved[index])
                yield return ["extra", "", Number(index), "", Number(session.ObservedSeconds[index]), "", "", ""];
    }

    private static string[] Fact(string type, string name, string value) => [type, "", "", "", "", "", name, value];

    /// <summary>Every digit needed to read the number back exactly, with a point as the decimal separator whatever the language, and no negative zero.</summary>
    private static string Number(double value) =>
        value == 0 ? "0" : value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>A string as JSON text, written so that it is the same on every runtime and safe in a line-based file.</summary>
internal static class JsonText
{
    /// <summary>
    /// The string in double quotes with what JSON requires escaped (the quote, the backslash and every control character) and also
    /// the characters that text tools break lines at (DEL, NEL, the line and paragraph separators). Letters of every script stay as
    /// they are, so that a person who opens the file can read the name. The text must be well-formed (no half of a surrogate pair).
    /// </summary>
    internal static string Quote(string text)
    {
        var json = new StringBuilder(text.Length + 2).Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '\b': json.Append("\\b"); break;
                case '\f': json.Append("\\f"); break;
                case '\n': json.Append("\\n"); break;
                case '\r': json.Append("\\r"); break;
                case '\t': json.Append("\\t"); break;
                case < ' ' or '\u007f' or '\u0085' or '\u2028' or '\u2029':
                    json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default: json.Append(c); break;
            }
        }
        return json.Append('"').ToString();
    }
}
