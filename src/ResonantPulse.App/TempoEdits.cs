using ResonantPulse.App.Localization;
using ResonantPulse.Core;

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

    internal static void ApplyTapIn(PulseDocument document, double tempo)
    {
        if (!double.IsFinite(tempo) || tempo is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(tempo), Text.Get("TapInRange"));
        var ratio = document.Program.Count == 0 ? 1 : tempo / document.Program[0].StartTempo;
        var scaled = document.Program.Select(step => (Start: step.StartTempo * ratio, End: step.EndTempo * ratio)).ToArray();
        if (scaled.Any(step => !double.IsFinite(step.Start) || !double.IsFinite(step.End) ||
            step.Start is < 1 or > 1000 || step.End is < 1 or > 1000))
            throw new ArgumentOutOfRangeException(nameof(tempo), Text.Get("TapInRange"));
        document.Settings.Tempo = tempo;
        for (var index = 0; index < scaled.Length; index++)
        {
            document.Program[index].StartTempo = scaled[index].Start;
            document.Program[index].EndTempo = scaled[index].End;
        }
    }
}
