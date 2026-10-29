namespace ResonantPulse.Core;

/// <summary>Robust recent-interval estimate in taps/minute, interpreted in the UI's selected note unit. Not hardware calibration.</summary>
public sealed class TapTempoEstimator
{
    private readonly double[] intervals = new double[12];
    private int count, next;
    private double? last;
    private int candidateCount;
    private double candidateMean;
    public int AcceptedIntervals => count;
    public int RejectedTaps { get; private set; }
    public double? CurrentBpm { get; private set; }
    public bool LastTapAccepted { get; private set; }
    /// <summary>
    /// Applies the outlier gate after the first interval instead of after three. Regular tap tempo keeps the default: it prefers
    /// to follow the player's first intervals. An armed tap-in starts the metronome from the estimate, so it distrusts a single
    /// odd interval at once and a tap that is not steady never starts playback.
    /// </summary>
    public bool RejectEarlyOutliers { get; init; }
    /// <summary>Idle reset threshold. Increase this explicitly when measuring extremely slow taps.</summary>
    public double IdleResetSeconds { get; init; } = 4;
    public double? Tap(double timestampSeconds)
    {
        if (!double.IsFinite(timestampSeconds) || timestampSeconds < 0) throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
        if (!double.IsFinite(IdleResetSeconds) || IdleResetSeconds <= 0) throw new ArgumentException("IdleResetSeconds must be finite and positive.");
        LastTapAccepted = false;
        if (last is null) { last = timestampSeconds; LastTapAccepted = true; return null; }
        double delta = timestampSeconds - last.Value;
        if (delta <= 0) { RejectedTaps++; return CurrentBpm; }
        double median = count == 0 ? 0 : IntervalMedian();
        if (delta > Math.Max(IdleResetSeconds, median * 3))
        { Reset(); last = timestampSeconds; LastTapAccepted = true; return null; }
        // Ignore contact bounce without shifting the reference timestamp.
        if (delta < 0.06) { RejectedTaps++; return CurrentBpm; }
        if (delta > 60) { Reset(); last = timestampSeconds; LastTapAccepted = true; return null; }
        if (count >= (RejectEarlyOutliers ? 1 : 3) && Math.Abs(delta - median) > Math.Max(0.025, median * 0.30))
        {
            // An anomalous interval is excluded; advance reference so a missed tap does not poison later intervals.
            if (candidateCount > 0 && Math.Abs(delta - candidateMean) <= Math.Max(0.025, candidateMean * 0.1))
            { candidateMean = (candidateMean * candidateCount + delta) / (candidateCount + 1); candidateCount++; }
            else { candidateMean = delta; candidateCount = 1; }
            last = timestampSeconds;
            if (candidateCount < 3) { RejectedTaps++; return CurrentBpm; }
            // Three consistent new intervals are evidence of an intentional tempo change, not a single missed tap.
            count = 3; next = 3; intervals[0] = intervals[1] = intervals[2] = candidateMean;
            candidateCount = 0; CurrentBpm = 60 / candidateMean; LastTapAccepted = true;
            return CurrentBpm;
        }
        candidateCount = 0;
        intervals[next] = delta; next = (next + 1) % intervals.Length; count = Math.Min(count + 1, intervals.Length);
        last = timestampSeconds; LastTapAccepted = true;
        CurrentBpm = 60 / IntervalMedian();
        return CurrentBpm;
    }
    public void Reset()
    {
        count = 0; next = 0; last = null; CurrentBpm = null; RejectedTaps = 0; LastTapAccepted = false;
        candidateCount = 0; candidateMean = 0;
        Array.Clear(intervals);
    }
    private double IntervalMedian()
    {
        Span<double> work = stackalloc double[12];
        intervals.AsSpan(0, count).CopyTo(work);
        work = work[..count]; work.Sort();
        return count % 2 == 0 ? (work[count / 2 - 1] + work[count / 2]) / 2 : work[count / 2];
    }
}
