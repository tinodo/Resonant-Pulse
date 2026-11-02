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
    /// denominator, the accent pattern restarts (strong downbeat) and group starts are accented. The strengths of the
    /// clicks that split a beat restart too, as they belonged to the bar that is gone.
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
        settings.SubdivisionAccents = null;
    }

    /// <summary>
    /// What choosing a time signature from the picker (or the repair of a mismatched unit) does: <see cref="Apply"/>, and an end to a
    /// split that no longer fits. A subdivision is counted against the click unit, so when the unit changes the old split means
    /// nothing against the new one and the beats are no longer split. When the new unit is exactly what the split clicks were
    /// (6/8 felt in two, counted in dotted quarters and split in three, becoming 6/8 counted in eighths) the tempo is raised by
    /// the old split so that the same clicks keep coming at the same speed; otherwise the BPM number is kept, as it always was.
    /// A split with the same unit (4/4 to 3/4) is kept.
    /// </summary>
    internal static void ApplyOneClickPerBeat(PulseSettings settings, int beats, int denominator, int[]? groups = null)
    {
        var unitBefore = settings.BeatUnitQuarters;
        var split = settings.Subdivision;
        Apply(settings, beats, denominator, groups);
        if (settings.BeatUnitQuarters == unitBefore || split <= 1) return;
        if (Math.Abs(unitBefore / split - settings.BeatUnitQuarters) < 1e-9)
            settings.Tempo = Math.Clamp(Math.Round(settings.Tempo * split, 1), 1, 1000);
        settings.Subdivision = 1;
        settings.Swing = 0.5;
    }

    /// <summary>
    /// Applies a ready-made meter to the project the way the window does it: after the nudges that are on their way, because choosing it
    /// can rewrite the tempo (<see cref="ApplyOneClickPerBeat"/>) and a nudge that landed after the conversion would put a number of the
    /// old unit over the converted tempo, in the order of the other edits that wait for them (a ready-made meter or Done in the Custom
    /// meter panel chosen earlier is applied first, so the one chosen last is the one the project ends on), and only into the project it
    /// was chosen for: when that project has been replaced meanwhile (Open, New, a ready-made rhythm, the next song) nothing is applied.
    /// A meter that the project holds already is not applied again (that would restart its accents), unless an edit is on its way: what
    /// the project holds then is not what it will hold, and the choice is the one that must come last. True when the meter was applied.
    /// </summary>
    /// <param name="held">The settings of the project as it is now.</param>
    /// <param name="commit">Commits an edit to the project in the edit queue (the window's commit, which takes its place before it awaits).</param>
    internal static async Task<bool> ApplyAfterNudgesAsync(TempoNudges nudges, Func<PulseSettings> held,
        Func<Action<PulseDocument>, Task> commit, MeterPreset preset)
    {
        if (!nudges.TypedEditsOnTheirWay && IsApplied(held(), preset)) return false;
        return await nudges.EditAfterNudgesAsync(commit,
            document => ApplyOneClickPerBeat(document.Settings, preset.Beats, preset.Denominator, preset.Groups));
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
