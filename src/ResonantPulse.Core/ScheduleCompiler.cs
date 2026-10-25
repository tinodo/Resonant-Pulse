using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ResonantPulse.Core;

/// <summary>Pure finite compiler. Absolute rational positions are integrated once, then rounded to nearest sample (ties away from zero).</summary>
public static class ScheduleCompiler
{
    public static RenderPlan Compile(PulseDocument document, int sampleRate, int bars = 16, bool includeCountIn = true)
    {
        DocumentValidator.Validate(document);
        DocumentValidator.Require(sampleRate is >= 8000 and <= 192000, "Sample rate must be 8000..192000 Hz.");
        DocumentValidator.Require(bars is >= 1 and <= 100_000, "Compile bars must be 1..100000.");
        var s = document.Settings;
        var sections = Resolve(document, bars, includeCountIn);
        double duration = sections[^1].StartSeconds + sections[^1].DurationSeconds;
        DocumentValidator.Require(duration is >= 0.1 and <= 86400, "Resolved duration must be 0.1 seconds..24 hours. Reduce program size or density.");
        long total = checked((long)Math.Round(duration * sampleRate, MidpointRounding.AwayFromZero));
        var notes = new List<MusicalOnset>();
        var targets = new List<TargetOnset>();
        var silence = new List<(long, long)>();
        var tempos = new List<TempoSegment>();
        long countIn = 0;
        bool solo = document.Lanes.Any(l => l.Solo && !l.Muted);
        int opportunities = 0;
        Rational unit = Rational.FromDouble(s.BeatUnitQuarters);
        Rational swing = Rational.FromDouble(s.Swing);
        foreach (var section in sections)
        {
            long startFrame = section.FrameAt(0, sampleRate), endFrame = section.FrameAt(section.Length, sampleRate);
            DocumentValidator.Require(endFrame > startFrame, "A section is shorter than one sample; use a lower tempo or longer section.");
            tempos.Add(new() { StartFrame = startFrame, EndFrame = endFrame, StartTempo = section.B0, EndTempo = section.B1 });
            if (section.CountIn) countIn = endFrame;
            for (int bar = 0; bar < section.Bars; bar++)
            {
                Rational barStart = section.BarLength * bar;
                bool gap = !section.CountIn && section.SilentBars > 0 &&
                    bar % (section.AudibleBars + section.SilentBars) >= section.AudibleBars;
                if (gap)
                {
                    long a = section.FrameAt(barStart, sampleRate), b = section.FrameAt(barStart + section.BarLength, sampleRate);
                    if (silence.Count > 0 && silence[^1].Item2 == a) silence[^1] = (silence[^1].Item1, b);
                    else silence.Add((a, b));
                }
            }
            // Primary pulses keep their selected-unit spacing even when a unit crosses a bar boundary.
            // Authored section starts reset the primary pulse; lanes preserve their absolute phase.
            for (Rational pulse = 0; pulse < section.Length; pulse += unit)
            {
                int bar = checked((int)(pulse / section.BarLength).Floor());
                Rational withinBar = pulse - section.BarLength * bar;
                int pulseIndex = checked((int)(withinBar / unit).Floor());
                Accent accent = s.Accents.Length == 0 ? Accent.Normal : s.Accents[pulseIndex % s.Accents.Length];
                if (section.CountIn) accent = withinBar == 0 ? Accent.Strong : Accent.Normal;
                if (s.Groups.Length > 0 && section.Numerator == s.BeatsPerBar && section.Denominator == s.BeatDenominator)
                {
                    int boundary = 0;
                    foreach (int group in s.Groups)
                    {
                        if (withinBar == new Rational(4 * boundary, section.Denominator)) accent = Accent.Strong;
                        boundary += group;
                    }
                }
                Add(pulse, -1, accent, 1, 0, s.Sound, 1, GapAt(pulse) || (!section.CountIn && solo), false);
                if (section.CountIn) continue;
                for (int sub = 1; sub < s.Subdivision; sub++)
                {
                    Rational offset = s.Swing == 0.5 || sub % 2 == 0
                        ? unit * new Rational(sub, s.Subdivision)
                        : unit * (new Rational(sub - 1, s.Subdivision) + new Rational(2, s.Subdivision) * swing);
                    if (pulse + offset < section.Length)
                        Add(pulse + offset, -2, Accent.Soft, 0.65f, 0, s.Sound, 1, GapAt(pulse + offset) || solo, false);
                }
            }
            if (section.CountIn) continue;
            for (int laneIndex = 0; laneIndex < document.Lanes.Count; laneIndex++)
            {
                var lane = document.Lanes[laneIndex];
                Rational cycle = Rational.FromDouble(lane.CycleQuarters), offset = Rational.FromDouble(lane.OffsetQuarters);
                int eventCount = lane.Events.Count == 0 ? lane.Pulses : lane.Events.Count;
                for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
                {
                    var authored = lane.Events.Count == 0 ? null : lane.Events[eventIndex];
                    Rational position = authored?.PositionQuarters ?? cycle * new Rational(eventIndex, lane.Pulses);
                    Rational origin = offset + position;
                    Rational firstCycle = new(((section.Start - origin) / cycle).Ceiling(), 1);
                    for (Rational q = origin + firstCycle * cycle; q < section.Start + section.Length; q += cycle)
                    {
                        Rational local = q - section.Start;
                        int bar = checked((int)(local / section.BarLength).Floor());
                        bool gap = section.SilentBars > 0 && bar % (section.AudibleBars + section.SilentBars) >= section.AudibleBars;
                        Accent accent = authored?.Accent ?? (lane.Pattern.Length == 0 ? (eventIndex == 0 ? Accent.Strong : Accent.Normal) : lane.Pattern[eventIndex % lane.Pattern.Length]);
                        Add(local, laneIndex, accent, lane.Volume * (authored?.Gain ?? 1), lane.Pan, lane.Sound,
                            lane.Probability * (authored?.Probability ?? 1), gap || lane.Muted || (solo && !lane.Solo),
                            authored?.Tie ?? false);
                    }
                }
            }

            bool GapAt(Rational local)
            {
                if (section.CountIn || section.SilentBars == 0) return false;
                int bar = checked((int)(local / section.BarLength).Floor());
                return bar % (section.AudibleBars + section.SilentBars) >= section.AudibleBars;
            }
            void Add(Rational local, int lane, Accent accent, float gain, float pan, string sound, double probability, bool suppressed, bool tie)
            {
                if (++opportunities > DocumentValidator.MaximumOnsets)
                    throw new ArgumentException("Schedule exceeds one million onset opportunities; shorten the export or reduce subdivision/lane density.");
                if (accent == Accent.Silent || tie) return;
                long frame = section.FrameAt(local, sampleRate);
                // Rounding at the exclusive boundary may otherwise create an event in the next section.
                DocumentValidator.Require(frame < endFrame, "An onset rounds onto a section endpoint; use a higher sample rate or reduce density.");
                bool audible = !suppressed && probability > 0 && (probability >= 1 ||
                    Probability(document.Seed, section.StepIndex, section.RepeatIndex, lane, section.Start + local) < probability);
                if (!section.CountIn) targets.Add(new(frame, lane, accent, audible));
                if (!audible) return;
                var onset = new ScheduledOnset { Frame = frame, LaneIndex = lane, Accent = accent, Gain = gain, Pan = pan, Sound = sound };
                notes.Add(new(section.Start + local, onset));
            }
        }
        notes.Sort((a, b) => { int c = a.Onset.Frame.CompareTo(b.Onset.Frame); return c != 0 ? c : a.Onset.LaneIndex.CompareTo(b.Onset.LaneIndex); });
        targets.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        // Sliding and circular-loop budgets, including silent performer targets, not merely average density.
        ValidateWindow(targets.Select(t => t.Frame).Concat(notes.Where(n => n.Quarter < 0).Select(n => n.Onset.Frame)).Order().ToArray(),
            sampleRate, total, DocumentValidator.MaximumOnsetsPerSecond, "More than 4096 target/click onsets occur in a one-second window.");
        ValidateWindow(notes.Select(n => n.Onset.Frame).ToArray(), (int)Math.Ceiling(sampleRate * 0.05), total, 256,
            "More than 256 audible voices overlap in a 50 ms window. Reduce density/tempo or mute lanes.");
        return new(sampleRate, total, notes.Select(n => n.Onset).ToArray(), tempos.ToArray(), silence.ToArray(),
            targets.ToArray(), countIn, s.MasterGain, notes.ToArray(), sections.ToArray(), s.BeatUnitQuarters, document.Seed, document.ProbabilityAlgorithm);
    }

    internal static List<ResolvedSection> Resolve(PulseDocument d, int bars, bool countIn)
    {
        var list = new List<ResolvedSection>();
        var s = d.Settings;
        var first = d.Program.FirstOrDefault();
        int numerator = first?.BeatsPerBar ?? s.BeatsPerBar, denominator = first?.BeatDenominator ?? s.BeatDenominator;
        double initial = first?.StartTempo ?? s.Tempo;
        Rational q = 0;
        double seconds = 0;
        if (countIn && s.CountInBars > 0)
        {
            Rational length = new Rational(4 * numerator, denominator) * s.CountInBars;
            var section = new ResolvedSection("Count-in", -1, 0, -length, length, 0, initial, initial, s.BeatUnitQuarters,
                "Constant", s.CountInBars, numerator, denominator, 0, 0, true);
            list.Add(section); seconds = section.DurationSeconds;
        }
        if (d.Program.Count == 0)
            Append(d.Name, 0, 0, bars, s.BeatsPerBar, s.BeatDenominator, s.Tempo, s.Tempo, "Constant", s.AudibleBars, s.SilentBars);
        else
            for (int i = 0; i < d.Program.Count; i++)
            {
                var p = d.Program[i];
                for (int repeat = 0; repeat < p.Repeats; repeat++)
                    Append(p.Name, i, repeat, p.Bars, p.BeatsPerBar, p.BeatDenominator, p.StartTempo, p.EndTempo, p.Ramp, p.AudibleBars, p.SilentBars);
            }
        return list;
        void Append(string name, int step, int repeat, int nBars, int n, int den, double b0, double b1, string ramp, int audible, int silent)
        {
            Rational length = new Rational(4 * n, den) * nBars;
            var section = new ResolvedSection(name, step, repeat, q, length, seconds, b0, b1, s.BeatUnitQuarters, ramp, nBars, n, den, audible, silent, false);
            list.Add(section); q += length; seconds += section.DurationSeconds;
        }
    }
    private static void ValidateWindow(long[] frames, int window, long total, int maximum, string message)
    {
        if (frames.Length == 0) return;
        if (total < window)
        {
            DocumentValidator.Require(frames.Length * Math.Ceiling((double)window / total) <= maximum, message + " The loop is very short.");
            return;
        }
        int left = 0;
        // Visit two cycles without materializing a duplicated array. The first cycle checks linear playback;
        // the second checks the end-to-start seam, and therefore also bounds tail overlap in the callback.
        for (int i = 0; i < frames.Length * 2; i++)
        {
            long at = frames[i % frames.Length] + (i >= frames.Length ? total : 0);
            while (at - (frames[left % frames.Length] + (left >= frames.Length ? total : 0)) >= window) left++;
            DocumentValidator.Require(i - left + 1 <= maximum, message);
        }
    }
    /// <summary>
    /// event-hash-v1: SHA256 of ASCII "RP-EVENT-1\0", four signed int32 little-endian
    /// (seed, step index, repeat index, lane index), then UTF8 reduced absolute q ("n/d").
    /// Uniform value is the first uint64 little-endian, shifted right 11, divided by 2^53.
    /// </summary>
    public static double Probability(int seed, int stepIndex, int repeatIndex, int laneIndex, Rational quarter)
    {
        byte[] coordinate = Encoding.UTF8.GetBytes(quarter.ToString());
        byte[] input = new byte[27 + coordinate.Length];
        Encoding.ASCII.GetBytes("RP-EVENT-1\0").CopyTo(input, 0);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(11), seed);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(15), stepIndex);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(19), repeatIndex);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(23), laneIndex);
        coordinate.CopyTo(input, 27);
        return (BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(input)) >> 11) / 9007199254740992d;
    }
}
