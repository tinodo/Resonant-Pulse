using System.Text.Json.Serialization;

namespace ResonantPulse.Core;

public enum Accent { Silent = 0, Soft = 1, Normal = 2, Strong = 3 }

/// <summary>Tempo is in BeatUnitQuarters units/minute, independent of the written meter.</summary>
public sealed record PulseSettings
{
    public double Tempo { get; set; } = 120;
    public int BeatsPerBar { get; set; } = 4;
    public int BeatDenominator { get; set; } = 4;
    public double BeatUnitQuarters { get; set; } = 1;
    public int Subdivision { get; set; } = 1;
    public double Swing { get; set; } = 0.5;
    public int CountInBars { get; set; } = 1;
    /// <summary>Live practice duration after count-in; zero disables the timer. Pauses do not consume audio frames.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PracticeSeconds { get; set; }
    /// <summary>Live stop after written denominator-unit beats, excluding count-in; zero disables.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PracticeBeats { get; set; }
    /// <summary>Live stop after complete authored bars, excluding count-in; zero disables.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PracticeBars { get; set; }
    public Accent[] Accents { get; set; } = [Accent.Strong, Accent.Normal, Accent.Normal, Accent.Normal];
    public int AudibleBars { get; set; }
    public int SilentBars { get; set; }
    public bool BlindGaps { get; set; } = true;
    public float MasterGain { get; set; } = 0.5f;
    public string Sound { get; set; } = "Wood";
    /// <summary>Optional additive groups in denominator units; must sum to BeatsPerBar.</summary>
    public int[] Groups { get; set; } = [];
}

/// <summary>Even pulses over an independent cycle, or exact Events when supplied. Offsets preserve absolute phase across program steps.</summary>
public sealed record RhythmLane
{
    public string Name { get; set; } = "Lane";
    public int Pulses { get; set; } = 4;
    public double CycleQuarters { get; set; } = 4;
    public double OffsetQuarters { get; set; }
    public float Volume { get; set; } = 0.7f;
    public float Pan { get; set; }
    public bool Muted { get; set; }
    public bool Solo { get; set; }
    public double Probability { get; set; } = 1;
    public Accent[] Pattern { get; set; } = [];
    public string Sound { get; set; } = "Glass";
    public List<RhythmEvent> Events { get; set; } = [];
}

/// <summary>An attack at a rational position inside a lane cycle. Tie suppresses a new attack; it does not synthesize sustain.</summary>
public sealed record RhythmEvent
{
    public Rational PositionQuarters { get; set; }
    public Accent Accent { get; set; } = Accent.Normal;
    public double Probability { get; set; } = 1;
    public float Gain { get; set; } = 1;
    public bool Tie { get; set; }
}

/// <summary>Bars/repeats are finite. Each repeat restarts its tempo ramp; lane phase remains absolute.</summary>
public sealed record ProgramStep
{
    public string Name { get; set; } = "Section";
    public double StartTempo { get; set; } = 80;
    public double EndTempo { get; set; } = 80;
    public int Bars { get; set; } = 8;
    public int Repeats { get; set; } = 1;
    public string Ramp { get; set; } = "Constant";
    public int AudibleBars { get; set; }
    public int SilentBars { get; set; }
    public int BeatsPerBar { get; set; } = 4;
    public int BeatDenominator { get; set; } = 4;
}

public sealed record SongEntry
{
    public string Name { get; set; } = "";
    public PulseDocument Document { get; set; } = new();
    public string Notes { get; set; } = "";
}

/// <summary>Version-one local project. Setlist entries are armed/compiled individually, not implicitly concatenated.</summary>
public sealed record PulseDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Open practice";
    public PulseSettings Settings { get; set; } = new();
    public List<RhythmLane> Lanes { get; set; } = [];
    public List<ProgramStep> Program { get; set; } = [];
    public List<SongEntry> Setlist { get; set; } = [];
    public int Seed { get; set; } = 1;
    public string ProbabilityAlgorithm { get; set; } = "event-hash-v1";
}

public sealed record ScheduledOnset
{
    public long Frame { get; init; }
    public int LaneIndex { get; init; }
    public Accent Accent { get; init; }
    public float Gain { get; init; }
    public float Pan { get; init; }
    public string Sound { get; init; } = "Wood";
}

public sealed record TempoSegment
{
    public long StartFrame { get; init; }
    public long EndFrame { get; init; }
    public double StartTempo { get; init; }
    public double EndTempo { get; init; }
}

/// <summary>A performer target is retained even when probability, mute/solo or a gap suppresses its sound.</summary>
public sealed record TargetOnset(long Frame, int LaneIndex, Accent Accent, bool Audible);

/// <summary>Presentation-only projection, not an audio clock. Indices are zero-based; count-in BarIndex is negative. Tempo uses the selected BPM unit.</summary>
public sealed record PlaybackPosition(double QuarterPosition, int BarIndex, int SectionBarIndex, int BeatIndex,
    double BeatPhase, double PulsePhase, double Tempo, string SectionName, int ProgramStepIndex, int RepeatIndex,
    bool IsCountIn, bool IsSilent);

/// <summary>Immutable compiled revision. Construct through ScheduleCompiler to prepare audio outside the callback.</summary>
public sealed class RenderPlan
{
    public int SampleRate { get; }
    public long TotalFrames { get; }
    public IReadOnlyList<ScheduledOnset> Onsets { get; }
    public IReadOnlyList<TempoSegment> TempoSegments { get; }
    public IReadOnlyList<(long StartFrame, long EndFrame)> SilentRanges { get; }
    public IReadOnlyList<TargetOnset> Targets { get; }
    public long CountInFrames { get; }
    /// <summary>Authored lanes, including lanes with no audible attacks or targets.</summary>
    public int LaneCount { get; }
    public float DefaultMasterGain { get; }
    public double BeatUnitQuarters { get; }
    public int Seed { get; }
    public string ProbabilityAlgorithm { get; }
    public IReadOnlyList<SectionBoundary> SectionBoundaries { get; }
    internal RenderVoice[] Voices { get; }
    internal MusicalOnset[] MusicalOnsets { get; }
    internal ResolvedSection[] Sections { get; }
    internal int TailFrames { get; }
    internal RenderPlan(int rate, long total, ScheduledOnset[] onsets, TempoSegment[] tempos,
        (long, long)[] silence, TargetOnset[] targets, long countIn, float gain,
        MusicalOnset[] musical, ResolvedSection[] sections, double beatUnit, int seed, string probabilityAlgorithm, int laneCount)
    {
        SampleRate = rate; TotalFrames = total; CountInFrames = countIn; DefaultMasterGain = gain;
        BeatUnitQuarters = beatUnit; Seed = seed; ProbabilityAlgorithm = probabilityAlgorithm;
        LaneCount = laneCount;
        Onsets = Array.AsReadOnly(onsets); TempoSegments = Array.AsReadOnly(tempos);
        SilentRanges = Array.AsReadOnly(silence); Targets = Array.AsReadOnly(targets);
        MusicalOnsets = musical; Sections = sections;
        SectionBoundaries = Array.AsReadOnly(sections.Select(s => new SectionBoundary(s.Name, s.StepIndex, s.RepeatIndex,
            s.FrameAt(0, rate), s.FrameAt(s.Length, rate), s.Start, s.Length, s.Bars, s.Numerator, s.Denominator, s.Ramp)).ToArray());
        (Voices, TailFrames) = ClickCache.Prepare(onsets, rate);
    }
    /// <summary>Maps a canonical quarter coordinate (negative during count-in) to an absolute frame.</summary>
    public long FrameAtQuarter(Rational quarter)
    {
        foreach (var s in Sections)
            if (quarter >= s.Start && quarter < s.Start + s.Length) return s.FrameAt(quarter - s.Start, SampleRate);
        if (quarter == Sections[^1].Start + Sections[^1].Length) return TotalFrames;
        throw new ArgumentOutOfRangeException(nameof(quarter), "Position is outside the resolved plan.");
    }
    /// <summary>Inverts the resolved ramp. Accepts 0..TotalFrames; callers modulo looping cursors. IsSilent identifies gap masks, not mute/probability.</summary>
    public PlaybackPosition PositionAtFrame(long frame)
    {
        if (frame < 0 || frame > TotalFrames) throw new ArgumentOutOfRangeException(nameof(frame));
        int index = 0, completedBars = 0;
        while (index < Sections.Length - 1 && frame >= SectionBoundaries[index].EndFrame)
        {
            if (!Sections[index].CountIn) completedBars += Sections[index].Bars;
            index++;
        }
        var section = Sections[index];
        double local = section.QuarterAtSeconds(Math.Clamp((double)frame / SampleRate - section.StartSeconds, 0, section.DurationSeconds));
        if (frame == TotalFrames) local = section.Length.ToDouble();
        else if (frame == SectionBoundaries[index].StartFrame) local = 0;
        double barLength = section.BarLength.ToDouble(), beatLength = 4d / section.Denominator;
        int bar = Math.Min(section.Bars - 1, Math.Max(0, (int)Math.Floor(local / barLength)));
        double withinBar = Math.Clamp(local - bar * barLength, 0, barLength);
        double beats = withinBar / beatLength, pulses = local / section.Unit;
        bool silent = false;
        int lo = 0, hi = SilentRanges.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (SilentRanges[mid].EndFrame <= frame) lo = mid + 1; else hi = mid;
        }
        if (lo < SilentRanges.Count) silent = SilentRanges[lo].StartFrame <= frame && frame < SilentRanges[lo].EndFrame;
        double tempo = section.TempoAtQuarter(local);
        return new(section.Start.ToDouble() + local, section.CountIn ? bar - section.Bars : completedBars + bar,
            bar, Math.Min(section.Numerator - 1, (int)Math.Floor(beats)), beats - Math.Floor(beats),
            pulses - Math.Floor(pulses), tempo, section.Name, section.StepIndex, section.RepeatIndex, section.CountIn, silent);
    }
}

internal sealed record MusicalOnset(Rational Quarter, ScheduledOnset Onset);
