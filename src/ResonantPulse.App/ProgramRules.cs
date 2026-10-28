using System.Globalization;
using System.Numerics;
using System.Text;
using ResonantPulse.Core;

namespace ResonantPulse.App;

/// <summary>How the Studio changes a practice program. Pure, so the rules can be tested without a window.</summary>
internal static class ProgramEdits
{
    /// <summary>The project refuses a program with more steps than this.</summary>
    internal const int MaxSteps = 1024;
    internal const int MaxBars = DocumentValidator.MaximumProgramBars;
    /// <summary>Steps are counted once for every repeat when the project works out how many sections a program has.</summary>
    internal const int MaxSections = 4096;
    internal const int DefaultAudibleBars = 2;
    internal const int DefaultSilentBars = 2;

    /// <summary>How the tempo of a step behaves: it stays the same, or changes evenly with the seconds or with the beats.</summary>
    internal static readonly string[] Ramps = ["Constant", "LinearTime", "LinearBeat"];

    /// <summary>A new step starts from the project's own tempo and time signature, so adding one never changes how it sounds.</summary>
    internal static ProgramStep NewStep(PulseSettings settings, string name) => new()
    {
        Name = name, StartTempo = settings.Tempo, EndTempo = settings.Tempo,
        BeatsPerBar = settings.BeatsPerBar, BeatDenominator = settings.BeatDenominator
    };

    /// <summary>The tempo behaviour of a step: it stays the same, or moves from one tempo to another.</summary>
    internal static bool IsSteady(ProgramStep step) => IsSteady(step.Ramp);

    internal static bool IsSteady(string ramp) => ramp == "Constant";

    /// <summary>
    /// Chooses how the tempo behaves. A steady step has one tempo, which the project insists on (start and end are equal), so
    /// choosing it makes the end tempo the start tempo. Choosing a ramp keeps both, so the step sounds the same until the end
    /// tempo is changed.
    /// </summary>
    internal static void SetRamp(ProgramStep step, string ramp)
    {
        step.Ramp = ramp;
        if (ramp == "Constant") step.EndTempo = step.StartTempo;
    }

    /// <summary>The first (or, for a steady step, the only) tempo.</summary>
    internal static void SetStartTempo(ProgramStep step, double tempo)
    {
        step.StartTempo = tempo;
        if (IsSteady(step)) step.EndTempo = tempo;
    }

    /// <summary>The tempo a ramp ends on. A steady step has no such tempo; it is left alone.</summary>
    internal static void SetEndTempo(ProgramStep step, double tempo)
    {
        if (!IsSteady(step)) step.EndTempo = tempo;
    }

    /// <summary>
    /// "Leave bars silent": on plays 2 bars and leaves 2 silent; off plays every bar. The project accepts either both numbers
    /// above zero or both zero, so they always change together.
    /// </summary>
    internal static void SetSilentBars(ProgramStep step, bool on)
    {
        step.AudibleBars = on ? DefaultAudibleBars : 0;
        step.SilentBars = on ? DefaultSilentBars : 0;
    }

    internal static bool HasSilentBars(ProgramStep step) => step.AudibleBars > 0 && step.SilentBars > 0;

    /// <summary>With additive groups (2+2+3) the project's own time signature is the only one a step may have.</summary>
    internal static bool MeterIsFixed(PulseSettings settings) => settings.Groups.Length > 0;

    /// <summary>
    /// The text key of the limit a program breaks, or null. The project counts a step once for every repeat and refuses more
    /// than <see cref="MaxSections"/> of them, or more than <see cref="MaxBars"/> bars in all.
    /// </summary>
    internal static string? BrokenLimit(IReadOnlyList<ProgramStep> program)
    {
        if (program.Count > MaxSteps) return "StepLimit";
        long sections = 0, bars = 0;
        foreach (var step in program)
        {
            sections += step.Repeats;
            bars += (long)step.Bars * step.Repeats;
        }
        return sections > MaxSections || bars > MaxBars ? "ProgramTooLong" : null;
    }

    /// <summary>Swaps the step with its neighbour. False when there is none in that direction.</summary>
    internal static bool Move(IList<ProgramStep> program, int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= program.Count || target < 0 || target >= program.Count || delta == 0) return false;
        (program[index], program[target]) = (program[target], program[index]);
        return true;
    }

    /// <summary>The copy goes right after the original, named after it and cut to fit the longest name the project accepts.</summary>
    internal static ProgramStep Copy(ProgramStep step, string suffix) => step with { Name = LaneEdits.CopyName(step.Name, suffix) };
}

/// <summary>How the Studio places the clicks of a lane at exact positions. Pure, so the rules can be tested without a window.</summary>
internal static class PositionEdits
{
    /// <summary>The project refuses a lane with more positions than this.</summary>
    internal const int MaxPositions = 256;

    /// <summary>The project refuses a position whose numerator or denominator needs more bits than this.</summary>
    internal const int MaxBits = 256;

    /// <summary>
    /// True when the project accepts the position: it is an exact value, and 1e-100 is exact too, but a fraction that long is
    /// refused when the project is checked, so it is refused here with a reason instead.
    /// </summary>
    internal static bool FitsProject(Rational position) =>
        position.Numerator.GetBitLength() <= MaxBits && position.Denominator.GetBitLength() <= MaxBits;

    /// <summary>
    /// Where a new position goes: the middle of the longest gap between the positions (the cycle wraps back to the first), so
    /// it lands where there is the most room and never on a position that is taken.
    /// </summary>
    internal static Rational Midpoint(Rational cycle, IEnumerable<Rational> positions)
    {
        var sorted = positions.Order().ToArray();
        if (sorted.Length == 0) return Rational.Zero;
        var bestStart = Rational.Zero;
        var bestLength = sorted[0];
        for (var index = 0; index < sorted.Length; index++)
        {
            var length = (index + 1 < sorted.Length ? sorted[index + 1] : cycle) - sorted[index];
            if (length <= bestLength) continue;
            bestStart = sorted[index];
            bestLength = length;
        }
        return bestStart + bestLength / 2;
    }

    /// <summary>
    /// Reads a typed position: a whole number, a fraction (1/3) or a decimal, with a point or the language's own decimal
    /// separator (0.5 or 0,5). The value is kept exact, never rounded to a grid.
    /// </summary>
    internal static bool TryParse(string? text, CultureInfo culture, out Rational position)
    {
        position = Rational.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var typed = text.Trim();
        var separator = culture.NumberFormat.NumberDecimalSeparator;
        // The language's separator is read as a decimal point only when it can be nothing else: one of them, and no point.
        if (separator != "." && !typed.Contains('.') && typed.Split(separator).Length == 2)
            typed = typed.Replace(separator, ".", StringComparison.Ordinal);
        try
        {
            position = Rational.Parse(typed);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or DivideByZeroException)
        {
            return false;
        }
    }

    /// <summary>
    /// The text key of what is wrong with a position in a lane, or null: it must lie in [0, cycle), be no more precise than the
    /// project allows, and not be taken.
    /// </summary>
    internal static string? Problem(Rational position, Rational cycle, IEnumerable<Rational> others)
    {
        if (position < Rational.Zero || position >= cycle) return "PositionRange";
        if (!FitsProject(position)) return "PositionPrecise";
        return others.Contains(position) ? "PositionTaken" : null;
    }

    /// <summary>
    /// A position as short text in the language's own number format: "3", "0.375" ("0,375"), or "1/3" when it has no finite
    /// decimal form.
    /// </summary>
    internal static string Format(Rational value, CultureInfo culture)
    {
        var denominator = value.Denominator;
        if (denominator.IsOne) return value.Numerator.ToString(CultureInfo.InvariantCulture);
        // A fraction has a finite decimal form exactly when its denominator has no prime factors but 2 and 5.
        int twos = 0, fives = 0;
        var rest = denominator;
        while (rest.IsEven) { rest /= 2; twos++; }
        while ((rest % 5).IsZero) { rest /= 5; fives++; }
        var places = Math.Max(twos, fives);
        if (!rest.IsOne || places > 9) return value.ToString();
        var scaled = value.Numerator * BigInteger.Pow(10, places) / denominator;
        var digits = BigInteger.Abs(scaled).ToString(CultureInfo.InvariantCulture).PadLeft(places + 1, '0');
        var text = (digits[..^places] + "." + digits[^places..]).TrimEnd('0').TrimEnd('.');
        if (culture.NumberFormat.NumberDecimalSeparator != ".")
            text = text.Replace(".", culture.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal);
        return scaled.Sign < 0 ? "-" + text : text;
    }

    /// <summary>One click per pattern step, spread evenly over the cycle with the strength the pattern gave it.</summary>
    internal static List<RhythmEvent> FromPattern(RhythmLane lane)
    {
        var accents = AccentPatterns.ExpandLane(lane.Pattern, lane.Pulses);
        var cycle = Rational.FromDouble(lane.CycleQuarters);
        return Enumerable.Range(0, lane.Pulses).Select(index => new RhythmEvent
        {
            PositionQuarters = cycle * new Rational(index, lane.Pulses),
            Accent = accents[index]
        }).ToList();
    }

    /// <summary>
    /// Removes a position. The project reads a lane with no positions as an even pattern, so removing the last one leaves one
    /// silent click, which keeps the lane quiet instead of making it play.
    /// </summary>
    internal static void Remove(RhythmLane lane, int index)
    {
        lane.Events.RemoveAt(index);
        if (lane.Events.Count > 0) return;
        lane.Pulses = 1;
        lane.Pattern = [Accent.Silent];
    }

    /// <summary>The positions in time order, as the rest of the Studio keeps them.</summary>
    internal static void Sort(List<RhythmEvent> events) =>
        events.Sort((left, right) => left.PositionQuarters.CompareTo(right.PositionQuarters));
}

/// <summary>A sentence with numbered places for boxes ("Play {0} bars, then {1} bars silent"), cut into its words and its places.</summary>
internal static class SentenceTemplate
{
    /// <summary>Words to show, or (when <see cref="Text"/> is null) the place of box number <see cref="Slot"/>.</summary>
    internal readonly record struct Part(string? Text, int Slot)
    {
        internal static Part Words(string text) => new(text, -1);
        internal static Part Place(int slot) => new(null, slot);
    }

    /// <summary>
    /// Cuts the sentence at its places, in the order the language puts them: a language that says the second number first gets
    /// its box first. Only single-digit places ({0} to {9}) are places; any other brace is part of the words.
    /// </summary>
    internal static IReadOnlyList<Part> Split(string template)
    {
        var parts = new List<Part>();
        var words = new StringBuilder();
        for (var index = 0; index < template.Length; index++)
        {
            if (template[index] == '{' && index + 2 < template.Length && char.IsAsciiDigit(template[index + 1]) && template[index + 2] == '}')
            {
                if (words.Length > 0) parts.Add(Part.Words(words.ToString()));
                words.Clear();
                parts.Add(Part.Place(template[index + 1] - '0'));
                index += 2;
                continue;
            }
            words.Append(template[index]);
        }
        if (words.Length > 0) parts.Add(Part.Words(words.ToString()));
        return parts;
    }
}
