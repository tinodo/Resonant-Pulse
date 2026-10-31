using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

/// <summary>What the timing tests share: a context that a real run could produce, and a reader of the file that is not the writer.</summary>
internal static class TimingTestKit
{
    /// <summary>A check of 40,000 frames at 8,000 Hz: the window is [0, 5) seconds long.</summary>
    internal static TimingSessionContext Context(string project = "Practice", string target = TimingSession.MainBeatName) =>
        new(project, target, -1, 8000, 1234, 41234, true);

    internal static readonly DateTimeOffset Completed = new(2026, 10, 31, 0, 46, 32, 242, TimeSpan.Zero);

    internal static TimingSession Session(IReadOnlyList<double> expected, IReadOnlyList<double> observed, double tolerance = 0.1,
        string project = "Practice", string target = TimingSession.MainBeatName) =>
        new(Context(project, target), expected, observed, tolerance, Completed);

    internal static double Number(string cell) => double.Parse(cell, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>The value of the one metadata or summary row with this name.</summary>
    internal static string Value(List<string[]> rows, string key) => rows.Single(row => row[6] == key)[7];

    internal static List<string[]> Read(string path) => Strict(File.ReadAllBytes(path));

    /// <summary>
    /// A reader that knows only what the format promises, not how the writer works: a byte order mark, then records that each end in
    /// CRLF, whose fields are all quoted, with a quote inside a field doubled and nothing else (no bare quote, no raw CR or LF in a
    /// field, no space between a quote and a comma). Anything else is a format error, so a writer that skips a quote is caught.
    /// </summary>
    internal static List<string[]> Strict(byte[] bytes)
    {
        Assert.IsTrue(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "The file does not start with a UTF-8 byte order mark.");
        var text = new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
        var rows = new List<string[]>();
        var row = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] != '"') throw new FormatException($"A field does not start with a quote at {index}.");
            index++;
            var field = new StringBuilder();
            while (true)
            {
                if (index >= text.Length) throw new FormatException("A quoted field is not closed.");
                var c = text[index++];
                if (c != '"') { field.Append(c); continue; }
                if (index < text.Length && text[index] == '"') { field.Append('"'); index++; continue; }
                break;
            }
            var value = field.ToString();
            if (value.Contains('\r') || value.Contains('\n')) throw new FormatException("A field holds a raw line break: " + value);
            row.Add(value);
            if (index < text.Length && text[index] == ',') { index++; continue; }
            if (index + 1 < text.Length && text[index] == '\r' && text[index + 1] == '\n')
            {
                index += 2;
                rows.Add(row.ToArray());
                row.Clear();
                continue;
            }
            throw new FormatException($"A field is followed by neither a comma nor CRLF at {index}.");
        }
        if (row.Count != 0) throw new FormatException("The last record is not ended by CRLF.");
        return rows;
    }
}
