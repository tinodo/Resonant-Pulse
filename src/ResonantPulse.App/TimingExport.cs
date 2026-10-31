using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

/// <summary>
/// What the Train page decides about exporting the last finished keyboard timing check, apart from the window: when the export is
/// offered, what the save dialog suggests and how the finished check is described. The file itself is Core's
/// (<see cref="TimingCsvExporter"/>).
/// </summary>
internal static class TimingExport
{
    private const string Suffix = "-timing";

    /// <summary>The part of the suggested name that comes from the project, so that the whole stays within what a name box shows.</summary>
    private const int MaximumProjectPart = 90;

    /// <summary>
    /// What a Windows file name cannot hold: these nine characters and every control character. A fixed set, not the running
    /// system's own list, so that the same project gives the same suggestion on every machine the code is tested on.
    /// </summary>
    private static readonly char[] InvalidFileNameCharacters = [.. "<>:\"/\\|?*", .. Enumerable.Range(0, 32).Select(code => (char)code)];

    /// <summary>
    /// The export is offered for a check that has ended and been analysed, and only then: never while one is running (its result
    /// does not exist yet), and not after the check was ended by a change of playback or project or by an error.
    /// </summary>
    internal static bool CanExport(bool isArmed, TimingSession? lastFinished) => !isArmed && lastFinished is not null;

    /// <summary>
    /// The file name the save dialog suggests: the name of the project that was checked, then "-timing". The name is made safe for
    /// a file name and cut so that "-timing" always fits, and never cut in the middle of a character.
    /// </summary>
    internal static string SuggestedFileName(string projectName)
    {
        var kept = projectName.Where(character => !InvalidFileNameCharacters.Contains(character)).Take(MaximumProjectPart).ToArray();
        // A cut that falls between the two halves of a pair would leave half a character in the name.
        if (kept.Length > 0 && char.IsHighSurrogate(kept[^1])) kept = kept[..^1];
        var name = new string(kept).Trim();
        return (name.Length == 0 ? "ResonantPulse" : name) + Suffix;
    }

    /// <summary>The name a check gives its rhythm: a lane's own name, or fixed text for the main beat (it has none in a project).</summary>
    internal static string TargetName(int laneIndex, string laneName) => laneIndex < 0 ? TimingSession.MainBeatName : laneName;

    /// <summary>The result of a finished check as the page shows it, in the language in use.</summary>
    internal static string ReportText(TimingReport report) => report.Matched == 0
        ? Text.Format("TimingNoMatches", report.Missed, report.Extra)
        : Text.Format("TimingReport", report.Matched, report.Missed, report.Extra,
              report.MedianOffsetMilliseconds, report.SpreadMilliseconds, report.Coverage) +
          "\n" + (report.HasDriftEstimate
              ? Text.Format("TimingAdditional", report.DriftMillisecondsPerSecond, report.AmbiguousExpected)
              : Text.Get("TimingDriftUnavailable"));

    /// <summary>
    /// The line that says what an export would hold: the project and the rhythm of the finished check and when it ended. The names
    /// are the ones the check had, not the ones the project has now, and the main beat is named in the language in use.
    /// </summary>
    internal static string ReadyText(TimingSession session) => Text.Format("TimingCsvReady",
        OneLine(session.Context.ProjectName),
        OneLine(session.Context.LaneIndex < 0 ? Text.Get("MainTarget") : session.Context.TargetName),
        session.CompletedAtUtc.ToLocalTime().ToString("g", Text.Culture));

    /// <summary>A name as one line of text: a line break or another control character in it is a space.</summary>
    private static string OneLine(string name) =>
        string.Concat(name.Select(character => char.IsControl(character) ? ' ' : character));
}
