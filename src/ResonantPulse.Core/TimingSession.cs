namespace ResonantPulse.Core;

public sealed record TimingSessionContext(string ProjectName, string TargetName, int LaneIndex,
    int SampleRate, long StartFrame, long EndFrame, bool IncludesSilentTargets);

/// <summary>Immutable completed input-arrival measurement, not calibrated physical/acoustic timing or automatic history.</summary>
public sealed class TimingSession
{
    public TimingSessionContext Context { get; }
    public IReadOnlyList<double> ExpectedSeconds { get; }
    public IReadOnlyList<double> ObservedSeconds { get; }
    public double ToleranceSeconds { get; }
    public DateTimeOffset CompletedAtUtc { get; }
    public TimingReport Report { get; }
    public double DurationSeconds => (double)(Context.EndFrame - Context.StartFrame) / Context.SampleRate;

    public TimingSession(TimingSessionContext context, IReadOnlyList<double> expectedSeconds,
        IReadOnlyList<double> observedSeconds, double toleranceSeconds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(expectedSeconds);
        ArgumentNullException.ThrowIfNull(observedSeconds);
        DocumentValidator.Require(context.ProjectName is not null && context.ProjectName.Length <= 256 &&
            context.TargetName is not null && context.TargetName.Length <= 256, "Timing project/target names must contain at most 256 characters.");
        DocumentValidator.Require(context.LaneIndex is >= -1 and < 8, "Timing target must be main pulse (-1) or an authored lane (0..7).");
        DocumentValidator.Require(context.SampleRate is >= 8000 and <= 192000 &&
            context.StartFrame >= 0 && context.EndFrame >= context.StartFrame, "Timing window needs a valid sample rate and ordered nonnegative frames.");
        int n = expectedSeconds.Count, m = observedSeconds.Count;
        DocumentValidator.Require(n <= TimingAnalyzer.MaximumTimestamps && m <= TimingAnalyzer.MaximumTimestamps &&
            (long)(n + 1) * (m + 1) <= TimingAnalyzer.MaximumAlignmentCells,
            "Timing alignment exceeds its timestamp/comparison-cell budget. Analyze a shorter section.");
        Context = context;
        double[] expected = expectedSeconds.ToArray(), observed = observedSeconds.ToArray();
        DocumentValidator.Require(expected.Concat(observed).All(time => double.IsFinite(time) && time >= 0 && time < DurationSeconds),
            "Timing timestamps must be finite, nonnegative and inside the exclusive measurement window.");
        Report = TimingAnalyzer.Analyze(expected, observed, toleranceSeconds);
        DocumentValidator.Require(double.IsFinite(Report.MedianOffsetMilliseconds) && double.IsFinite(Report.SpreadMilliseconds) &&
            double.IsFinite(Report.DriftMillisecondsPerSecond), "Timing statistics exceed finite numeric range; use a wider or shorter measurement window.");
        ExpectedSeconds = Array.AsReadOnly(expected);
        ObservedSeconds = Array.AsReadOnly(observed);
        ToleranceSeconds = toleranceSeconds;
        CompletedAtUtc = DateTimeOffset.UtcNow;
    }
}
