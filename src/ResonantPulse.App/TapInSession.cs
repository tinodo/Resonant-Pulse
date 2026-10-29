using ResonantPulse.Core;

namespace ResonantPulse.App;

/// <summary>
/// The intent to start the metronome from taps: explicit, one-shot and never saved. It counts the consecutive taps that were
/// accepted, a tap that is rejected breaks the run, and cancelling it (or arming again) invalidates a start that is still on its
/// way. It knows nothing of windows or audio, so every rule is tested without either.
/// </summary>
internal sealed class TapInSession
{
    private readonly TapTempoEstimator _estimate = new() { RejectEarlyOutliers = true };
    private string _outputId = "";

    /// <summary>Changes whenever the intent ends or is armed again, so a start that is still on its way can tell it was overtaken.</summary>
    public long Generation { get; private set; }

    /// <summary>Listening for taps.</summary>
    public bool IsArmed { get; private set; }

    /// <summary>The last tap was accepted: the tempo is being saved and the audio prepared. No further tap is taken.</summary>
    public bool IsPending { get; private set; }

    public int RequiredTaps { get; private set; } = TapStartRules.DefaultTaps;

    /// <summary>Consecutive accepted taps in the current run.</summary>
    public int ValidTaps { get; private set; }

    public bool LastTapRejected { get; private set; }

    /// <summary>The estimate in taps per minute, which the person reads in the project's BPM note unit.</summary>
    public double? Bpm => _estimate.CurrentBpm;

    /// <summary>The estimate to the nearest tenth, as a plain tap rounds it: the tempo a start would use.</summary>
    public double? Tempo => Bpm is { } bpm ? Math.Round(bpm, 1) : null;

    /// <summary>
    /// What the dial shows while tapping: the estimate of a run that is still unbroken and has at least one interval, and also
    /// while the start is on its way, so that the old tempo does not flash back. After an uneven tap the estimate that is left
    /// over is not the one a start would use, so there is none until the run has two taps again.
    /// </summary>
    public double? ShownTempo => (IsArmed || IsPending) && ValidTaps >= TapStartRules.MinimumTaps ? Tempo : null;

    /// <param name="requiredTaps">How many consecutive accepted taps start the metronome, 2 to 16.</param>
    /// <param name="outputId">The output device that will play. The intent ends if that device changes (see <see cref="IsAffectedByOutput"/>).</param>
    public void Arm(int requiredTaps, string outputId)
    {
        // Checked before anything changes, so a refused request leaves an armed session as it was.
        if (requiredTaps < TapStartRules.MinimumTaps || requiredTaps > TapStartRules.MaximumTaps)
            throw new ArgumentOutOfRangeException(nameof(requiredTaps));
        ArgumentException.ThrowIfNullOrWhiteSpace(outputId);
        Cancel();
        RequiredTaps = requiredTaps;
        _outputId = outputId;
        IsArmed = true;
    }

    /// <summary>True while armed or pending for this output device (Windows spells device ids in either case).</summary>
    public bool IsAffectedByOutput(string id) => (IsArmed || IsPending) &&
        string.Equals(_outputId, id, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Takes one tap. True when it completes the run: the session is then pending, and no further tap is accepted until it is
    /// armed again.
    /// </summary>
    public bool Tap(double timestampSeconds)
    {
        if (!IsArmed) throw new InvalidOperationException("Tap-in is not armed.");
        _estimate.Tap(timestampSeconds);
        LastTapRejected = !_estimate.LastTapAccepted;
        if (LastTapRejected) ValidTaps = 0;
        else if (_estimate.CurrentBpm is null) ValidTaps = 1;
        else ValidTaps++;
        if (ValidTaps < RequiredTaps || _estimate.CurrentBpm is null) return false;
        IsArmed = false;
        IsPending = true;
        return true;
    }

    /// <summary>True while a start that began at <paramref name="generation"/> is still wanted.</summary>
    public bool IsCurrent(long generation) => IsPending && Generation == generation;

    /// <summary>Ends the intent once its start has been carried out (or has failed). Does nothing for a run that was overtaken.</summary>
    public void Finish(long generation)
    {
        if (IsCurrent(generation)) Cancel();
    }

    public void Cancel()
    {
        Generation++;
        IsArmed = IsPending = LastTapRejected = false;
        ValidTaps = 0;
        _outputId = "";
        _estimate.Reset();
    }
}
