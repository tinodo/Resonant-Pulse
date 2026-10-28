using ResonantPulse.Core;

namespace ResonantPulse.App;

/// <summary>
/// One undo step per burst of edits to the same field: holding a stepper or nudging a value ten times should not take ten
/// presses of Undo to reverse. An edit continues a burst when it has the same key and follows the last one closely.
/// </summary>
internal static class UndoCoalescing
{
    internal static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    /// <summary>True when the edit replaces the current undo step instead of adding another.</summary>
    internal static bool ShouldMerge(string? lastKey, string? key, DateTime lastAt, DateTime now) =>
        key is not null && key == lastKey && now >= lastAt && now - lastAt <= Window;
}

/// <summary>Levels (0 to 1) and pan (-1 to 1) as the whole percentages a slider shows.</summary>
internal static class LevelFormat
{
    internal static int ToPercent(double level) =>
        (int)Math.Round(Math.Clamp(level, 0, 1) * 100, MidpointRounding.AwayFromZero);

    internal static double FromPercent(double percent) => Math.Clamp(Math.Round(percent, MidpointRounding.AwayFromZero), 0, 100) / 100;

    internal static int PanToPercent(double pan) =>
        (int)Math.Round(Math.Clamp(pan, -1, 1) * 100, MidpointRounding.AwayFromZero);

    internal static double PanFromPercent(double percent) =>
        Math.Clamp(Math.Round(percent, MidpointRounding.AwayFromZero), -100, 100) / 100;
}

internal static class TextFit
{
    /// <summary>The text cut to at most <paramref name="maxLength"/> characters with an ellipsis, never inside a surrogate pair.</summary>
    internal static string Shorten(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        var keep = Math.Max(0, maxLength - 1);
        if (keep > 0 && char.IsHighSurrogate(text[keep - 1])) keep--;
        return text[..keep] + "…";
    }
}

/// <summary>
/// When a slider's change should be committed: at the end of a gesture, not on every tick of a drag. A pointer drag and a held
/// arrow key are gestures; a change with no gesture in progress (a screen reader or automation setting the value) is its own
/// gesture and is committed at once.
/// </summary>
internal sealed class GestureCommit
{
    private bool _pointer;
    private bool _key;
    private bool _dirty;

    /// <summary>The value changed. True when it should be committed now.</summary>
    internal bool Changed()
    {
        _dirty = true;
        return !_pointer && !_key && Consume();
    }

    internal void PointerDown() => _pointer = true;

    /// <summary>The pointer was released or lost. True when a change made during the gesture should be committed now.</summary>
    internal bool PointerUp()
    {
        _pointer = false;
        return Finish();
    }

    internal void KeyDown() => _key = true;

    internal bool KeyUp()
    {
        _key = false;
        return Finish();
    }

    private bool Finish() => !_pointer && !_key && Consume();

    private bool Consume()
    {
        var was = _dirty;
        _dirty = false;
        return was;
    }
}

internal static class EuclideanPreview
{
    /// <summary>One symbol per step: a filled circle for a hit and an empty one for a rest.</summary>
    internal static string Format(IEnumerable<bool> hits) => string.Join(" ", hits.Select(hit => hit ? "●" : "○"));
}

/// <summary>How the Studio changes one lane. Pure, so the rules can be tested without a window.</summary>
internal static class LaneEdits
{
    /// <summary>The project refuses a lane name longer than this.</summary>
    internal const int MaxNameLength = 256;
    internal const int MaxLanes = 8;

    /// <summary>
    /// Sets how many clicks one cycle holds. A pattern longer than the new count is cut to it (the project refuses a pattern
    /// longer than its clicks); a shorter one keeps repeating, as the metronome plays it.
    /// </summary>
    internal static void SetPulses(RhythmLane lane, int pulses)
    {
        lane.Pulses = pulses;
        if (lane.Pattern.Length > pulses) lane.Pattern = lane.Pattern.Take(pulses).ToArray();
    }

    /// <summary>
    /// Changes one beat's strength (silent, soft, normal, strong). A tap wraps round; the arrow keys stop at the ends. The
    /// whole pattern is written out, so the beats around it keep the strength they showed. False when the lane has no such beat
    /// (the number of clicks was lowered a moment ago and the strip has not caught up): nothing is changed.
    /// </summary>
    internal static bool ChangeAccent(RhythmLane lane, int step, int delta, bool wrap)
    {
        var accents = AccentPatterns.ExpandLane(lane.Pattern, Math.Max(lane.Pattern.Length, lane.Pulses));
        if (step < 0 || step >= accents.Length) return false;
        var levels = Enum.GetValues<Accent>().Length;
        var next = (int)accents[step] + delta;
        accents[step] = (Accent)(wrap ? ((next % levels) + levels) % levels : Math.Clamp(next, 0, levels - 1));
        lane.Pattern = accents;
        return true;
    }

    /// <summary>Moves every beat one step later; the last one comes round to the first. Patterns and exact positions agree.</summary>
    internal static void Rotate(RhythmLane lane)
    {
        if (lane.Events.Count == 0)
        {
            var accents = AccentPatterns.ExpandLane(lane.Pattern, lane.Pulses);
            lane.Pattern = accents.TakeLast(1).Concat(accents.SkipLast(1)).ToArray();
            return;
        }
        var cycle = Rational.FromDouble(lane.CycleQuarters);
        foreach (var item in lane.Events)
        {
            var shifted = item.PositionQuarters + cycle / lane.Pulses;
            item.PositionQuarters = shifted - new Rational((shifted / cycle).Floor(), 1) * cycle;
        }
        lane.Events.Sort((left, right) => left.PositionQuarters.CompareTo(right.PositionQuarters));
    }

    /// <summary>Clicks become rests and rests become clicks.</summary>
    internal static void Invert(RhythmLane lane)
    {
        if (lane.Events.Count == 0)
            lane.Pattern = AccentPatterns.ExpandLane(lane.Pattern, lane.Pulses)
                .Select(accent => accent == Accent.Silent ? Accent.Normal : Accent.Silent).ToArray();
        else
            foreach (var item in lane.Events)
                item.Accent = item.Accent == Accent.Silent ? Accent.Normal : Accent.Silent;
    }

    /// <summary>The first "Lane n" that no lane has, so a name is never given twice after a lane was removed.</summary>
    internal static string NextName(IEnumerable<string> existing, string baseName)
    {
        var used = existing.ToHashSet(StringComparer.Ordinal);
        for (var number = 1; ; number++)
        {
            var name = $"{baseName} {number}";
            if (!used.Contains(name)) return name;
        }
    }

    /// <summary>The name of a copy: the original with a suffix, cut to fit the longest name the project accepts.</summary>
    internal static string CopyName(string name, string suffix) =>
        (name.Length + suffix.Length <= MaxNameLength ? name : name[..(MaxNameLength - suffix.Length)]) + suffix;
}
