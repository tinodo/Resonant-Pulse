namespace ResonantPulse.App;

internal static class TempoEdits
{
    internal static bool IsChange(double current, double proposed) =>
        double.IsFinite(proposed) && current != proposed;

    internal static bool TryApplyTap(double current, double? estimate, out double proposed)
    {
        proposed = current;
        if (estimate is not { } value || !double.IsFinite(value)) return false;
        proposed = Math.Clamp(value, 1, 1000);
        return IsChange(current, proposed);
    }
}
