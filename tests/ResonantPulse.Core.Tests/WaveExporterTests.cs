using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class WaveExporterTests
{
    [TestMethod]
    public async Task Export_HeaderAndLengths_EncodeFiniteStereoPcm16()
    {
        using var temp = new TempDirectory(); var p = Check.Plan();
        await WaveExporter.ExportAsync(temp.File("out.wav"), p);
        byte[] b = await File.ReadAllBytesAsync(temp.File("out.wav"));
        Assert.AreEqual("RIFF", Encoding.ASCII.GetString(b, 0, 4));
        Assert.AreEqual("WAVEfmt ", Encoding.ASCII.GetString(b, 8, 8));
        Assert.AreEqual((uint)(36 + p.TotalFrames * 4), BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4)));
        Assert.AreEqual(16u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(16)));
        Assert.AreEqual((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(20)));
        Assert.AreEqual((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(22)));
        Assert.AreEqual(8000u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(24)));
        Assert.AreEqual(32000u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(28)));
        Assert.AreEqual((ushort)4, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(32)));
        Assert.AreEqual((ushort)16, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(34)));
        Assert.AreEqual("data", Encoding.ASCII.GetString(b, 36, 4));
        Assert.AreEqual(64000u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(40)));
        Assert.AreEqual(64044, b.Length); Assert.AreNotEqual(0L, p.TotalFrames % 4096); temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_SelectedSamples_UsePlanGainAndAwayFromZeroPcmScaling()
    {
        using var temp = new TempDirectory();
        var p = Check.Plan(Check.LaneDocument(pan: -0.25f));
        await WaveExporter.ExportAsync(temp.File("samples.wav"), p);
        byte[] b = await File.ReadAllBytesAsync(temp.File("samples.wav"));
        var floats = Check.Render(p, 400, gain: p.DefaultMasterGain);
        Assert.IsTrue(floats.Any(x => x > 0.001) && floats.Any(x => x < -0.001));
        foreach (int i in new[] { 0, 1, 8, 9, 20, 21, 40, 41, 100, 101, 250, 251, 798, 799 })
        {
            short expected = checked((short)Math.Round(floats[i] * 32767f, MidpointRounding.AwayFromZero));
            Assert.AreEqual(expected, BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(44 + i * 2)));
        }
        Assert.AreEqual(0.3f, p.DefaultMasterGain); temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_FiniteEndpoint_DoesNotWrapOrExtendTail()
    {
        using var temp = new TempDirectory();
        var d = Check.LaneDocument(position: new Rational(399, 100));
        var p = Check.Plan(d); await WaveExporter.ExportAsync(temp.File("finite.wav"), p);
        byte[] b = await File.ReadAllBytesAsync(temp.File("finite.wav"));
        Assert.AreEqual(44L + 4 * p.TotalFrames, b.LongLength);
        var end = Check.Render(p, 100, p.TotalFrames - 50, gain: p.DefaultMasterGain);
        Assert.IsTrue(end.Take(100).Any(x => x != 0)); Assert.IsTrue(end.Skip(100).All(x => x == 0));
        for (int i = 0; i < 100; i++)
            Assert.AreEqual((short)Math.Round(end[i] * 32767f, MidpointRounding.AwayFromZero),
                BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(b.Length - 200 + i * 2)));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_Manifest_ContainsTempoMeterSeedAndGapFacts()
    {
        using var temp = new TempDirectory();
        var d = Check.Document(); d.Seed = 73; d.Settings.CountInBars = 1; d.Settings.BeatUnitQuarters = 1.5;
        d.Settings.BeatsPerBar = 6; d.Settings.BeatDenominator = 8; d.Settings.Tempo = 80;
        d.Settings.AudibleBars = d.Settings.SilentBars = 1;
        var p = Check.Plan(d, 2, countIn: true);
        await WaveExporter.ExportAsync(temp.File("meta.wav"), p);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(temp.File("meta.wav.manifest.json")));
        var r = json.RootElement;
        Assert.AreEqual(1, r.GetProperty("SchemaVersion").GetInt32()); Assert.AreEqual("PCM16 stereo", r.GetProperty("Format").GetString());
        Assert.AreEqual(p.TotalFrames, r.GetProperty("TotalFrames").GetInt64());
        Assert.AreEqual(p.CountInFrames, r.GetProperty("CountInFrames").GetInt64());
        Assert.AreEqual(1.5, r.GetProperty("BeatUnitQuarters").GetDouble()); Assert.AreEqual(73, r.GetProperty("Seed").GetInt32());
        Assert.AreEqual("event-hash-v1", r.GetProperty("ProbabilityAlgorithm").GetString());
        Assert.IsFalse(r.GetProperty("Loop").GetBoolean());
        Assert.AreEqual("truncate at finite duration", r.GetProperty("TailPolicy").GetString());
        Assert.AreEqual(80d, r.GetProperty("TempoSegments")[0].GetProperty("StartTempo").GetDouble());
        Assert.AreEqual(6, r.GetProperty("SectionBoundaries")[1].GetProperty("BeatsPerBar").GetInt32());
        Assert.AreEqual(8, r.GetProperty("SectionBoundaries")[1].GetProperty("BeatDenominator").GetInt32());
        Assert.AreEqual(p.SilentRanges.Single().StartFrame, r.GetProperty("SilentRanges")[0].GetProperty("StartFrame").GetInt64());
        Assert.AreEqual(p.Onsets.Count, r.GetProperty("AudibleOnsets").GetArrayLength()); temp.NoTemps();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Export_PreCanceled_PreservesBothTargetsAndCleansTemps(bool existing)
    {
        using var temp = new TempDirectory(); string path = temp.File("cancel.wav");
        if (existing) { await File.WriteAllTextAsync(path, "wave sentinel"); await File.WriteAllTextAsync(path + ".manifest.json", "manifest sentinel"); }
        await Check.ThrowsAsync<OperationCanceledException>(() => WaveExporter.ExportAsync(path, Check.Plan(), new CancellationToken(true)));
        if (existing)
        {
            Assert.AreEqual("wave sentinel", await File.ReadAllTextAsync(path));
            Assert.AreEqual("manifest sentinel", await File.ReadAllTextAsync(path + ".manifest.json"));
        }
        else { Assert.IsFalse(File.Exists(path)); Assert.IsFalse(File.Exists(path + ".manifest.json")); }
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_RiffSizeOverflow_RejectsBeforeWritingLargeData()
    {
        using var temp = new TempDirectory(); string path = temp.File("huge.wav");
        await File.WriteAllTextAsync(path, "retained"); await File.WriteAllTextAsync(path + ".manifest.json", "retained manifest");
        var d = Check.Document(); d.Settings.Tempo = 1; d.Settings.BeatUnitQuarters = 16;
        var p = Check.Plan(d, 5760, 192000); // 1440 primary pulses, not billions of rendered samples.
        Assert.IsTrue(p.TotalFrames * 4 > uint.MaxValue - 36L);
        var error = await Check.ThrowsAsync<ArgumentException>(() => WaveExporter.ExportAsync(path, p));
        Assert.IsTrue(error.Message.Contains("RIFF", StringComparison.Ordinal));
        Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        Assert.AreEqual("retained manifest", await File.ReadAllTextAsync(path + ".manifest.json")); temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_ManifestDirectoryPreflight_PreservesWaveBeforeCommit()
    {
        using var temp = new TempDirectory(); string path = temp.File("preflight.wav");
        await File.WriteAllTextAsync(path, "sentinel"); Directory.CreateDirectory(path + ".manifest.json");
        await Check.ThrowsAsync<ArgumentException>(() => WaveExporter.ExportAsync(path, Check.Plan()));
        Assert.AreEqual("sentinel", await File.ReadAllTextAsync(path));
        Assert.IsTrue(Directory.Exists(path + ".manifest.json")); temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_InvalidLocalPath_ReportsErrorWithoutProducingArtifacts()
    {
        using var temp = new TempDirectory();
        await Check.ThrowsAsync<ArgumentException>(() => WaveExporter.ExportAsync(temp.File("invalid:stream"), Check.Plan()));
        await Check.ThrowsAsync<ArgumentNullException>(() => WaveExporter.ExportAsync(temp.File("null.wav"), null!));
        Assert.AreEqual(0, Directory.GetFiles(temp.Root).Length); temp.NoTemps();
    }
}
