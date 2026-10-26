using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class WaveStemExporterTests
{
    [TestMethod]
    public async Task Export_TracksShareOriginDurationAndMetadataAcrossCountInRampsAndMeters()
    {
        using var temp = new TempDirectory();
        var document = Check.Document();
        document.Settings.CountInBars = 1;
        document.Settings.BeatUnitQuarters = 1.5;
        document.Settings.Subdivision = 2;
        document.Settings.Swing = 0.65;
        document.Settings.PracticeSeconds = document.Settings.PracticeBeats = document.Settings.PracticeBars = 1;
        document.Lanes = [new() { Name = "../../outside.wav", Pulses = 5, CycleQuarters = 3, Pan = -1 },
            new() { Name = "Duplicate", Pulses = 7, CycleQuarters = 5, Pan = 1 }];
        document.Program = [
            new() { Name = "Compound", BeatsPerBar = 6, BeatDenominator = 8, Bars = 2,
                StartTempo = 80, EndTempo = 100, Ramp = "LinearBeat" },
            new() { Name = "Odd", BeatsPerBar = 5, BeatDenominator = 4, Bars = 1,
                StartTempo = 100, EndTempo = 100 }];
        var plan = Check.Plan(document, countIn: true);
        string path = temp.File("aligned.zip");

        await WaveStemExporter.ExportAsync(path, plan, document.Lanes.Select(lane => lane.Name).ToArray());

        using var zip = ZipFile.OpenRead(path);
        Check.Sequence(new[] { "main.wav", "lane-01.wav", "lane-02.wav", "manifest.json" },
            zip.Entries.Select(entry => entry.FullName));
        foreach (string name in new[] { "main.wav", "lane-01.wav", "lane-02.wav" })
        {
            var wave = await ReadAsync(zip, name);
            Assert.AreEqual(44L + plan.TotalFrames * 4, wave.LongLength);
            Assert.AreEqual("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
            Assert.AreEqual("WAVEfmt ", Encoding.ASCII.GetString(wave, 8, 8));
            Assert.AreEqual(1, (int)BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(20)));
            Assert.AreEqual(2, (int)BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(22)));
            Assert.AreEqual((uint)plan.SampleRate, BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(24)));
            Assert.AreEqual(16, (int)BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(34)));
            Assert.AreEqual((uint)(plan.TotalFrames * 4), BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(40)));
            var countIn = wave.Skip(44).Take(checked((int)plan.CountInFrames * 4));
            if (name == "main.wav") Assert.IsTrue(countIn.Any(value => value != 0));
            else Assert.IsTrue(countIn.All(value => value == 0));
        }
        using var manifest = JsonDocument.Parse(await ReadAsync(zip, "manifest.json"));
        var root = manifest.RootElement;
        Assert.AreEqual(plan.TotalFrames, root.GetProperty("TotalFrames").GetInt64());
        Assert.AreEqual(plan.CountInFrames, root.GetProperty("CountInFrames").GetInt64());
        Assert.AreEqual(0, root.GetProperty("OriginFrame").GetInt32());
        Assert.AreEqual(plan.TotalFrames * 12, root.GetProperty("UncompressedAudioBytes").GetInt64());
        Assert.AreEqual(document.Seed, root.GetProperty("Seed").GetInt32());
        Assert.AreEqual(document.ProbabilityAlgorithm, root.GetProperty("ProbabilityAlgorithm").GetString());
        Assert.AreEqual(plan.DefaultMasterGain, root.GetProperty("MasterGain").GetSingle());
        Assert.AreEqual(1.5, root.GetProperty("BeatUnitQuarters").GetDouble());
        Assert.IsFalse(root.GetProperty("Loop").GetBoolean());
        Assert.AreEqual(3, root.GetProperty("SectionBoundaries").GetArrayLength());
        Assert.AreEqual(6, root.GetProperty("SectionBoundaries")[1].GetProperty("BeatsPerBar").GetInt32());
        Assert.AreEqual(5, root.GetProperty("SectionBoundaries")[2].GetProperty("BeatsPerBar").GetInt32());
        Assert.AreEqual("../../outside.wav", root.GetProperty("Tracks")[1].GetProperty("Name").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("Tracks")[0].GetProperty("LaneIndex").ValueKind);
        Assert.AreEqual(1, root.GetProperty("Tracks")[2].GetProperty("LaneIndex").GetInt32());
        Assert.AreEqual(plan.Onsets.Count, root.GetProperty("AudibleOnsets").GetArrayLength());
        Assert.IsTrue(plan.TotalFrames > plan.CountInFrames + plan.SampleRate);
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_MainTrackWithSubdivisionsMatchesExistingMixedWaveWhenThereAreNoLanes()
    {
        using var temp = new TempDirectory();
        var document = Check.Document();
        document.Settings.Subdivision = 4;
        document.Settings.Swing = 0.7;
        var plan = Check.Plan(document);
        await WaveExporter.ExportAsync(temp.File("expected.wav"), plan);
        await WaveStemExporter.ExportAsync(temp.File("stems.zip"), plan, []);
        using var zip = ZipFile.OpenRead(temp.File("stems.zip"));
        Assert.AreEqual(2, zip.Entries.Count);
        Check.Sequence(await File.ReadAllBytesAsync(temp.File("expected.wav")), await ReadAsync(zip, "main.wav"));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_IsolatedLanePreservesPanGainAndTheExclusiveTailBoundary()
    {
        using var temp = new TempDirectory();
        var document = Check.LaneDocument(pan: -0.25f, position: new Rational(399, 100));
        var plan = Check.Plan(document);
        await WaveExporter.ExportAsync(temp.File("expected.wav"), plan);
        await WaveStemExporter.ExportAsync(temp.File("stems.zip"), plan, ["Lane"]);
        using var zip = ZipFile.OpenRead(temp.File("stems.zip"));
        var wave = await ReadAsync(zip, "lane-01.wav");
        Check.Sequence(await File.ReadAllBytesAsync(temp.File("expected.wav")), wave);
        Assert.IsTrue(wave.TakeLast(200).Any(value => value != 0));
        Assert.IsTrue((await ReadAsync(zip, "main.wav")).Skip(44).All(value => value == 0));
        temp.NoTemps();
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task Export_ProbabilityAndGapSamplesMatchAnIndependentlyCompiledIsolation(int laneIndex)
    {
        using var temp = new TempDirectory();
        var document = Check.Document();
        document.Seed = 763;
        document.Settings.Accents = [Accent.Silent];
        document.Settings.AudibleBars = document.Settings.SilentBars = 1;
        document.Lanes = [
            new() { Pulses = 7, CycleQuarters = 3.5, Probability = 0.37, Pan = -0.4f },
            new() { Pulses = 5, CycleQuarters = 5, Probability = 0.61, Pan = 0.7f }];
        var plan = Check.Plan(document, 4);
        var isolated = document with
        {
            Lanes = document.Lanes.Select((lane, index) => lane with { Muted = index != laneIndex }).ToList()
        };
        Assert.IsTrue(plan.Onsets.Any(onset => onset.LaneIndex == laneIndex));
        Assert.IsTrue(plan.Targets.Any(target => target.LaneIndex == laneIndex && !target.Audible));
        await WaveExporter.ExportAsync(temp.File("expected.wav"), Check.Plan(isolated, 4));
        await WaveStemExporter.ExportAsync(temp.File("stems.zip"), plan, ["First", "Second"]);
        using var zip = ZipFile.OpenRead(temp.File("stems.zip"));
        var wave = await ReadAsync(zip, laneIndex == 0 ? "lane-01.wav" : "lane-02.wav");
        Check.Sequence(await File.ReadAllBytesAsync(temp.File("expected.wav")), wave);
        foreach (var range in plan.SilentRanges)
            Assert.IsTrue(wave.Skip(checked(44 + (int)range.StartFrame * 4))
                .Take(checked((int)(range.EndFrame - range.StartFrame) * 4)).All(value => value == 0));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_SoloMuteZeroProbabilityAndSilentLanesKeepTheirAlignedTracks()
    {
        using var temp = new TempDirectory();
        var document = Check.Document();
        document.Settings.CountInBars = 1;
        document.Lanes = [new() { Solo = true, Pan = 1 }, new(), new() { Muted = true },
            new() { Probability = 0 }, new() { Pattern = [Accent.Silent] }];
        var plan = Check.Plan(document, countIn: true);
        await WaveStemExporter.ExportAsync(temp.File("stems.zip"), plan, ["Same", "Same", "", "Zero", "Silent"]);
        using var zip = ZipFile.OpenRead(temp.File("stems.zip"));
        Assert.AreEqual(7, zip.Entries.Count);
        var main = await ReadAsync(zip, "main.wav");
        Assert.IsTrue(main.Skip(44).Take((int)plan.CountInFrames * 4).Any(value => value != 0));
        Assert.IsTrue(main.Skip(44 + (int)plan.CountInFrames * 4).All(value => value == 0));
        foreach (string name in new[] { "lane-02.wav", "lane-03.wav", "lane-04.wav", "lane-05.wav" })
            Assert.IsTrue((await ReadAsync(zip, name)).Skip(44).All(value => value == 0));
        var right = await ReadAsync(zip, "lane-01.wav");
        Assert.IsTrue(right.Skip(44).Any(value => value != 0));
        for (int frame = 0; frame < plan.TotalFrames; frame++)
            Assert.AreEqual((short)0, BinaryPrimitives.ReadInt16LittleEndian(right.AsSpan(44 + frame * 4)));
        temp.NoTemps();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Export_PreCanceledPreservesPriorContentsOrAbsence(bool existing)
    {
        using var temp = new TempDirectory();
        string path = temp.File("cancel.zip");
        if (existing) await File.WriteAllTextAsync(path, "retained");
        await Check.ThrowsAsync<OperationCanceledException>(() =>
            WaveStemExporter.ExportAsync(path, Check.Plan(), [], new CancellationToken(true)));
        if (existing) Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        else Assert.IsFalse(File.Exists(path));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_InvalidLayoutNamesAndPathNeverReplaceExistingDestination()
    {
        using var temp = new TempDirectory();
        string path = temp.File("retained.zip");
        await File.WriteAllTextAsync(path, "retained");
        await Check.ThrowsAsync<ArgumentException>(() => WaveStemExporter.ExportAsync(path, Check.Plan(Check.LaneDocument()), []));
        await Check.ThrowsAsync<ArgumentException>(() => WaveStemExporter.ExportAsync(path, Check.Plan(), new string[9]));
        await Check.ThrowsAsync<ArgumentException>(() => WaveStemExporter.ExportAsync(path, Check.Plan(Check.LaneDocument()), [null!]));
        await Check.ThrowsAsync<ArgumentException>(() => WaveStemExporter.ExportAsync(path, Check.Plan(Check.LaneDocument()), [new string('x', 257)]));
        await Check.ThrowsAsync<ArgumentNullException>(() => WaveStemExporter.ExportAsync(path, null!, []));
        await Check.ThrowsAsync<ArgumentNullException>(() => WaveStemExporter.ExportAsync(path, Check.Plan(), null!));
        await Check.ThrowsAsync<ArgumentException>(() => WaveStemExporter.ExportAsync(temp.File("invalid:stream"), Check.Plan(), []));
        Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        await WaveStemExporter.ExportAsync(path, Check.Plan(), []).WaitAsync(TimeSpan.FromSeconds(10));
        using var zip = ZipFile.OpenRead(path);
        Assert.IsNotNull(zip.GetEntry("manifest.json"));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_RiffOverflowRejectsBeforeAllocatingLargeOutput()
    {
        using var temp = new TempDirectory();
        string path = temp.File("huge.zip");
        await File.WriteAllTextAsync(path, "retained");
        var document = Check.Document();
        document.Settings.Tempo = 1;
        document.Settings.BeatUnitQuarters = 16;
        var plan = Check.Plan(document, 5760, 192000);
        var error = await Check.ThrowsAsync<ArgumentException>(() => WaveStemExporter.ExportAsync(path, plan, []));
        StringAssert.Contains(error.Message, "RIFF");
        Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_AllEightSilentLanesKeepTheirAuthoredCountAfterDocumentMutation()
    {
        using var temp = new TempDirectory();
        var document = Check.Document();
        document.Settings.Accents = [Accent.Silent];
        document.Lanes = Enumerable.Range(1, 8).Select(_ => new RhythmLane { Pattern = [Accent.Silent] }).ToList();
        var plan = Check.Plan(document);
        document.Lanes.Clear();
        Assert.AreEqual(8, plan.LaneCount);
        Assert.AreEqual(0, plan.Onsets.Count);
        Assert.AreEqual(0, plan.Targets.Count);
        Assert.AreEqual(plan.TotalFrames * 36, WaveStemExporter.GetUncompressedAudioBytes(plan));
        await Check.ThrowsAsync<ArgumentException>(() => WaveStemExporter.ExportAsync(temp.File("missing.zip"), plan, []));
        await WaveStemExporter.ExportAsync(temp.File("silent.zip"), plan, Enumerable.Repeat("", 8).ToArray());
        using var zip = ZipFile.OpenRead(temp.File("silent.zip"));
        Assert.AreEqual(10, zip.Entries.Count);
        foreach (var entry in zip.Entries.Where(entry => entry.Name.EndsWith(".wav", StringComparison.Ordinal)))
            Assert.IsTrue((await ReadAsync(zip, entry.Name)).Skip(44).All(value => value == 0));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_ConcurrentSameDestinationAlwaysCommitsACompleteMatchingArchive()
    {
        using var temp = new TempDirectory();
        string path = temp.File("shared.zip");
        var plans = Enumerable.Range(1, 4).Select(seed =>
        {
            var document = Check.LaneDocument();
            document.Seed = seed;
            return Check.Plan(document);
        }).ToArray();
        await Task.WhenAll(plans.Select(plan =>
            WaveStemExporter.ExportAsync(path, plan, ["Seed " + plan.Seed]))).WaitAsync(TimeSpan.FromSeconds(30));
        using var zip = ZipFile.OpenRead(path);
        using var manifest = JsonDocument.Parse(await ReadAsync(zip, "manifest.json"));
        int seed = manifest.RootElement.GetProperty("Seed").GetInt32();
        Assert.AreEqual("Seed " + seed, manifest.RootElement.GetProperty("Tracks")[1].GetProperty("Name").GetString());
        await WaveExporter.ExportAsync(temp.File("expected.wav"), plans.Single(plan => plan.Seed == seed));
        Check.Sequence(await File.ReadAllBytesAsync(temp.File("expected.wav")), await ReadAsync(zip, "lane-01.wav"));
        Assert.AreEqual(3, zip.Entries.Count);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_WindowsBlockedDestinationPreservesContentsAndCleansTemporaryArchive()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows delete-sharing enforcement.");
        using var temp = new TempDirectory();
        string path = temp.File("locked.zip");
        await File.WriteAllTextAsync(path, "retained");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Check.ThrowsAsync<IOException>(() => WaveStemExporter.ExportAsync(path, Check.Plan(), []));
        Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
        await WaveStemExporter.ExportAsync(path, Check.Plan(), []).WaitAsync(TimeSpan.FromSeconds(10));
        using var zip = ZipFile.OpenRead(path);
        Assert.IsNotNull(zip.GetEntry("main.wav"));
    }

    private static async Task<byte[]> ReadAsync(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.IsNotNull(entry, name);
        await using var stream = entry.Open();
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes);
        return bytes.ToArray();
    }
}
