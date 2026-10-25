namespace ResonantPulse.Core;

/// <summary>Stable, localizable identifiers with fresh mutable documents on every call. Display names are English fallback text.</summary>
public sealed record LibraryPreset(string Id, string Name, string Description, PulseDocument Document);

public static class BuiltInLibrary
{
    public static IReadOnlyList<LibraryPreset> Presets()
    {
        var compound = new PulseDocument
        {
            Name = "Compound 6/8",
            Settings = new() { Tempo = 80, BeatsPerBar = 6, BeatDenominator = 8, BeatUnitQuarters = 1.5,
                Subdivision = 3, Accents = [Accent.Strong, Accent.Normal], Groups = [3, 3] }
        };
        var additive = new PulseDocument
        {
            Name = "Seven: 2+2+3",
            Settings = new() { Tempo = 180, BeatsPerBar = 7, BeatDenominator = 8, BeatUnitQuarters = 0.5,
                Accents = [Accent.Normal], Groups = [2, 2, 3] }
        };
        var poly = new PulseDocument { Name = "Three against four", Lanes = [new() { Name = "Three", Pulses = 3, CycleQuarters = 4 }] };
        var polymeter = new PulseDocument
        {
            Name = "Three and four quarter meters", Settings = new() { Accents = [Accent.Silent] },
            Lanes = [new() { Name = "3/4", Pulses = 3, CycleQuarters = 3 }, new() { Name = "4/4", Pulses = 4, CycleQuarters = 4, Sound = "Wood", Pan = -0.3f }]
        };
        var euclidean = new PulseDocument { Name = "Five of eight", Lanes = [RhythmGenerators.Euclidean(5, 8, 4)] };
        var paradiddle = new PulseDocument { Name = "Paradiddle", Lanes = RhythmGenerators.RudimentLanes(RudimentKind.Paradiddle).ToList() };
        return new[]
        {
            new LibraryPreset("open", "Open practice", "Quarter = 120, 4/4, one count-in bar.", new()),
            new LibraryPreset("compound-6-8", compound.Name, "Dotted-quarter = 80: two primary pulses and six eighth positions per bar.", compound),
            new LibraryPreset("additive-7-8", additive.Name, "Seven eighths grouped 2+2+3; group starts are accented.", additive),
            new LibraryPreset("swing", "Paired swing", "2:1 swing of paired eighth subdivisions.", new() { Name = "Paired swing", Settings = new() { Subdivision = 2, Swing = 2d / 3 } }),
            new LibraryPreset("polyrhythm-3-4", poly.Name, "Three and four attacks over the same four-quarter cycle.", poly),
            new LibraryPreset("polymeter-3-4", polymeter.Name, "Independent three- and four-quarter cycles; common downbeat every twelve quarters.", polymeter),
            new LibraryPreset("euclidean-5-8", euclidean.Name, "Original evenly distributed five-attack study over eight positions.", euclidean),
            new LibraryPreset("paradiddle", paradiddle.Name, "Original right/left sixteen-stroke sticking study.", paradiddle)
        };
    }
    public static IReadOnlyList<LibraryPreset> PracticePrograms()
    {
        var ladder = new PulseDocument { Name = "Warmup ladder", Program = TempoPrograms.Ladder(80, 112, 4) };
        var gaps = new PulseDocument
        {
            Name = "Internal time",
            Settings = new() { BlindGaps = true },
            Program = [new() { Name = "Two on, two off", StartTempo = 96, EndTempo = 96, Bars = 32, AudibleBars = 2, SilentBars = 2 }]
        };
        var ramps = new PulseDocument
        {
            Name = "Ramp domains",
            Program =
            [
                new() { Name = "Linear in beats", StartTempo = 60, EndTempo = 120, Bars = 8, Ramp = "LinearBeat" },
                new() { Name = "Linear in time", StartTempo = 120, EndTempo = 60, Bars = 8, Ramp = "LinearTime" },
                new() { Name = "Recover", StartTempo = 80, EndTempo = 80, Bars = 8 }
            ]
        };
        var omissions = new PulseDocument
        {
            Name = "Seeded density", Seed = 2026, Settings = new() { Accents = [Accent.Silent], BlindGaps = true },
            Lanes = [new() { Name = "Half-density pulse", Probability = 0.5, Sound = "Wood" }]
        };
        return new[]
        {
            new LibraryPreset("warmup-ladder", ladder.Name, "+4 BPM every eight bars, 80 through 112.", ladder),
            new LibraryPreset("internal-time", gaps.Name, "Two audible bars, two blind silent bars, eight cycles.", gaps),
            new LibraryPreset("ramp-domains", ramps.Name, "Beat-linear acceleration, time-linear deceleration, recovery.", ramps),
            new LibraryPreset("seeded-density", omissions.Name, "Independent 50% click omissions; performer targets remain intact.", omissions)
        };
    }
}
