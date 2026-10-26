using ResonantPulse.Core;

namespace ResonantPulse.App;

/// <summary>Matches Core's distinct implicit defaults and repeating patterns.
/// Expanding is for deliberate per-pulse editing, not unrelated setting changes.</summary>
internal static class AccentPatterns
{
    internal static Accent MainAt(Accent[] source, int index) =>
        source.Length == 0 ? Accent.Normal : source[index % source.Length];

    internal static Accent LaneAt(Accent[] source, int index) =>
        source.Length == 0
            ? (index == 0 ? Accent.Strong : Accent.Normal)
            : source[index % source.Length];

    internal static Accent[] ExpandMain(Accent[] source, int count) =>
        Enumerable.Range(0, count).Select(index => MainAt(source, index)).ToArray();

    internal static Accent[] ExpandEffectiveMain(PulseSettings settings, int count)
    {
        var pattern = ExpandMain(settings.Accents, count);
        if (settings.AccentOverridesGroups) return pattern;
        int boundary = 0;
        foreach (int group in settings.Groups)
        {
            for (int index = 0; index < count; index++)
                if (!(settings.ManualAccentIndices?.Contains(index) ?? false) &&
                    Rational.FromDouble(settings.BeatUnitQuarters) * index == new Rational(4 * boundary, settings.BeatDenominator))
                    pattern[index] = Accent.Strong;
            boundary += group;
        }
        return pattern;
    }

    internal static Accent[] ExpandLane(Accent[] source, int count) =>
        Enumerable.Range(0, count).Select(index => LaneAt(source, index)).ToArray();
}
