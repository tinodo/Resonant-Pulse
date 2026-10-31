using System.Globalization;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

/// <summary>
/// What the Library page decides about saving a project as separate tracks in one ZIP, apart from the window: the numbers the
/// confirmation shows, the file name the save dialog suggests and how a track that is too long is refused. The archive itself is
/// Core's (<see cref="WaveStemExporter"/>; docs/WAV-STEMS.md is the specification).
/// </summary>
internal static class StemExport
{
    private const string Suffix = "-tracks";

    /// <summary>The part of the suggested name that comes from the project, so that the whole stays within what a name box shows.</summary>
    private const int MaximumProjectPart = 90;

    private const double Kibi = 1024, Mebi = 1024 * Kibi, Gibi = 1024 * Mebi;

    /// <summary>
    /// What a Windows file name cannot hold: these nine characters and every control character. A fixed set, not the running
    /// system's own list, so that the same project gives the same suggestion on every machine the code is tested on.
    /// </summary>
    private static readonly char[] InvalidFileNameCharacters = [.. "<>:\"/\\|?*", .. Enumerable.Range(0, 32).Select(code => (char)code)];

    /// <summary>What the confirmation says: how many tracks, how long each is and how much room they take unzipped.</summary>
    internal readonly record struct Summary(int Tracks, TimeSpan Length, long Bytes);

    /// <summary>
    /// The numbers the confirmation shows, taken from the plan that will be exported. A track that would be longer than a WAV file can
    /// be is refused here, in the person's own words and before anything is asked of them.
    /// </summary>
    internal static Summary Summarize(RenderPlan plan)
    {
        long bytes;
        try { bytes = WaveStemExporter.GetUncompressedAudioBytes(plan); }
        catch (ArgumentException) { throw new UserMessageException(Text.Format("StemExportTooLong", Text.Get("ExportBars"))); }
        return new Summary(plan.LaneCount + 1, TimeSpan.FromSeconds((double)plan.TotalFrames / plan.SampleRate), bytes);
    }

    /// <summary>
    /// From this many unzipped bytes on, the question also says that saving can take a while: the time is mostly rendering and
    /// conversion, about 20 to 40 seconds for each GiB on the machine where it was measured (docs/WAV-STEMS.md), so a few seconds
    /// below this size and the better part of a minute above it.
    /// </summary>
    internal const long SlowBytes = 512L * 1024 * 1024;

    /// <summary>The question that is asked before a file is chosen.</summary>
    internal static string ConfirmText(Summary summary)
    {
        var question = Text.Format("StemExportConfirm", summary.Tracks, FormatLength(summary.Length), FormatSize(summary.Bytes, Text.Culture));
        return summary.Bytes >= SlowBytes ? question + " " + Text.Get("StemExportSlow") : question;
    }

    /// <summary>
    /// A size in the largest unit that keeps it above 1 (KiB, MiB or GiB: binary units, 1 MiB is 1,048,576 bytes, written the same in
    /// every language so that no language's MB or Mo can mean something else), with one decimal from MiB up. The unit changes where the
    /// written number would otherwise be off: from 0.1 MiB up (below it the number would read 0.0 or 0.1) and from 1,023.95 MiB up
    /// (it would read 1,024.0 MiB).
    /// </summary>
    internal static string FormatSize(long bytes, CultureInfo culture)
    {
        if (Math.Round(bytes / Mebi, 1, MidpointRounding.AwayFromZero) >= 1024) return string.Format(culture, "{0:N1} GiB", bytes / Gibi);
        if (bytes / Mebi >= 0.1) return string.Format(culture, "{0:N1} MiB", bytes / Mebi);
        return string.Format(culture, "{0:N0} KiB", Math.Max(1, Math.Round(bytes / Kibi, 0, MidpointRounding.AwayFromZero)));
    }

    /// <summary>
    /// A length as minutes and seconds (hours as well from an hour up), rounded up to the next whole second: the same digits in every
    /// language.
    /// </summary>
    internal static string FormatLength(TimeSpan length)
    {
        var seconds = (long)Math.Ceiling(length.TotalSeconds);
        var hours = seconds / 3600;
        var minutes = seconds / 60 % 60;
        var rest = seconds % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{rest:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{rest:00}");
    }

    /// <summary>
    /// The file name the save dialog suggests: the name of the project, then "-tracks". The name is made safe for a file name and cut
    /// so that "-tracks" always fits, and never cut in the middle of a character.
    /// </summary>
    internal static string SuggestedFileName(string projectName)
    {
        var kept = projectName.Where(character => !InvalidFileNameCharacters.Contains(character)).Take(MaximumProjectPart).ToArray();
        // A cut that falls between the two halves of a pair would leave half a character in the name.
        if (kept.Length > 0 && char.IsHighSurrogate(kept[^1])) kept = kept[..^1];
        var name = new string(kept).Trim();
        return (name.Length == 0 ? "ResonantPulse" : name) + Suffix;
    }
}
