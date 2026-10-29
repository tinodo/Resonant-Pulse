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
        var unit = Rational.FromDouble(settings.BeatUnitQuarters);
        foreach (int group in settings.Groups)
        {
            var position = new Rational(4 * boundary, settings.BeatDenominator);
            for (int index = 0; index < count; index++)
                if (!(settings.ManualAccentIndices?.Contains(index) ?? false) &&
                    unit * index == position)
                    pattern[index] = Accent.Strong;
            boundary += group;
        }
        return pattern;
    }

    internal static Accent[] ExpandLane(Accent[] source, int count) =>
        Enumerable.Range(0, count).Select(index => LaneAt(source, index)).ToArray();

    /// <summary>A split click's strength: the project's, repeating like the beats' (Core's rule), or Soft when it sets none.</summary>
    internal static Accent SplitAt(Accent[]? source, int slot) =>
        source is { Length: > 0 } ? source[slot % source.Length] : Accent.Soft;

    internal static Accent[] ExpandSplit(Accent[]? source, int count) =>
        Enumerable.Range(0, count).Select(slot => SplitAt(source, slot)).ToArray();

    /// <summary>A tap cycles Silent, Soft, Medium and Strong; an arrow key steps up or down and stops at the ends.</summary>
    internal static Accent NextStrength(Accent current, int delta, bool wrap)
    {
        var next = (int)current + delta;
        return (Accent)(wrap ? (next % 4 + 4) % 4 : Math.Clamp(next, 0, 3));
    }

    /// <summary>
    /// Changes the strength of one cell of the strip a person tapped. <paramref name="shown"/> is the strip that was on screen: a tap
    /// can reach the document after another change has cut the bar differently (a new subdivision, meter or unit), when the same
    /// cell number is another click, so nothing changes then. A pattern longer than the shown bar (a program section in a longer
    /// meter reads the rest of it) keeps its length. Returns whether the settings changed.
    /// </summary>
    internal static bool ChangeStrength(PulseSettings settings, ClickGrid shown, int cell, int delta, bool wrap)
    {
        var clicks = ClickGrid.Of(settings);
        if (!clicks.SameCells(shown) || cell < 0 || cell >= clicks.Count) return false;
        if (clicks.IsSplit(cell))
        {
            var splits = ExpandSplit(settings.SubdivisionAccents, Math.Max(settings.SubdivisionAccents?.Length ?? 0, clicks.SplitSlots));
            var slot = clicks.SlotOf(cell);
            splits[slot] = NextStrength(splits[slot], delta, wrap);
            settings.SubdivisionAccents = splits;
            return true;
        }
        var beat = clicks.BeatOf(cell);
        var count = Math.Max(settings.Accents.Length, clicks.Beats);
        var chosen = ExpandEffectiveMain(settings, count)[beat];
        var pattern = ExpandMain(settings.Accents, count);
        pattern[beat] = NextStrength(chosen, delta, wrap);
        settings.Accents = pattern;
        settings.ManualAccentIndices = (settings.ManualAccentIndices ?? []).Append(beat).Distinct().Order().ToArray();
        return true;
    }
}
