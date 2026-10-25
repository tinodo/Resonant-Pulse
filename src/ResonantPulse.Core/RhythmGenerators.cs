namespace ResonantPulse.Core;

public enum RudimentKind { SingleStroke, DoubleStroke, Paradiddle, Flam, Drag }
public enum StickingHand { Right, Left }
public sealed record RudimentStroke(Rational PositionQuarters, StickingHand Hand, Accent Accent);

/// <summary>Original algorithmic study patterns, not transcriptions of copyrighted exercises.</summary>
public static class RhythmGenerators
{
    /// <summary>Evenly distributes attacks over steps using an integer bucket, then rotates right. No timing-grid rounding is performed.</summary>
    public static RhythmLane Euclidean(int attacks, int steps, Rational cycleQuarters, int rotation = 0)
    {
        DocumentValidator.Require(steps is >= 1 and <= 256 && attacks >= 0 && attacks <= steps, "Euclidean attacks must be 0..steps; steps must be 1..256.");
        DocumentValidator.CheckRational(cycleQuarters);
        DocumentValidator.Require(cycleQuarters > 0 && cycleQuarters <= 4096, "Euclidean cycle must be positive and at most 4096 quarters.");
        // Contract cycle is a double. Reject any cycle whose decimal round-trip would change exact authored semantics.
        double cycle = cycleQuarters.ToDouble();
        DocumentValidator.Require(Rational.FromDouble(cycle) == cycleQuarters,
            "This cycle is not exactly representable by CycleQuarters' decimal contract. Use a decimal cycle with rational event positions.");
        var lane = new RhythmLane { Name = $"Euclidean {attacks}/{steps}", Pulses = steps, CycleQuarters = cycle, Pattern = [] };
        int rotate = ((rotation % steps) + steps) % steps;
        for (int i = 0; i < steps; i++)
        {
            bool hit = attacks > 0 && (i == 0 || (i * attacks) % steps < attacks);
            int target = (i + rotate) % steps;
            lane.Events.Add(new() { PositionQuarters = cycleQuarters * new Rational(target, steps), Accent = hit ? (i == 0 ? Accent.Strong : Accent.Normal) : Accent.Silent });
        }
        lane.Events.Sort((a, b) => a.PositionQuarters.CompareTo(b.PositionQuarters));
        return lane;
    }
    /// <summary>Sixteenth-note sticking studies over four quarters. Flam/drag grace notes wrap into the preceding cycle; initial playback has no negative-time pickup.</summary>
    public static IReadOnlyList<RudimentStroke> Rudiment(RudimentKind kind)
    {
        DocumentValidator.Require(Enum.IsDefined(kind), "Unknown rudiment.");
        string hands = kind switch
        {
            RudimentKind.DoubleStroke => "RRLLRRLLRRLLRRLL",
            RudimentKind.Paradiddle => "RLRRLRLLRLRRLRLL",
            _ => "RLRLRLRLRLRLRLRL"
        };
        var strokes = new List<RudimentStroke>();
        for (int i = 0; i < hands.Length; i++)
        {
            Rational position = new(i, 4);
            var hand = hands[i] == 'R' ? StickingHand.Right : StickingHand.Left;
            strokes.Add(new(position, hand, i % 4 == 0 ? Accent.Strong : Accent.Normal));
            int graceCount = kind == RudimentKind.Flam ? 1 : kind == RudimentKind.Drag ? 2 : 0;
            for (int grace = 1; grace <= graceCount; grace++)
            {
                Rational q = position - new Rational(grace, 32);
                if (q < 0) q += 4;
                strokes.Add(new(q, hand == StickingHand.Right ? StickingHand.Left : StickingHand.Right, Accent.Soft));
            }
        }
        strokes.Sort((a, b) => a.PositionQuarters.CompareTo(b.PositionQuarters));
        return strokes.AsReadOnly();
    }
    public static IReadOnlyList<RhythmLane> RudimentLanes(RudimentKind kind)
    {
        var strokes = Rudiment(kind);
        return new[] { StickingHand.Right, StickingHand.Left }.Select(hand => new RhythmLane
        {
            Name = $"{kind}: {hand}", CycleQuarters = 4, Pan = hand == StickingHand.Right ? 0.25f : -0.25f,
            Sound = hand == StickingHand.Right ? "Wood" : "Glass",
            Events = strokes.Where(s => s.Hand == hand).Select(s => new RhythmEvent { PositionQuarters = s.PositionQuarters, Accent = s.Accent }).ToList()
        }).ToArray();
    }
}

/// <summary>Finite program helpers. Every step has explicit meter and selected-unit BPM.</summary>
public static class TempoPrograms
{
    public static List<ProgramStep> Ladder(double start, double end, double increment, int barsPerStep = 8)
    {
        DocumentValidator.FiniteRange(start, 1, 1000, "Ladder start"); DocumentValidator.FiniteRange(end, 1, 1000, "Ladder end");
        DocumentValidator.Require(double.IsFinite(increment) && increment != 0 && Math.Sign(end - start) == Math.Sign(increment),
            "Ladder increment must be nonzero and point toward a different endpoint.");
        DocumentValidator.Require(barsPerStep is >= 1 and <= 1000, "Ladder bars per step must be 1..1000.");
        Rational a = Rational.FromDouble(start), b = Rational.FromDouble(end), delta = Rational.FromDouble(increment);
        var steps = new List<ProgramStep>();
        for (Rational tempo = a; delta > 0 ? tempo <= b : tempo >= b; tempo += delta)
        {
            DocumentValidator.Require(steps.Count < 1024, "Ladder exceeds 1024 steps; increase increment.");
            double value = tempo.ToDouble();
            steps.Add(new() { Name = $"Tempo {value:R}", StartTempo = value, EndTempo = value, Bars = barsPerStep });
        }
        if (steps[^1].StartTempo != end) steps.Add(new() { Name = $"Tempo {end:R}", StartTempo = end, EndTempo = end, Bars = barsPerStep });
        DocumentValidator.Require(steps.Count <= 1024, "Ladder exceeds 1024 steps.");
        return steps;
    }
}
