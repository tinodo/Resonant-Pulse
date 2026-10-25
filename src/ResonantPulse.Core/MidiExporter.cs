using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace ResonantPulse.Core;

/// <summary>Preflight for the conservative exact-position MIDI subset. Continuous nonconstant ramps are explicitly rejected, not approximated.</summary>
public sealed record MidiExportReport(bool CanExport, int PulsesPerQuarter, double MaximumTimingErrorSeconds,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Notices);

/// <summary>Standard MIDI File type 0, exact finite PPQ, seeded audible realization, conductor meter/quarter-tempo events.</summary>
public static class MidiExporter
{
    public static MidiExportReport Preflight(PulseDocument document, int bars = 16)
    {
        try
        {
            var plan = ScheduleCompiler.Compile(document, 48000, bars);
            return Inspect(document, plan);
        }
        catch (ArgumentException e) { return new(false, 0, 0, new[] { e.Message }, Array.Empty<string>()); }
    }
    public static async Task ExportAsync(string path, PulseDocument document, int bars = 16, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = ScheduleCompiler.Compile(document, 48000, bars);
        var report = Inspect(document, plan);
        if (!report.CanExport) throw new ArgumentException("MIDI export preflight failed: " + string.Join(" ", report.Errors));
        int ppq = report.PulsesPerQuarter;
        Rational origin = plan.Sections[0].Start;
        long Tick(Rational q) => checked((long)((q - origin) * ppq).Numerator);
        var events = new List<MidiEvent>();
        int serial = 0;
        void Meta(long tick, int priority, byte type, byte[] data)
        {
            using var payload = new MemoryStream();
            payload.WriteByte(0xff); payload.WriteByte(type); WriteVariable(payload, data.Length); payload.Write(data);
            events.Add(new(tick, priority, serial++, payload.ToArray()));
        }
        Meta(0, 0, 3, Encoding.UTF8.GetBytes(document.Name));
        Meta(0, 0, 1, Encoding.UTF8.GetBytes("ResonantPulse schema1; seeded audible realization; GM sounds approximate; source=" +
            JsonSerializer.Serialize(document, DocumentStore.JsonOptions)));
        Meta(0, 0, 1, Encoding.UTF8.GetBytes($"PPQ={ppq}; exact onset tick coordinates; maximum tempo-rounding error bound={report.MaximumTimingErrorSeconds:R}s; continuous ramps not approximated."));
        foreach (var section in plan.Sections)
        {
            long tick = Tick(section.Start);
            int micros = checked((int)Math.Round(60_000_000 / (section.B0 * section.Unit), MidpointRounding.AwayFromZero));
            Meta(tick, 0, 0x51, [(byte)(micros >> 16), (byte)(micros >> 8), (byte)micros]);
            int logDenominator = BitOperations.Log2((uint)section.Denominator);
            Meta(tick, 0, 0x58, [(byte)section.Numerator, (byte)logDenominator, 24, 8]);
            Meta(tick, 0, 6, Encoding.UTF8.GetBytes(section.Name));
        }
        foreach (var group in plan.MusicalOnsets.GroupBy(n => n.Onset.LaneIndex))
        {
            int lane = group.Key, channel = Channel(lane);
            var ordered = group.OrderBy(n => n.Quarter).ToArray();
            string sound = ordered[0].Onset.Sound;
            byte program = sound switch { "Wood" => 115, "Glass" => 10, "Kick" => 118, "HiHat" => 119, _ => 112 };
            events.Add(new(0, 1, serial++, [(byte)(0xc0 | channel), program]));
            int pan = (int)Math.Round((ordered[0].Onset.Pan + 1) * 63.5);
            events.Add(new(0, 1, serial++, [(byte)(0xb0 | channel), 10, (byte)pan]));
            for (int i = 0; i < ordered.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var n = ordered[i];
                long tick = Tick(n.Quarter);
                long next = i + 1 < ordered.Length ? Tick(ordered[i + 1].Quarter) : Tick(plan.Sections[^1].Start + plan.Sections[^1].Length);
                long off = Math.Min(next, tick + Math.Max(1, ppq / 32));
                float accent = n.Onset.Accent switch { Accent.Strong => 1f, Accent.Normal => 0.72f, _ => 0.42f };
                int velocity = Math.Clamp((int)Math.Round(127 * accent * n.Onset.Gain * document.Settings.MasterGain), 1, 127);
                if (n.Onset.Gain == 0 || document.Settings.MasterGain == 0) continue;
                events.Add(new(tick, 3, serial++, [(byte)(0x90 | channel), 60, (byte)velocity]));
                events.Add(new(off, 2, serial++, [(byte)(0x80 | channel), 60, 0]));
            }
        }
        long totalTick = Tick(plan.Sections[^1].Start + plan.Sections[^1].Length);
        Meta(totalTick, 9, 0x2f, []);
        events.Sort((a, b) => { int c = a.Tick.CompareTo(b.Tick); if (c != 0) return c; c = a.Priority.CompareTo(b.Priority); return c != 0 ? c : a.Serial.CompareTo(b.Serial); });
        using var track = new MemoryStream();
        long previous = 0;
        foreach (var e in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteVariable(track, e.Tick - previous); track.Write(e.Data); previous = e.Tick;
        }
        await AtomicFile.WriteAsync(path, async (stream, token) =>
        {
            var header = new byte[22];
            Encoding.ASCII.GetBytes("MThd").CopyTo(header, 0);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 6);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), 0);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), 1);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(12), (ushort)ppq);
            Encoding.ASCII.GetBytes("MTrk").CopyTo(header, 14);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(18), checked((uint)track.Length));
            await stream.WriteAsync(header, token).ConfigureAwait(false);
            track.Position = 0;
            await track.CopyToAsync(stream, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
    private static MidiExportReport Inspect(PulseDocument d, RenderPlan plan)
    {
        var errors = new List<string>();
        var notices = new List<string>
        {
            "Exports the finite seeded audible realization, including count-in and gaps; rules remain in source metadata.",
            "Global conductor meter only; additive groups, lane polymeter, selected BPM unit and sound names remain in source metadata.",
            "General MIDI timbres approximate synthesized Core sounds. Pan and velocity use MIDI's 7-bit resolution; audio gain is not acoustically equivalent.",
            "Note gates are at most 1/32 quarter (at least one tick), clipped at the next same-voice attack; MIDI instruments do not reproduce PCM envelopes.",
            "Continuous nonconstant ramps are not approximated. Use WAV/project export for those programs."
        };
        BigInteger ppq = 24;
        Rational origin = plan.Sections[0].Start;
        void Include(Rational q)
        {
            if (ppq > 32767) return;
            BigInteger den = (q - origin).Denominator;
            ppq = ppq / BigInteger.GreatestCommonDivisor(ppq, den) * den;
        }
        foreach (var s in plan.Sections) { Include(s.Start); Include(s.Start + s.Length); }
        foreach (var n in plan.MusicalOnsets) Include(n.Quarter);
        if (ppq > 32767) errors.Add("Exact onset coordinates require PPQ above 32767. Use exact rational authored lane events for ratios such as 2:1 swing, simplify decimal offsets, or export WAV; no quantization is silently applied.");
        double error = 0;
        foreach (var s in plan.Sections)
        {
            if (s.B0 != s.B1) errors.Add($"Section '{s.Name}' has a continuous ramp. Export WAV/project or author explicit constant-tempo steps.");
            double exact = 60_000_000 / (s.B0 * s.Unit);
            double rounded = Math.Round(exact, MidpointRounding.AwayFromZero);
            if (rounded is < 1 or > 0xffffff)
                errors.Add($"Section '{s.Name}' quarter-note tempo {s.B0 * s.Unit:R} BPM exceeds the MIDI 24-bit tempo field. Change the explicit unit/tempo or export WAV.");
            error += Math.Abs(rounded - exact) * s.Length.ToDouble() / 1_000_000;
        }
        if (error > 0.001) errors.Add($"Tempo-field rounding can accumulate {error * 1000:F3} ms (>1 ms). Export shorter sections or WAV.");
        if (ppq <= 32767)
        {
            var total = (plan.Sections[^1].Start + plan.Sections[^1].Length - origin) * new Rational(ppq, 1);
            if (total.Numerator > 0x0fffffff) errors.Add("MIDI duration exceeds the supported 28-bit tick range. Export shorter sections.");
            foreach (var lane in plan.MusicalOnsets.GroupBy(n => n.Onset.LaneIndex))
            {
                Rational? previous = null;
                foreach (var n in lane.OrderBy(n => n.Quarter))
                {
                    if (previous == n.Quarter) { errors.Add($"Lane {lane.Key} has simultaneous attacks on one MIDI voice. Split them into separate lanes or export WAV."); break; }
                    previous = n.Quarter;
                }
            }
        }
        return new(errors.Count == 0, ppq <= 32767 ? (int)ppq : 0, error, errors.AsReadOnly(), notices.AsReadOnly());
    }
    private static int Channel(int lane)
    {
        int channel = lane + 2;
        return channel >= 9 ? channel + 1 : channel;
    }
    private static void WriteVariable(Stream stream, long value)
    {
        if (value is < 0 or > 0x0fffffff) throw new ArgumentException("MIDI delta exceeds the 28-bit variable-length limit.");
        Span<byte> bytes = stackalloc byte[4];
        int i = 3; bytes[i] = (byte)(value & 0x7f);
        while ((value >>= 7) != 0) bytes[--i] = (byte)((value & 0x7f) | 0x80);
        stream.Write(bytes[i..]);
    }
    private sealed record MidiEvent(long Tick, int Priority, int Serial, byte[] Data);
}
