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

    /// <summary>
    /// Makes the tempo tapped for "Tap to start" the tempo of the project, in the project's own BPM note unit (nothing about the
    /// unit, the meter or the count-in changes). A project with a program takes it as the tempo of the first step, and every
    /// step is scaled by the same factor, ramps and steady steps alike, so the program keeps its shape. Every tempo it would write
    /// is checked first and a tempo outside 1 to 1000 BPM refuses the whole change with a message for the person: the project
    /// is left as it was, and nothing is clamped to a tempo nobody tapped.
    /// </summary>
    /// <exception cref="UserMessageException">The tapped tempo, or a step scaled to it, would be outside 1 to 1000 BPM.</exception>
    internal static void ApplyTapIn(PulseDocument document, double tempo)
    {
        if (!InRange(tempo)) throw new UserMessageException(Text.Get("TapStartRange"));
        // Exact arithmetic, on the same round-trip decimals the compiler uses: a ratio such as 1000 / 29 is not a double, and
        // multiplying 29 by its rounded value would miss 1000 (and be refused) although 29 BPM at 1000 BPM is allowed. A tempo
        // that is genuinely outside the range is refused, not clamped.
        foreach (var step in document.Program)
            if (!InRange(step.StartTempo) || !InRange(step.EndTempo)) throw new UserMessageException(Text.Get("TapStartRange"));
        var ratio = document.Program.Count == 0 ? Rational.One :
            Rational.FromDouble(tempo) / Rational.FromDouble(document.Program[0].StartTempo);
        var scaled = document.Program.Select(step => (
            Start: Rational.FromDouble(step.StartTempo) * ratio,
            End: Rational.FromDouble(step.EndTempo) * ratio)).ToArray();
        if (scaled.Any(step => step.Start < 1 || step.Start > 1000 || step.End < 1 || step.End > 1000))
            throw new UserMessageException(Text.Get("TapStartRange"));
        document.Settings.Tempo = tempo;
        for (var index = 0; index < scaled.Length; index++)
        {
            // The bounds were checked exactly; only the rounding of the conversion back to a double is normalized here.
            document.Program[index].StartTempo = Math.Clamp(scaled[index].Start.ToDouble(), 1, 1000);
            document.Program[index].EndTempo = Math.Clamp(scaled[index].End.ToDouble(), 1, 1000);
        }
    }

    private static bool InRange(double tempo) => double.IsFinite(tempo) && tempo is >= 1 and <= 1000;
}
