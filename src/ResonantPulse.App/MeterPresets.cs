using System.Globalization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

/// <summary>A time signature a musician recognises from sheet music, with its customary grouping.</summary>
internal sealed record MeterPreset(int Beats, int Denominator, int[] Groups);

internal static class MeterPresets
{
    /// <summary>Row-major quick picks. The first four are the signatures a beginner meets first.</summary>
    internal static IReadOnlyList<MeterPreset> All { get; } =
    [
        new(2, 4, []), new(3, 4, []), new(4, 4, []), new(6, 8, [3, 3]),
        new(5, 4, [3, 2]), new(7, 8, [2, 2, 3]), new(9, 8, [3, 3, 3]), new(12, 8, [3, 3, 3, 3]),
        new(2, 2, []), new(3, 8, []), new(5, 8, [3, 2]), new(7, 4, [2, 2, 3])
    ];

    internal static bool IsCurrent(PulseSettings settings, MeterPreset preset) =>
        settings.BeatsPerBar == preset.Beats && settings.BeatDenominator == preset.Denominator;

    /// <summary>True when choosing the preset would change nothing: meter, one-click-per-beat unit and grouping all match.</summary>
    internal static bool IsApplied(PulseSettings settings, MeterPreset preset) =>
        IsCurrent(settings, preset) && settings.BeatUnitQuarters == 4d / preset.Denominator &&
        settings.Groups.SequenceEqual(preset.Groups);

    /// <summary>
    /// Applies a written meter so that every written beat is one click: the BPM unit follows the
    /// denominator, the accent pattern restarts (strong downbeat) and group starts are accented.
    /// </summary>
    internal static void Apply(PulseSettings settings, int beats, int denominator, int[]? groups = null)
    {
        settings.BeatsPerBar = beats;
        settings.BeatDenominator = denominator;
        settings.BeatUnitQuarters = 4d / denominator;
        settings.Groups = groups?.ToArray() ?? [];
        settings.Accents = DefaultAccents(beats);
        settings.AccentOverridesGroups = false;
        settings.ManualAccentIndices = null;
    }

    internal static Accent[] DefaultAccents(int pulses) =>
        Enumerable.Range(0, Math.Max(1, pulses)).Select(index => index == 0 ? Accent.Strong : Accent.Normal).ToArray();

    /// <summary>Pulses (clicks) in one bar for the settings' unit; at least one.</summary>
    internal static int PulsesPerBar(PulseSettings settings)
    {
        var bar = Rational.FromDouble(4d * settings.BeatsPerBar / settings.BeatDenominator);
        var count = (bar / Rational.FromDouble(settings.BeatUnitQuarters)).Ceiling();
        return (int)Math.Clamp((long)count, 1, 256);
    }

    /// <summary>True when the BPM unit divides the written bar a whole number of times, so clicks, accents and bar lines agree.</summary>
    internal static bool UnitFitsBar(int beats, int denominator, double unitQuarters)
    {
        var bar = Rational.FromDouble(4d * beats / denominator);
        return (bar / Rational.FromDouble(unitQuarters)).Denominator.IsOne;
    }

    /// <summary>
    /// True when the saved BPM unit does not divide the written bar evenly. The unit picker never offers such a unit, but an
    /// older or imported project can carry one, and its clicks then drift against the bar lines, the accents and the dial.
    /// </summary>
    internal static bool UnitNeedsFix(PulseSettings settings) =>
        !UnitFitsBar(settings.BeatsPerBar, settings.BeatDenominator, settings.BeatUnitQuarters);

    internal static string GroupsText(IEnumerable<int> groups) => string.Join("+", groups);

    /// <summary>Parses "2+2+3". Empty means no grouping. The sum must equal the beats per bar.</summary>
    internal static bool TryParseGroups(string? text, int beats, out int[] groups)
    {
        groups = [];
        if (string.IsNullOrWhiteSpace(text)) return true;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var parsed = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 1 or > 64)
                return false;
            parsed.Add(value);
        }
        if (parsed.Count > 64 || parsed.Sum() != beats) return false;
        groups = parsed.ToArray();
        return true;
    }
}
