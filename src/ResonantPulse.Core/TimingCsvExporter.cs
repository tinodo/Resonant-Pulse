using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ResonantPulse.Core;

/// <summary>Atomic UTF-8/BOM CSV of a frozen keyboard-arrival report, including matched and unmatched original indices.</summary>
public static class TimingCsvExporter
{
    public static Task ExportAsync(string path, TimingSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return AtomicFile.WriteAsync(path, async (stream, token) =>
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(true), 4096, leaveOpen: true) { NewLine = "\r\n" };
            foreach (var row in Rows(session))
            {
                token.ThrowIfCancellationRequested();
                string line = string.Join(',', row.Select(value => "\"" + value.Replace("\"", "\"\"") + "\""));
                await writer.WriteLineAsync(line.AsMemory(), token).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    private static IEnumerable<string[]> Rows(TimingSession session)
    {
        yield return ["record_type", "expected_index", "observed_index", "expected_seconds", "observed_seconds",
            "offset_milliseconds", "name", "value"];
        string[] Fact(string type, string name, string value) => [type, "", "", "", "", "", name, value];
        string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        var context = session.Context;
        var report = session.Report;
        yield return Fact("metadata", "schema_version", "1");
        yield return Fact("metadata", "source", "keyboard-ui-arrival");
        yield return Fact("metadata", "limitation", "Input-arrival observations, not calibrated acoustic latency or physical performance");
        yield return Fact("metadata", "clock_reference", "Processed-audio frame snapshot and Stopwatch input-arrival elapsed time");
        yield return Fact("metadata", "index_base", "0");
        yield return Fact("metadata", "window_policy", "start inclusive, end exclusive; seconds relative to arming");
        yield return Fact("metadata", "completed_at_utc", session.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        // JSON strings start with a literal quote and escape controls. User labels
        // therefore cannot become spreadsheet formulas or inject CSV records.
        yield return Fact("metadata", "project_name_json", JsonSerializer.Serialize(context.ProjectName));
        yield return Fact("metadata", "target_name_json", JsonSerializer.Serialize(context.TargetName));
        yield return Fact("metadata", "label_encoding", "JSON string; decode once to recover the exact user label");
        yield return Fact("metadata", "target_lane", Number(context.LaneIndex));
        yield return Fact("metadata", "sample_rate", Number(context.SampleRate));
        yield return Fact("metadata", "start_frame", context.StartFrame.ToString(CultureInfo.InvariantCulture));
        yield return Fact("metadata", "end_frame", context.EndFrame.ToString(CultureInfo.InvariantCulture));
        yield return Fact("metadata", "duration_seconds", Number(session.DurationSeconds));
        yield return Fact("metadata", "tolerance_seconds", Number(session.ToleranceSeconds));
        yield return Fact("metadata", "includes_silent_targets", context.IncludesSilentTargets ? "true" : "false");
        yield return Fact("summary", "expected_count", Number(session.ExpectedSeconds.Count));
        yield return Fact("summary", "observed_count", Number(session.ObservedSeconds.Count));
        yield return Fact("summary", "matched", Number(report.Matched));
        yield return Fact("summary", "missed", Number(report.Missed));
        yield return Fact("summary", "extra", Number(report.Extra));
        yield return Fact("summary", "median_offset_milliseconds", report.Matched == 0 ? "" : Number(report.MedianOffsetMilliseconds));
        yield return Fact("summary", "spread_milliseconds", report.Matched == 0 ? "" : Number(report.SpreadMilliseconds));
        yield return Fact("summary", "coverage", session.ExpectedSeconds.Count == 0 ? "" : Number(report.Coverage));
        yield return Fact("summary", "ambiguous_expected", Number(report.AmbiguousExpected));
        yield return Fact("summary", "drift_milliseconds_per_second", report.Matched < 4 ? "" : Number(report.DriftMillisecondsPerSecond));
        var matchedExpected = new HashSet<int>();
        var matchedObserved = new HashSet<int>();
        foreach (var match in report.Matches)
        {
            matchedExpected.Add(match.ExpectedIndex);
            matchedObserved.Add(match.ObservedIndex);
            yield return ["match", Number(match.ExpectedIndex), Number(match.ObservedIndex),
                Number(match.ExpectedSeconds), Number(match.ObservedSeconds), Number(match.OffsetMilliseconds), "", ""];
        }
        for (int index = 0; index < session.ExpectedSeconds.Count; index++)
            if (!matchedExpected.Contains(index))
                yield return ["miss", Number(index), "", Number(session.ExpectedSeconds[index]), "", "", "", ""];
        for (int index = 0; index < session.ObservedSeconds.Count; index++)
            if (!matchedObserved.Contains(index))
                yield return ["extra", "", Number(index), "", Number(session.ObservedSeconds[index]), "", "", ""];
    }
}
