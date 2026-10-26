using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

internal static class TempoEdits
{
    internal static bool ShouldSynchronizeTapIn(long capturedInputVersion, long currentInputVersion) =>
        capturedInputVersion == currentInputVersion;

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
            throw new ArgumentOutOfRangeException(null, Text.Get("TapInRange"));
        // Reuse the compiler's exact round-trip decimal convention: 29 * (1000 / 29)
        // must reach 1000, while a genuinely out-of-range endpoint must not be clamped.
        var ratio = document.Program.Count == 0 ? Rational.One :
            Rational.FromDouble(tempo) / Rational.FromDouble(document.Program[0].StartTempo);
        var scaled = document.Program.Select(step => (
            Start: Rational.FromDouble(step.StartTempo) * ratio,
            End: Rational.FromDouble(step.EndTempo) * ratio)).ToArray();
        if (scaled.Any(step => step.Start < 1 || step.Start > 1000 || step.End < 1 || step.End > 1000))
            throw new ArgumentOutOfRangeException(null, Text.Get("TapInRange"));
        document.Settings.Tempo = tempo;
        for (var index = 0; index < scaled.Length; index++)
        {
            // Exact bounds were already checked; normalize only conversion roundoff.
            document.Program[index].StartTempo = Math.Clamp(scaled[index].Start.ToDouble(), 1, 1000);
            document.Program[index].EndTempo = Math.Clamp(scaled[index].End.ToDouble(), 1, 1000);
        }
    }
}
