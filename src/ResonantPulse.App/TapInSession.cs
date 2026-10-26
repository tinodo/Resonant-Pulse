using ResonantPulse.Core;

namespace ResonantPulse.App;

/// <summary>Explicit, one-shot intent. Rejected taps break the consecutive run;
/// cancellation invalidates a pending asynchronous start.</summary>
internal sealed class TapInSession
{
    private readonly TapTempoEstimator _estimate = new() { RejectEarlyOutliers = true };
    public long Generation { get; private set; }
    public bool IsArmed { get; private set; }
    public bool IsPending { get; private set; }
    public int RequiredTaps { get; private set; } = 4;
    public int ValidTaps { get; private set; }
    public bool LastTapRejected { get; private set; }
    public double? Bpm => _estimate.CurrentBpm;

    public void Arm(int requiredTaps)
    {
        if (requiredTaps is < 2 or > 16) throw new ArgumentOutOfRangeException(nameof(requiredTaps));
        Cancel();
        RequiredTaps = requiredTaps;
        IsArmed = true;
    }

    public bool Tap(double timestamp)
    {
        if (!IsArmed) throw new InvalidOperationException("Tap-in is not armed.");
        _estimate.Tap(timestamp);
        LastTapRejected = !_estimate.LastTapAccepted;
        if (LastTapRejected) ValidTaps = 0;
        else if (_estimate.CurrentBpm is null) ValidTaps = 1;
        else ValidTaps++;
        if (ValidTaps < RequiredTaps || _estimate.CurrentBpm is null) return false;
        IsArmed = false;
        IsPending = true;
        return true;
    }

    public bool IsCurrent(long generation) => IsPending && Generation == generation;

    public void Finish(long generation)
    {
        if (IsCurrent(generation)) Cancel();
    }

    public void Cancel()
    {
        Generation++;
        IsArmed = IsPending = LastTapRejected = false;
        ValidTaps = 0;
        _estimate.Reset();
    }
}
