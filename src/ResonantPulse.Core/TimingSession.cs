namespace ResonantPulse.Core;

/// <summary>
/// Where a keyboard timing check was run: the project and the rhythm it followed, and the window of the audio stream it covered.
/// Frames are absolute audio-device frames (<see cref="StartFrame"/> is included, <see cref="EndFrame"/> is not).
/// </summary>
/// <param name="ProjectName">The name of the project as it was called when the check began.</param>
/// <param name="TargetName">The name of the rhythm that was followed: a lane's name as it was then, or <see cref="TimingSession.MainBeatName"/>.</param>
/// <param name="LaneIndex">-1 for the main beat, otherwise the lane's place (0 to 7) in the project that was playing.</param>
/// <param name="SampleRate">Frames in a second of the audio stream.</param>
/// <param name="StartFrame">The audio frame the app had processed when the check began.</param>
/// <param name="EndFrame">
/// The frame after the last one of the check: <paramref name="StartFrame"/> plus the time the check lasted, counted by the system timer,
/// at the sample rate, or the frame where a program or a practice limit ended it first. It is not necessarily a frame the audio had reached.
/// </param>
/// <param name="IncludesSilentTargets">Whether clicks that are not played (gaps, muted layers, chance) were followed too.</param>
public sealed record TimingSessionContext(string ProjectName, string TargetName, int LaneIndex,
    int SampleRate, long StartFrame, long EndFrame, bool IncludesSilentTargets);

/// <summary>
/// One keyboard timing check that ran to its end, kept as it was measured: its context, the scheduled clicks and the presses, the
/// tolerance and the analysis. It is a copy: nothing that happens to the project, the lists it was made from or the objects it
/// hands out can change it. It is an input-arrival measurement, not calibrated acoustic timing, and not an automatic history.
/// </summary>
public sealed class TimingSession
{
    /// <summary>The longest name of a project or lane that the project file accepts.</summary>
    public const int MaximumNameLength = 256;

    /// <summary>A check lasts at most ten minutes.</summary>
    public const double MaximumDurationSeconds = 600;

    /// <summary>
    /// The name a session gives the main beat, which has no name in a project. It is fixed text, not the label of any language,
    /// so that the same check writes the same file whatever language the window that wrote it was in.
    /// </summary>
    public const string MainBeatName = "Main beat";

    public TimingSessionContext Context { get; }

    /// <summary>The scheduled clicks, in seconds after the start of the check, in the order they were given.</summary>
    public IReadOnlyList<double> ExpectedSeconds { get; }

    /// <summary>The presses, in seconds after the start of the check, in the order they were given.</summary>
    public IReadOnlyList<double> ObservedSeconds { get; }

    public double ToleranceSeconds { get; }

    public DateTimeOffset CompletedAtUtc { get; }

    public TimingReport Report { get; }

    /// <summary>The length of the window, in seconds.</summary>
    public double DurationSeconds { get; }

    /// <param name="context">Where and how the check was run.</param>
    /// <param name="expectedSeconds">The scheduled clicks, in seconds after the start of the check.</param>
    /// <param name="observedSeconds">The presses, in seconds after the start of the check.</param>
    /// <param name="toleranceSeconds">How far from a click a press may be and still be matched with it.</param>
    /// <param name="completedAtUtc">When the check ended; the clock of this computer when it is not given.</param>
    public TimingSession(TimingSessionContext context, IReadOnlyList<double> expectedSeconds, IReadOnlyList<double> observedSeconds,
        double toleranceSeconds, DateTimeOffset? completedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(expectedSeconds);
        ArgumentNullException.ThrowIfNull(observedSeconds);
        DocumentValidator.Require(IsName(context.ProjectName) && IsName(context.TargetName),
            $"Timing project and target names must be text of at most {MaximumNameLength} characters.");
        DocumentValidator.Require(context.LaneIndex is >= -1 and < 8, "Timing target must be the main beat (-1) or an authored lane (0..7).");
        DocumentValidator.Require(context.SampleRate is >= 8000 and <= 192000 &&
            context.StartFrame >= 0 && context.EndFrame >= context.StartFrame, "Timing window needs a valid sample rate and ordered nonnegative frames.");
        DocumentValidator.Require(context.EndFrame - context.StartFrame <= (long)MaximumDurationSeconds * context.SampleRate,
            "A timing check lasts at most ten minutes.");
        int n = expectedSeconds.Count, m = observedSeconds.Count;
        DocumentValidator.Require(n <= TimingAnalyzer.MaximumTimestamps && m <= TimingAnalyzer.MaximumTimestamps &&
            (long)(n + 1) * (m + 1) <= TimingAnalyzer.MaximumAlignmentCells,
            "Timing alignment exceeds four million comparison cells or 100,000 timestamps. Analyze a shorter section.");
        var duration = (double)(context.EndFrame - context.StartFrame) / context.SampleRate;
        double[] expected = expectedSeconds.ToArray(), observed = observedSeconds.ToArray();
        DocumentValidator.Require(expected.Concat(observed).All(time => double.IsFinite(time) && time >= 0 && time < duration),
            "Timing timestamps must be finite, nonnegative and inside the exclusive measurement window.");
        Report = TimingAnalyzer.Analyze(expected, observed, toleranceSeconds);
        Context = context;
        ExpectedSeconds = Array.AsReadOnly(expected);
        ObservedSeconds = Array.AsReadOnly(observed);
        ToleranceSeconds = toleranceSeconds;
        DurationSeconds = duration;
        CompletedAtUtc = (completedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
    }

    private static bool IsName(string? name)
    {
        if (name is null || name.Length > MaximumNameLength) return false;
        for (var index = 0; index < name.Length; index++)
        {
            var c = name[index];
            if (char.IsHighSurrogate(c))
            {
                if (index + 1 >= name.Length || !char.IsLowSurrogate(name[index + 1])) return false;
                index++;
            }
            else if (char.IsLowSurrogate(c)) return false;
        }
        return true;
    }
}
