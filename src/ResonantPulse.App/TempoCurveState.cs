using ResonantPulse.Core;

namespace ResonantPulse.App;

internal readonly record struct TempoCurveFrame(RenderPlan? Plan, long Frame, bool IsBlind);

/// <summary>
/// UI-thread preview state, independent of the immutable active audio plan.
/// Keep one compilation in flight and attempt each document revision once.
/// </summary>
internal sealed class TempoCurveState
{
    private int? _pendingRevision;
    private int? _attemptedRevision;
    private int? _previewRevision;
    private RenderPlan? _preview;

    public bool TryBegin(int revision)
    {
        if (_pendingRevision.HasValue || _attemptedRevision == revision) return false;
        _pendingRevision = revision;
        _attemptedRevision = revision;
        return true;
    }

    public bool TryComplete(int requestedRevision, int currentRevision, RenderPlan? plan)
    {
        // A late/duplicate completion must not release a newer request.
        if (_pendingRevision != requestedRevision) return false;
        _pendingRevision = null;
        if (requestedRevision != currentRevision || plan is null) return false;
        _previewRevision = requestedRevision;
        _preview = plan;
        return true;
    }

    public TempoCurveFrame Select(int currentRevision, RenderPlan? activePlan, long frame, bool isBlind)
    {
        if (activePlan is not null)
            return new(activePlan, Math.Clamp(frame, 0, activePlan.TotalFrames - 1), isBlind);
        // Never retain the previous playback cursor/blind gap, or a preview
        // compiled for a different document, across idle/null-plan refreshes.
        return new(_previewRevision == currentRevision ? _preview : null, 0, false);
    }
}
