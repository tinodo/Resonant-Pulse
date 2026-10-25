namespace ResonantPulse.App;

internal enum TimingRunUpdate { None, Continue, Finish, Invalidate }

/// <summary>Timing follows the active immutable playback revision, not pending edits.</summary>
internal static class TimingRunRules
{
    internal static bool CanEditArmingOptions(bool isArmed) => !isArmed;

    internal static TimingRunUpdate Update(bool isArmed, bool isSameRevision, bool isPlaying, bool isCompleted)
    {
        if (!isArmed) return TimingRunUpdate.None;
        if (!isSameRevision) return TimingRunUpdate.Invalidate;
        if (isCompleted) return TimingRunUpdate.Finish;
        return isPlaying ? TimingRunUpdate.Continue : TimingRunUpdate.Invalidate;
    }

    internal static bool IsValidTarget(int lane, int activeLaneCount) =>
        lane >= -1 && lane < activeLaneCount;

    internal static bool CanArm(bool hasPendingEdits, int lane, int activeLaneCount) =>
        !hasPendingEdits && IsValidTarget(lane, activeLaneCount);

    internal static long EndFrame(long startFrame, double elapsedSeconds, int sampleRate,
        long? finiteEndFrame, long? completedFrame)
    {
        var end = startFrame + (long)(Math.Clamp(elapsedSeconds, 0, 600) * sampleRate);
        if (finiteEndFrame is { } finite) end = Math.Min(end, finite);
        if (completedFrame is { } completed) end = Math.Min(end, completed);
        return Math.Max(startFrame, end);
    }
}
