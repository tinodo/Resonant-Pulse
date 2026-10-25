using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class MidiExporterTests
{
    [TestMethod]
    public void Preflight_SupportedDocument_ReportsExactGridAndDisclosures()
    {
        var r = MidiExporter.Preflight(Check.Document(), 1);
        Assert.IsTrue(r.CanExport); Assert.AreEqual(24, r.PulsesPerQuarter);
        Assert.AreEqual(0, r.Errors.Count); Assert.AreEqual(0d, r.MaximumTimingErrorSeconds);
        Assert.IsTrue(r.Notices.Any(n => n.Contains("General MIDI", StringComparison.Ordinal)));
        Assert.IsTrue(r.Notices.Any(n => n.Contains("7-bit", StringComparison.Ordinal)));
        Assert.IsTrue(r.Notices.Any(n => n.Contains("ramps", StringComparison.Ordinal)));
        var direct = new MidiExportReport(false, 120, 0.0002, new[] { "failure" }, new[] { "notice" });
        Assert.IsFalse(direct.CanExport); Assert.AreEqual(120, direct.PulsesPerQuarter);
        Assert.AreEqual(0.0002, direct.MaximumTimingErrorSeconds);
        Assert.AreEqual("failure", direct.Errors.Single()); Assert.AreEqual("notice", direct.Notices.Single());
    }

    [TestMethod]
    public async Task Export_HeaderTrackDeltasAndEndpoint_FormsFiniteTypeZeroSmf()
    {
        using var temp = new TempDirectory(); string path = temp.File("simple.mid");
        await MidiExporter.ExportAsync(path, Check.Document(), 1);
        var file = MidiReader.Read(await File.ReadAllBytesAsync(path));
        Assert.AreEqual(24, file.Ppq); Assert.AreEqual(96L, file.Events[^1].Tick);
        var notes = file.Events.Where(e => (e.Status & 0xf0) == 0x90).ToArray();
        Check.Sequence(new long[] { 0, 24, 48, 72 }, notes.Select(e => e.Tick));
        Assert.IsTrue(notes.All(e => e.Data[0] == 60 && e.Data[1] > 0));
        Assert.AreEqual(4, file.Events.Count(e => (e.Status & 0xf0) == 0x80));
        Assert.IsTrue(file.Events.Zip(file.Events.Skip(1), (a, b) => a.Tick <= b.Tick).All(x => x)); temp.NoTemps();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Export_QuarterAndDottedQuarterTempoAndMeters_EncodeCorrectMetaEvents(bool dotted)
    {
        using var temp = new TempDirectory(); var d = Check.Document();
        if (dotted)
        {
            d.Settings.Tempo = 80; d.Settings.BeatUnitQuarters = 1.5;
            d.Settings.BeatsPerBar = 6; d.Settings.BeatDenominator = 8;
        }
        await MidiExporter.ExportAsync(temp.File("tempo.mid"), d, 1);
        var file = MidiReader.Read(await File.ReadAllBytesAsync(temp.File("tempo.mid")));
        Check.Sequence(new byte[] { 0x07, 0xa1, 0x20 }, file.Events.Single(e => e.MetaType == 0x51 && e.Status == 0xff).Data);
        Check.Sequence(dotted ? new byte[] { 6, 3, 24, 8 } : new byte[] { 4, 2, 24, 8 },
            file.Events.Single(e => e.MetaType == 0x58 && e.Status == 0xff).Data);
        Assert.AreEqual(dotted ? 72L : 96L, file.Events[^1].Tick);
        string source = file.Events.Where(e => e.Status == 0xff && e.MetaType == 1)
            .Select(e => Encoding.UTF8.GetString(e.Data)).Single(s => s.Contains("source=", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(source[(source.IndexOf("source=", StringComparison.Ordinal) + 7)..]);
        Assert.AreEqual(dotted ? 1.5 : 1d, json.RootElement.GetProperty("Settings").GetProperty("BeatUnitQuarters").GetDouble());
        temp.NoTemps();
    }

    [TestMethod]
    public void Preflight_RationalGrid_UsesExactLcmAndRejectsUnrepresentablePpq()
    {
        var d = Check.LaneDocument(position: new Rational(1, 5));
        var fifth = MidiExporter.Preflight(d, 1);
        Assert.IsTrue(fifth.CanExport); Assert.AreEqual(120, fifth.PulsesPerQuarter);
        d.Lanes[0].Events[0].PositionQuarters = new Rational(1, 4095);
        var upper = MidiExporter.Preflight(d, 1);
        Assert.IsTrue(upper.CanExport); Assert.AreEqual(32760, upper.PulsesPerQuarter);
        d.Lanes[0].Events[0].PositionQuarters = new Rational(1, 32771);
        var rejected = MidiExporter.Preflight(d, 1);
        Assert.IsFalse(rejected.CanExport); Assert.AreEqual(0, rejected.PulsesPerQuarter);
        Assert.IsTrue(rejected.Errors.Any(e => e.Contains("PPQ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Export_CountInGapsAndNoteOrdering_MatchesExactFiniteSchedule()
    {
        using var temp = new TempDirectory();
        var d = Check.LaneDocument(pan: -1, volume: 0.5f); d.Seed = 7; d.Settings.MasterGain = 0.5f;
        d.Settings.CountInBars = 1; d.Settings.AudibleBars = d.Settings.SilentBars = 1;
        d.Lanes[0].Events = [new() { PositionQuarters = 0 }, new() { PositionQuarters = new Rational(1, 24) }];
        await MidiExporter.ExportAsync(temp.File("ordered.mid"), d, 2);
        var file = MidiReader.Read(await File.ReadAllBytesAsync(temp.File("ordered.mid")));
        Assert.AreEqual(24, file.Ppq); Assert.AreEqual(288L, file.Events[^1].Tick);
        var attacks = file.Events.Where(e => e.Status == 0x92).ToArray();
        Check.Sequence(new long[] { 96, 97 }, attacks.Select(e => e.Tick));
        Assert.IsTrue(attacks.All(e => e.Data[0] == 60 && e.Data[1] == 23));
        Check.Sequence(new byte[] { 10, 0 }, file.Events.Single(e => e.Status == 0xb2).Data);
        Check.Sequence(new byte[] { 10 }, file.Events.Single(e => e.Status == 0xc2).Data);
        var at97 = file.Events.Where(e => e.Tick == 97 && (e.Status == 0x82 || e.Status == 0x92)).ToArray();
        Check.Sequence(new byte[] { 0x82, 0x92 }, at97.Select(e => e.Status));
        Check.Sequence(new long[] { 0, 24, 48, 72 }, file.Events.Where(e => e.Status == 0x91).Select(e => e.Tick));
        Assert.IsFalse(file.Events.Any(e => (e.Status & 0xf0) == 0x90 && e.Tick >= 192));
        string source = file.Events.Where(e => e.Status == 0xff && e.MetaType == 1)
            .Select(e => Encoding.UTF8.GetString(e.Data)).Single(s => s.Contains("source=", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(source[(source.IndexOf("source=", StringComparison.Ordinal) + 7)..]);
        Assert.AreEqual(7, json.RootElement.GetProperty("Seed").GetInt32()); temp.NoTemps();
    }

    [TestMethod]
    public void Preflight_RampsTempoAndTimingError_RejectsUnsupportedSubset()
    {
        foreach (string ramp in new[] { "LinearTime", "LinearBeat" })
        {
            var d = Check.Document(); d.Program = [new() { Bars = 1, StartTempo = 60, EndTempo = 120, Ramp = ramp }];
            var r = MidiExporter.Preflight(d, 1);
            Assert.IsFalse(r.CanExport); Assert.IsTrue(r.Errors.Any(e => e.Contains("ramp", StringComparison.OrdinalIgnoreCase)));
            d.Program[0].EndTempo = 60; Assert.IsTrue(MidiExporter.Preflight(d, 1).CanExport);
        }
        var low = Check.Document(); low.Settings.Tempo = 1;
        Assert.IsTrue(MidiExporter.Preflight(low, 1).Errors.Any(e => e.Contains("24-bit", StringComparison.Ordinal)));
        low.Settings.Tempo = 60_000_000d / 0xffffff;
        Assert.IsTrue(MidiExporter.Preflight(low, 1).CanExport);
        // 512 quarter BPM -> exactly 117187.5 us/q: rounding error .5 us per quarter.
        var rounding = Check.Document(); rounding.Settings.Tempo = 512;
        var exact = MidiExporter.Preflight(rounding, 500);
        Assert.IsTrue(exact.CanExport); Assert.AreEqual(0.001, exact.MaximumTimingErrorSeconds, 1e-15);
        var above = MidiExporter.Preflight(rounding, 501);
        Assert.IsFalse(above.CanExport); Assert.AreEqual(0.001002, above.MaximumTimingErrorSeconds, 1e-15);
        Assert.IsTrue(above.Errors.Any(e => e.Contains(">1 ms", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Preflight_DurationTicks_RejectsBeyond28BitRangeWithSparsePlan()
    {
        var d = Check.LaneDocument(position: new Rational(1, 4095));
        d.Settings.Tempo = 1000; d.Settings.BeatUnitQuarters = 16; d.Lanes[0].CycleQuarters = 4096;
        var below = MidiExporter.Preflight(d, 2048);
        Assert.IsTrue(below.CanExport); Assert.AreEqual(32760, below.PulsesPerQuarter);
        var above = MidiExporter.Preflight(d, 2049);
        Assert.IsFalse(above.CanExport);
        Assert.IsTrue(above.Errors.Any(e => e.Contains("28-bit", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Export_PreCanceledAndFailedPreflight_PreserveSentinelAndCleanTemps(bool existing)
    {
        using var temp = new TempDirectory(); string path = temp.File("safe.mid");
        if (existing) await File.WriteAllTextAsync(path, "retained");
        await Check.ThrowsAsync<OperationCanceledException>(() =>
            MidiExporter.ExportAsync(path, Check.Document(), 1, new CancellationToken(true)));
        var bad = Check.Document(); bad.Settings.Tempo = 1;
        await Check.ThrowsAsync<ArgumentException>(() => MidiExporter.ExportAsync(path, bad, 1));
        if (existing) Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        else Assert.IsFalse(File.Exists(path));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Preflight_InvalidDocument_ReturnsErrorsWhileExportThrows()
    {
        using var temp = new TempDirectory();
        var d = Check.Document(); d.Settings.Subdivision = 0;
        var r = MidiExporter.Preflight(d);
        Assert.IsFalse(r.CanExport); Assert.AreEqual(0, r.PulsesPerQuarter);
        Assert.IsTrue(r.Errors.Single().Contains("Subdivision", StringComparison.Ordinal));
        await Check.ThrowsAsync<ArgumentException>(() => MidiExporter.ExportAsync(temp.File("bad.mid"), d));
        Assert.IsFalse(File.Exists(temp.File("bad.mid"))); temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_ExistingTarget_ReplacesCompleteSmfAndCleansTemps()
    {
        using var temp = new TempDirectory(); string path = temp.File("replace.mid");
        await File.WriteAllTextAsync(path, "sentinel");
        await MidiExporter.ExportAsync(path, Check.Document(), 1);
        var file = MidiReader.Read(await File.ReadAllBytesAsync(path));
        Assert.AreEqual(96L, file.Events[^1].Tick); Assert.AreEqual(24, file.Ppq);
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length); temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_InvalidLocalPath_ReportsErrorWithoutProducingArtifacts()
    {
        using var temp = new TempDirectory();
        await Check.ThrowsAsync<ArgumentException>(() => MidiExporter.ExportAsync(temp.File("invalid:stream"), Check.Document(), 1));
        Assert.AreEqual(0, Directory.GetFiles(temp.Root).Length); temp.NoTemps();
    }
}
