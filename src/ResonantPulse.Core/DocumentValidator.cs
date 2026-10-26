using System.Diagnostics.CodeAnalysis;

namespace ResonantPulse.Core;

/// <summary>Shared import/compiler validation. Limits are safety budgets, not measured playback guarantees.</summary>
public static class DocumentValidator
{
    public const int MaximumOnsets = 1_000_000;
    public const int MaximumOnsetsPerSecond = 4096;
    public const int MaximumDocumentBytes = 4 * 1024 * 1024;
    public const int MaximumProgramBars = 100_000;
    public static void Validate(PulseDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int count = 0;
        Visit(document, new HashSet<PulseDocument>(ReferenceEqualityComparer.Instance), 0, ref count);
    }
    private static void Visit(PulseDocument d, HashSet<PulseDocument> path, int depth, ref int count)
    {
        Require(d is not null, "A setlist document is null.");
        Require(depth <= 8 && ++count <= 256, "Setlists exceed eight levels or 256 total documents.");
        Require(path.Add(d), "Setlists cannot contain document reference cycles.");
        Require(d.SchemaVersion == 1, $"Unsupported schema {d.SchemaVersion}; this engine reads schema 1 only.");
        Text(d.Name, "Document name", 256);
        Require(d.ProbabilityAlgorithm == "event-hash-v1", "Unsupported probability algorithm; use event-hash-v1.");
        var s = d.Settings;
        Require(s is not null, "Settings are required.");
        Tempo(s.Tempo); Meter(s.BeatsPerBar, s.BeatDenominator);
        FiniteRange(s.BeatUnitQuarters, 1d / 64, 16, "BPM note unit in quarters");
        Require(s.Subdivision is >= 1 and <= 64, "Subdivision must be 1..64.");
        FiniteRange(s.Swing, 0.5, 0.9, "Swing");
        Require(s.Swing == 0.5 || s.Subdivision % 2 == 0,
            "Swing requires paired, even subdivisions. Keep odd tuplets straight or author exact lane events.");
        Require(s.CountInBars is >= 0 and <= 32, "Count-in must be 0..32 bars.");
        Require(s.PracticeSeconds is >= 0 and <= 86400, "Practice duration must be 0..86400 seconds.");
        Require(s.PracticeBeats is >= 0 and <= 6_400_000, "Practice beats must be 0..6400000.");
        Require(s.PracticeBars is >= 0 and <= 100_000, "Practice bars must be 0..100000.");
        Pattern(s.Accents, 256, "Main accents");
        Require(s.Groups is not null && s.Groups.Length <= 64, "Groups must be an array of at most 64 entries.");
        Require(s.Groups.Length == 0 || (s.Groups.All(x => x > 0 && x <= 64) && s.Groups.Sum() == s.BeatsPerBar),
            "Additive groups must be positive denominator-unit counts summing to BeatsPerBar.");
        Gap(s.AudibleBars, s.SilentBars); FiniteRange(s.MasterGain, 0, 1, "Master gain"); Sound(s.Sound);
        Require(d.Lanes is not null && d.Lanes.Count <= 8, "At most eight lanes are supported.");
        foreach (var l in d.Lanes)
        {
            Require(l is not null, "A rhythm lane is null."); Text(l.Name, "Lane name", 256);
            Require(l.Pulses is >= 1 and <= 256, "Lane pulses must be 1..256.");
            FiniteRange(l.CycleQuarters, 1d / 1024, 4096, "Lane cycle quarters");
            FiniteRange(l.OffsetQuarters, -4096, 4096, "Lane offset quarters");
            FiniteRange(l.Volume, 0, 1, "Lane volume"); FiniteRange(l.Pan, -1, 1, "Lane pan");
            FiniteRange(l.Probability, 0, 1, "Lane probability"); Sound(l.Sound); Pattern(l.Pattern, 256, "Lane pattern");
            Require(l.Pattern.Length <= l.Pulses, "Lane Pattern cannot exceed Pulses; increase Pulses or author exact Events so no accents are discarded.");
            Require(l.Events is not null && l.Events.Count <= 256, "Lane authored events are limited to 256.");
            Require(l.Events.Count == 0 || l.Pattern.Length == 0,
                "Use either authored Events or an even-pulse Pattern, not both.");
            var positions = new HashSet<Rational>();
            foreach (var e in l.Events)
            {
                Require(e is not null, "An authored event is null.");
                CheckRational(e.PositionQuarters);
                Require(e.PositionQuarters >= 0 && e.PositionQuarters < Rational.FromDouble(l.CycleQuarters),
                    "Authored event positions must be inside [0, CycleQuarters).");
                Require(positions.Add(e.PositionQuarters), "A lane cannot contain duplicate event positions. Put simultaneous independent voices in separate lanes.");
                Require(Enum.IsDefined(e.Accent), "Unknown event accent.");
                FiniteRange(e.Probability, 0, 1, "Event probability"); FiniteRange(e.Gain, 0, 1, "Event gain");
            }
        }
        Require(d.Program is not null && d.Program.Count <= 1024, "Programs are limited to 1024 steps.");
        long expanded = 0, bars = 0;
        foreach (var p in d.Program)
        {
            Require(p is not null, "A program step is null."); Text(p.Name, "Section name", 256);
            Tempo(p.StartTempo); Tempo(p.EndTempo); Meter(p.BeatsPerBar, p.BeatDenominator);
            Require(s.Groups.Length == 0 || (p.BeatsPerBar == s.BeatsPerBar && p.BeatDenominator == s.BeatDenominator),
                "Additive Settings.Groups apply only to their authored meter. Clear Groups or use matching program meters; per-section group overrides are not supported.");
            Require(p.Bars is >= 1 and <= MaximumProgramBars && p.Repeats is >= 1 and <= 1024, "Section bars/repeats exceed finite limits.");
            expanded += p.Repeats; bars += (long)p.Bars * p.Repeats;
            Require(expanded <= 4096 && bars <= MaximumProgramBars,
                $"Expanded program exceeds 4096 sections or {MaximumProgramBars} bars.");
            Require(p.Ramp is "Constant" or "LinearTime" or "LinearBeat", "Ramp must be Constant, LinearTime or LinearBeat.");
            Require(p.Ramp != "Constant" || p.StartTempo == p.EndTempo,
                "A Constant step must have equal start/end tempos. Select a ramp to change tempo.");
            Gap(p.AudibleBars, p.SilentBars);
        }
        Require(d.Setlist is not null && d.Setlist.Count <= 128, "Setlist exceeds 128 songs.");
        foreach (var song in d.Setlist)
        {
            Require(song is not null, "A setlist song is null."); Text(song.Name, "Song name", 256); Text(song.Notes, "Song notes", 32768);
            Visit(song.Document, path, depth + 1, ref count);
        }
        path.Remove(d);
    }
    internal static void CheckRational(Rational q) =>
        Require(q.Numerator.GetBitLength() <= 256 && q.Denominator.GetBitLength() <= 256, "Authored rational exceeds 256-bit resource limit.");
    internal static void Require([DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) throw new ArgumentException(message); }
    internal static void FiniteRange(double v, double min, double max, string name) =>
        Require(double.IsFinite(v) && v >= min && v <= max, $"{name} must be finite and between {min} and {max}.");
    private static void Tempo(double v) => FiniteRange(v, 1, 1000, "Tempo");
    private static void Meter(int n, int d) => Require(n is >= 1 and <= 64 && d is >= 1 and <= 64 && (d & (d - 1)) == 0,
        "Meter numerator must be 1..64 and denominator a power of two from 1..64.");
    private static void Gap(int audible, int silent) => Require(audible is >= 0 and <= 100_000 && silent is >= 0 and <= 100_000 &&
        ((audible == 0 && silent == 0) || (audible > 0 && silent > 0)), "Gaps require both audible and silent bars positive, or both zero.");
    private static void Pattern(Accent[] p, int max, string name) =>
        Require(p is not null && p.Length <= max && p.All(x => Enum.IsDefined(x)), $"{name} contains invalid accents or exceeds {max} entries.");
    private static void Text(string text, string name, int max) => Require(text is not null && text.Length <= max, $"{name} is missing or too long.");
    internal static void Sound(string sound) => Require(sound is "Wood" or "Glass" or "Click" or "Kick" or "HiHat",
        $"Unknown sound '{sound}'. Supported: Wood, Glass, Click, Kick, HiHat; imported samples require a separate sound adapter.");
}
