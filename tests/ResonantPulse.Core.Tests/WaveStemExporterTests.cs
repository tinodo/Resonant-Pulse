using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class WaveStemExporterTests
{
    private static string[] LaneNames(PulseDocument document) => document.Lanes.Select(lane => lane.Name).ToArray();

    private static string TrackFile(int laneIndex) => "lane-" + (laneIndex + 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + ".wav";

    /// <summary>
    /// Runs <paramref name="work"/> and returns the most temporary archives that were in <paramref name="folder"/> at one moment while it ran
    /// (a temporary archive exists only while its export writes). It reads the folder as fast as it can, so a short overlap is seen.
    /// </summary>
    private static async Task<int> MostTemporaryArchivesAsync(string folder, Func<Task> work)
    {
        using var finished = new CancellationTokenSource();
        int most = 0;
        var watcher = Task.Factory.StartNew(() =>
        {
            while (!finished.IsCancellationRequested)
            {
                try { most = Math.Max(most, Directory.GetFiles(folder, "*.tmp").Length); }
                catch (IOException) { }
            }
        }, TaskCreationOptions.LongRunning);
        try { await work(); }
        finally
        {
            await finished.CancelAsync();
            await watcher;
        }
        return most;
    }

    /// <summary>A destination that is not seekable and takes only so many bytes, as a disk that fills up does.</summary>
    private sealed class LimitedStream(long limit, Action<long>? afterWrite = null) : Stream
    {
        private readonly MemoryStream _kept = new();

        public byte[] Written => _kept.ToArray();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Add(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) => Add(buffer);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Add(buffer.Span);
            return ValueTask.CompletedTask;
        }

        private void Add(ReadOnlySpan<byte> bytes)
        {
            if (_kept.Length + bytes.Length > limit) throw new IOException("There is not enough space on the disk.");
            _kept.Write(bytes);
            afterWrite?.Invoke(_kept.Length);
        }
    }

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

        await WaveStemExporter.ExportAsync(path, plan, LaneNames(document));

        var archive = await StemArchive.ReadAsync(path);
        Check.Sequence(new[] { "main.wav", "lane-01.wav", "lane-02.wav", "manifest.json" }, archive.Names);
        using (var zip = ZipFile.OpenRead(path)) Check.Sequence(archive.Names, zip.Entries.Select(entry => entry.FullName));
        Assert.IsTrue(plan.CountInFrames > 0);
        foreach (string name in new[] { "main.wav", "lane-01.wav", "lane-02.wav" })
        {
            var wave = archive.Wave(name);
            Assert.AreEqual(plan.TotalFrames, wave.Frames, name);
            Assert.AreEqual(2, wave.Channels, name);
            Assert.AreEqual(16, wave.BitsPerSample, name);
            Assert.AreEqual(plan.SampleRate, wave.Rate, name);
            bool countInIsSilent = wave.IsSilent(0, plan.CountInFrames);
            if (name == "main.wav") Assert.IsFalse(countInIsSilent, "The count-in belongs to the main track.");
            else Assert.IsTrue(countInIsSilent, $"{name} holds the count-in as leading silence.");
            Assert.IsFalse(wave.IsSilent(plan.CountInFrames, plan.TotalFrames - plan.CountInFrames), $"{name} plays after the count-in.");
        }
        using var manifest = archive.Manifest();
        var root = manifest.RootElement;
        Assert.AreEqual(1, root.GetProperty("SchemaVersion").GetInt32());
        Assert.AreEqual("PCM16 stereo", root.GetProperty("Format").GetString());
        Assert.AreEqual(plan.SampleRate, root.GetProperty("SampleRate").GetInt32());
        Assert.AreEqual(plan.TotalFrames, root.GetProperty("TotalFrames").GetInt64());
        Assert.AreEqual(plan.CountInFrames, root.GetProperty("CountInFrames").GetInt64());
        Assert.AreEqual(2, root.GetProperty("LaneCount").GetInt32());
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
        Assert.AreEqual(plan.TempoSegments.Count, root.GetProperty("TempoSegments").GetArrayLength());
        Assert.AreEqual(plan.SilentRanges.Count, root.GetProperty("SilentRanges").GetArrayLength());
        var tracks = root.GetProperty("Tracks");
        Assert.AreEqual(3, tracks.GetArrayLength());
        Assert.AreEqual("main", tracks[0].GetProperty("Kind").GetString());
        Assert.AreEqual(JsonValueKind.Null, tracks[0].GetProperty("LaneIndex").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, tracks[0].GetProperty("Name").ValueKind);
        Assert.AreEqual("lane", tracks[1].GetProperty("Kind").GetString());
        Assert.AreEqual("../../outside.wav", tracks[1].GetProperty("Name").GetString());
        Assert.AreEqual(1, tracks[2].GetProperty("LaneIndex").GetInt32());
        Assert.AreEqual(plan.Onsets.Count, root.GetProperty("AudibleOnsets").GetArrayLength());
        Assert.AreEqual(plan.Onsets.Count, tracks.EnumerateArray().Sum(track => track.GetProperty("AudibleOnsetCount").GetInt32()),
            "Every audible onset belongs to exactly one track.");
        Assert.IsTrue(plan.TotalFrames > plan.CountInFrames + plan.SampleRate, "The practice limits did not shorten the export.");
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
        var archive = await StemArchive.ReadAsync(temp.File("stems.zip"));
        Check.Sequence(new[] { "main.wav", "manifest.json" }, archive.Names);
        Check.Sequence(await File.ReadAllBytesAsync(temp.File("expected.wav")), archive.Extract("main.wav"));
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
        var archive = await StemArchive.ReadAsync(temp.File("stems.zip"));
        var lane = archive.Wave("lane-01.wav");
        Check.Sequence(await File.ReadAllBytesAsync(temp.File("expected.wav")), lane.Bytes);
        Assert.IsFalse(lane.IsSilent(plan.TotalFrames - 50, 50), "The click that starts just before the end is cut at the end, not dropped.");
        Assert.AreEqual(plan.TotalFrames, lane.Frames);
        Assert.IsTrue(archive.Wave("main.wav").IsSilent());
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
        var archive = await StemArchive.ReadAsync(temp.File("stems.zip"));
        var wave = archive.Wave(TrackFile(laneIndex));
        Check.Sequence(await File.ReadAllBytesAsync(temp.File("expected.wav")), wave.Bytes);
        Assert.IsTrue(plan.SilentRanges.Count > 0);
        foreach (var range in plan.SilentRanges)
            Assert.IsTrue(wave.IsSilent(range.StartFrame, range.EndFrame - range.StartFrame), "A gap is silent in every track, tails included.");
        using var manifest = archive.Manifest();
        var listed = manifest.RootElement.GetProperty("SilentRanges").EnumerateArray()
            .Select(range => (range.GetProperty("StartFrame").GetInt64(), range.GetProperty("EndFrame").GetInt64())).ToArray();
        Check.Sequence(plan.SilentRanges.ToArray(), listed);
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
        var archive = await StemArchive.ReadAsync(temp.File("stems.zip"));
        Assert.AreEqual(7, archive.Entries.Count);
        var main = archive.Wave("main.wav");
        Assert.IsFalse(main.IsSilent(0, plan.CountInFrames), "Solo does not silence the count-in.");
        Assert.IsTrue(main.IsSilent(plan.CountInFrames, plan.TotalFrames - plan.CountInFrames), "Solo leaves the main beat out after the count-in.");
        foreach (string name in new[] { "lane-02.wav", "lane-03.wav", "lane-04.wav", "lane-05.wav" })
        {
            var wave = archive.Wave(name);
            Assert.AreEqual(plan.TotalFrames, wave.Frames, name);
            Assert.IsTrue(wave.IsSilent(), $"{name} is silent but as long as the others.");
        }
        var right = archive.Wave("lane-01.wav");
        Assert.IsFalse(right.IsSilent());
        for (long frame = 0; frame < plan.TotalFrames; frame++) Assert.AreEqual((short)0, right.Sample(frame, 0), $"left channel at {frame}");
        temp.NoTemps();
    }

    [TestMethod]
    [DataRow("master gain")]
    [DataRow("lane volume")]
    [DataRow("event gain")]
    public async Task Export_AGainOfZeroGivesSilentFilesThatStillListTheClicksTheScheduleLetsThrough(string zero)
    {
        // AudibleOnsetCount and AudibleOnsets are what the schedule lets into a file: not muted, not left out by Solo, not dropped by a chance
        // or a gap. The schedule does not look at a gain, so a click whose gain is 0 (the master gain, the lane's volume or the event's gain)
        // is listed and its file is silent; docs/WAV-STEMS.md says so. With the master gain at 0 every file is silent, with the lane's
        // volume or its event's gain at 0 only the lane's file is.
        using var temp = new TempDirectory();
        var document = Check.Document();
        var lane = new RhythmLane { Name = "One", Pulses = 4 };
        if (zero == "master gain") document.Settings.MasterGain = 0;
        if (zero == "lane volume") lane.Volume = 0;
        if (zero == "event gain")
        {
            lane = new RhythmLane { Name = "One", CycleQuarters = 4, Events = [new() { PositionQuarters = Rational.Zero, Gain = 0 }] };
        }
        document.Lanes = [lane];
        var plan = Check.Plan(document, 2, 8000, false);
        int mainClicks = plan.Onsets.Count(onset => onset.LaneIndex < 0), laneClicks = plan.Onsets.Count(onset => onset.LaneIndex == 0);
        Assert.IsTrue(mainClicks > 0 && laneClicks > 0, $"{zero}: the schedule has clicks for both tracks.");
        await WaveStemExporter.ExportAsync(temp.File("zero.zip"), plan, ["One"]);
        var archive = await StemArchive.ReadAsync(temp.File("zero.zip"));
        Assert.AreEqual(zero == "master gain", archive.Wave("main.wav").IsSilent(), $"{zero}: the main file");
        Assert.IsTrue(archive.Wave("lane-01.wav").IsSilent(), $"{zero}: the lane's file");
        using var manifest = archive.Manifest();
        var tracks = manifest.RootElement.GetProperty("Tracks");
        Assert.AreEqual(mainClicks, tracks[0].GetProperty("AudibleOnsetCount").GetInt32(), zero);
        Assert.AreEqual(laneClicks, tracks[1].GetProperty("AudibleOnsetCount").GetInt32(), zero);
        Assert.AreEqual(plan.Onsets.Count, manifest.RootElement.GetProperty("AudibleOnsets").GetArrayLength(), zero);
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
        await Check.ThrowsAsync<ArgumentException>(() => WaveStemExporter.ExportAsync(path, Check.Plan(Check.LaneDocument()), ["half a pair \uD83D"]));
        await Check.ThrowsAsync<ArgumentException>(() => WaveStemExporter.ExportAsync(path, Check.Plan(Check.LaneDocument()), ["\uDE00 the other half"]));
        await Check.ThrowsAsync<ArgumentNullException>(() => WaveStemExporter.ExportAsync(path, null!, []));
        await Check.ThrowsAsync<ArgumentNullException>(() => WaveStemExporter.ExportAsync(path, Check.Plan(), null!));
        await Check.ThrowsAsync<ArgumentException>(() => WaveStemExporter.ExportAsync(temp.File("invalid:stream"), Check.Plan(), []));
        Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        await WaveStemExporter.ExportAsync(path, Check.Plan(), []).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsNotNull((await StemArchive.ReadAsync(path)).Entries.SingleOrDefault(entry => entry.Name == "manifest.json"));
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
        Check.Throws<ArgumentException>(() => WaveStemExporter.GetUncompressedAudioBytes(plan));
        // Everything is checked before the file system is touched: a folder that is not there would be a different error if the export got that far.
        await Check.ThrowsAsync<ArgumentException>(() => WaveStemExporter.ExportAsync(Path.Combine(temp.Root, "no such folder", "huge.zip"), plan, []));
        Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task MixedExport_StillRefusesATooLongPlanBeforeTouchingTheFileSystem()
    {
        // The check moved into PcmWaveWriter with the writer, and the mixed export still makes it before it looks at the path: a folder
        // that is not there would give a different error if the export got that far. (WaveExporterTests is as it is on main.)
        using var temp = new TempDirectory();
        var document = Check.Document();
        document.Settings.Tempo = 1;
        document.Settings.BeatUnitQuarters = 16;
        var plan = Check.Plan(document, 5760, 192000);
        var error = await Check.ThrowsAsync<ArgumentException>(() =>
            WaveExporter.ExportAsync(Path.Combine(temp.Root, "no such folder", "huge.wav"), plan));
        StringAssert.Contains(error.Message, "RIFF");
        Assert.AreEqual(0, Directory.GetFileSystemEntries(temp.Root).Length);
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
        var archive = await StemArchive.ReadAsync(temp.File("silent.zip"));
        Assert.AreEqual(10, archive.Entries.Count);
        foreach (var entry in archive.Entries.Where(entry => entry.Name.EndsWith(".wav", StringComparison.Ordinal)))
            Assert.IsTrue(archive.Wave(entry.Name).IsSilent(), entry.Name);
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
            document.Settings.Tempo = 80 + seed * 20;
            document.Settings.MasterGain = 0.1f + seed * 0.1f;
            document.Lanes[0].Pan = -1 + seed * 0.4f;
            return Check.Plan(document, 40);
        }).ToArray();
        Assert.AreEqual(plans.Length, plans.Select(plan => plan.TotalFrames).Distinct().Count());
        // A temporary archive exists only while its export writes, so more than one at a time would mean that two exports ran together.
        int mostTemporaryArchives = await MostTemporaryArchivesAsync(temp.Root, () => Task.WhenAll(plans.Select(plan =>
            WaveStemExporter.ExportAsync(path, plan, ["Seed " + plan.Seed]))).WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.IsTrue(mostTemporaryArchives <= 1, $"One export of tracks runs at a time, but {mostTemporaryArchives} temporary archives were there together.");
        var archive = await StemArchive.ReadAsync(path);
        using var manifest = archive.Manifest();
        int seed = manifest.RootElement.GetProperty("Seed").GetInt32();
        Assert.AreEqual("Seed " + seed, manifest.RootElement.GetProperty("Tracks")[1].GetProperty("Name").GetString());
        await WaveExporter.ExportAsync(temp.File("expected.wav"), plans.Single(plan => plan.Seed == seed));
        Check.Sequence(await File.ReadAllBytesAsync(temp.File("expected.wav")), archive.Extract("lane-01.wav"));
        Assert.AreEqual(3, archive.Entries.Count);
        Assert.AreEqual(1, Directory.GetFiles(temp.Root, "*.zip").Length);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_ExportsToDifferentDestinationsNeverRunTogetherEither()
    {
        // The one-at-a-time rule is Core's, not the destination's: four exports to four files still write one after the other.
        using var temp = new TempDirectory();
        var plans = Enumerable.Range(1, 4).Select(seed =>
        {
            var document = Check.LaneDocument();
            document.Seed = seed;
            document.Settings.Tempo = 80 + seed * 20;
            return Check.Plan(document, 40);
        }).ToArray();
        int mostTemporaryArchives = await MostTemporaryArchivesAsync(temp.Root, () => Task.WhenAll(plans.Select((plan, index) =>
            WaveStemExporter.ExportAsync(temp.File($"part-{index}.zip"), plan, ["Lane"]))).WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.IsTrue(mostTemporaryArchives <= 1, $"One export of tracks runs at a time, but {mostTemporaryArchives} temporary archives were there together.");
        for (int index = 0; index < plans.Length; index++)
            Assert.AreEqual(3, (await StemArchive.ReadAsync(temp.File($"part-{index}.zip"))).Entries.Count, $"part-{index}.zip");
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_WindowsBlockedDestinationPreservesContentsAndCleansTemporaryArchive()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows delete-sharing enforcement.");
        using var temp = new TempDirectory();
        string path = temp.File("locked.zip");
        await File.WriteAllTextAsync(path, "retained");
        Exception? failure = null;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { await WaveStemExporter.ExportAsync(path, Check.Plan(), []); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failure = error; }
        }
        Assert.IsNotNull(failure, "A destination without delete sharing must not be replaced.");
        Assert.IsTrue((failure.HResult & 0xFFFF) is 5 or 32 or 33, failure.Message);
        Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
        temp.NoTemps();
        await WaveStemExporter.ExportAsync(path, Check.Plan(), []).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsNotNull((await StemArchive.ReadAsync(path)).Entries.SingleOrDefault(entry => entry.Name == "main.wav"));
    }

    [TestMethod]
    public async Task Export_EntryNamesAreTheFixedNamesWhateverTheLaneNamesAre()
    {
        string[][] nameSets =
        [
            ["../../outside.wav"], [@"..\..\x"], ["CON"], [new string('x', 256)], ["line\r\nbreak", "quote \" and \\ backslash"], [""],
            ["same", "same"], ["main.wav", "lane-01.wav", "manifest.json"], ["lane-02.wav", "lane-01.wav"],
            ["trailing dot.", "trailing space "], [@"C:\Windows\System32\x", "a/b/c", "directory/"],
            ["\uD83D\uDE00 emoji", "\u202E right-to-left override", "e\u0301 combining"],
            [.. Enumerable.Range(1, 8).Select(number => $"Lane {number}")]
        ];
        foreach (var names in nameSets)
        {
            using var temp = new TempDirectory();
            var document = Check.Document();
            document.Lanes = names.Select(name => new RhythmLane { Name = name }).ToList();
            var plan = Check.Plan(document);
            await WaveStemExporter.ExportAsync(temp.File("names.zip"), plan, names);

            var archive = await StemArchive.ReadAsync(temp.File("names.zip"));
            string[] expected = ["main.wav", .. names.Select((_, index) => TrackFile(index)), "manifest.json"];
            Check.Sequence(expected, archive.Names);
            Assert.AreEqual(expected.Length, archive.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "No two entries have the same name.");
            foreach (string entry in archive.Names)
                Assert.IsTrue(Regex.IsMatch(entry, @"^(main\.wav|manifest\.json|lane-0[1-8]\.wav)$"), $"Entry name '{entry}' is not one of the fixed names.");
            using var manifest = archive.Manifest();
            var tracks = manifest.RootElement.GetProperty("Tracks");
            Assert.AreEqual(names.Length + 1, tracks.GetArrayLength());
            for (int index = 0; index < names.Length; index++)
            {
                Assert.AreEqual(names[index], tracks[index + 1].GetProperty("Name").GetString(), "A lane's name is data in the manifest, exactly as it was.");
                Assert.AreEqual(TrackFile(index), tracks[index + 1].GetProperty("File").GetString());
                Assert.AreEqual(index, tracks[index + 1].GetProperty("LaneIndex").GetInt32());
            }
            Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length, "Nothing but the archive was written.");
            Assert.AreEqual(0, Directory.GetDirectories(temp.Root).Length);
            temp.NoTemps();
        }
    }

    [TestMethod]
    public void Crc32Reference_GivesTheStandardCheckValue()
    {
        Assert.AreEqual(0xCBF43926u, Crc32Reference.Of("123456789"u8));
        Assert.AreEqual(0u, Crc32Reference.Of([]));
    }

    [TestMethod]
    public async Task Export_TheArchiveIsWellFormedAndEveryCrcMatchesItsBytes()
    {
        using var temp = new TempDirectory();
        var document = Check.Document();
        document.Settings.Subdivision = 3;
        document.Lanes = [new() { Name = "One", Pulses = 5, CycleQuarters = 3 }, new() { Name = "Two", Pulses = 3, Pan = 0.5f }];
        var plan = Check.Plan(document, 2, 8000, true);
        await WaveStemExporter.ExportAsync(temp.File("crc.zip"), plan, LaneNames(document));

        byte[] bytes = await File.ReadAllBytesAsync(temp.File("crc.zip"));
        var archive = StemArchive.Read(bytes);
        Assert.AreEqual(4, archive.Entries.Count);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.AreEqual(archive.Entries.Count, zip.Entries.Count);
        foreach (var info in archive.Entries)
        {
            byte[] expanded = archive.Extract(info.Name);
            Assert.AreEqual(Crc32Reference.Of(expanded), info.Crc32, $"{info.Name}: the CRC-32 in the archive is not the CRC-32 of the bytes");
            var entry = zip.GetEntry(info.Name)!;
            // .NET does not check a CRC-32 when it reads an entry, so it is compared here.
            Assert.AreEqual(Crc32Reference.Of(expanded), entry.Crc32, $"{info.Name}: ZipArchiveEntry.Crc32");
            Assert.AreEqual((long)info.UncompressedSize, entry.Length, info.Name);
            Assert.AreEqual((long)info.CompressedSize, entry.CompressedLength, info.Name);
            await using var stream = entry.Open();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            Check.Sequence(expanded, copy.ToArray());
            Assert.AreEqual(8, info.Method, $"{info.Name}: every entry is Deflate-compressed (a stored entry would be about 18 times larger for the first project)");
            Assert.AreEqual(0, info.Flags & 1, $"{info.Name}: encrypted");
            Assert.IsFalse(info.Name.EndsWith('/') || info.Name.Contains('\\'), $"{info.Name}: a directory entry or a path");
            Assert.AreEqual(new DateTime(1980, 1, 1), info.Modified, $"{info.Name}: the date is fixed so that the same project always gives the same bytes");
        }
        foreach (string name in new[] { "main.wav", "lane-01.wav", "lane-02.wav" })
        {
            var wave = archive.Wave(name);
            Assert.AreEqual(plan.TotalFrames, wave.Frames, name);
            Assert.AreEqual(2, wave.Channels, name);
            Assert.AreEqual(16, wave.BitsPerSample, name);
            Assert.AreEqual(8000, wave.Rate, name);
        }
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Export_EveryWaveIsACanonicalSixteenBitStereoFileAtEverySampleRate()
    {
        foreach (int rate in new[] { 8000, 44100, 48000 })
        {
            using var temp = new TempDirectory();
            var document = Check.Document();
            document.Lanes = [new() { Name = "A", Pulses = 4 }];
            var plan = Check.Plan(document, 1, rate);
            await WaveStemExporter.ExportAsync(temp.File("rate.zip"), plan, ["A"]);
            var archive = await StemArchive.ReadAsync(temp.File("rate.zip"));
            foreach (string name in new[] { "main.wav", "lane-01.wav" })
            {
                var wave = archive.Wave(name);
                Assert.AreEqual(rate, wave.Rate, name);
                Assert.AreEqual(plan.TotalFrames, wave.Frames, name);
                Assert.AreEqual(44 + plan.TotalFrames * 4, wave.Bytes.LongLength, name);
            }
        }
    }

    [TestMethod]
    public async Task Export_WhenNothingClipsTheSumOfTheTracksIsWithinHalfAStepPerFileOfTheMixedWave()
    {
        using var temp = new TempDirectory();
        var document = Check.Document();
        document.Settings.MasterGain = 0.4f;
        document.Settings.Subdivision = 3;
        document.Settings.CountInBars = 1;
        document.Lanes = [new() { Name = "A", Pulses = 5, CycleQuarters = 3, Pan = -0.6f },
            new() { Name = "B", Pulses = 7, CycleQuarters = 5, Pan = 0.5f, Volume = 0.6f, Sound = "Kick" },
            new() { Name = "C", Pulses = 4, CycleQuarters = 4, Pan = 0.1f, Sound = "HiHat" }];
        var plan = Check.Plan(document, 2, 8000, true);
        await WaveExporter.ExportAsync(temp.File("mixed.wav"), plan);
        await WaveStemExporter.ExportAsync(temp.File("stems.zip"), plan, LaneNames(document));
        var mixed = WaveFile.Read(await File.ReadAllBytesAsync(temp.File("mixed.wav")), "mixed");
        var archive = await StemArchive.ReadAsync(temp.File("stems.zip"));
        var files = new[] { "main.wav", "lane-01.wav", "lane-02.wav", "lane-03.wav" }.Select(archive.Wave).ToArray();

        // Each file is rounded to 16 bits on its own and the mix is rounded once: the sum of k files differs from the mix by at most half a
        // step for each file and half a step for the mix, so by at most (k + 1) / 2 whole steps.
        int bound = (files.Length + 1) / 2, worst = 0;
        long loudest = 0;
        for (long frame = 0; frame < mixed.Frames; frame++)
        for (int channel = 0; channel < 2; channel++)
        {
            int sum = files.Sum(file => file.Sample(frame, channel));
            worst = Math.Max(worst, Math.Abs(sum - mixed.Sample(frame, channel)));
            loudest = Math.Max(loudest, Math.Abs((long)mixed.Sample(frame, channel)));
            foreach (var file in files) Assert.IsTrue(Math.Abs((int)file.Sample(frame, channel)) < 32767, "No track may reach full scale in this fixture.");
        }
        Assert.IsTrue(loudest < 32767, "The mix must not clip in this fixture.");
        Assert.IsTrue(worst <= bound, $"The sum of the tracks is {worst} steps from the mixed WAV; at most {bound} are allowed for {files.Length} files.");
        Assert.IsTrue(worst >= 1, "The fixture must show some rounding, or the bound is not exercised.");
    }

    [TestMethod]
    public async Task Export_WhenTheMixClipsTheTracksDoNotAddUpToIt()
    {
        using var temp = new TempDirectory();
        var document = Check.Document();
        document.Settings.MasterGain = 1f;
        document.Settings.Accents = [Accent.Silent];
        document.Lanes = Enumerable.Range(0, 3).Select(index => new RhythmLane { Name = "Lane " + index, Volume = 1f, Pan = -1, Pulses = 4 }).ToList();
        var plan = Check.Plan(document, 2);
        await WaveExporter.ExportAsync(temp.File("mixed.wav"), plan);
        await WaveStemExporter.ExportAsync(temp.File("stems.zip"), plan, LaneNames(document));
        var mixed = WaveFile.Read(await File.ReadAllBytesAsync(temp.File("mixed.wav")), "mixed");
        var archive = await StemArchive.ReadAsync(temp.File("stems.zip"));
        var files = new[] { "main.wav", "lane-01.wav", "lane-02.wav", "lane-03.wav" }.Select(archive.Wave).ToArray();

        int bound = (files.Length + 1) / 2, worst = 0;
        bool mixClipped = false, trackClipped = false;
        for (long frame = 0; frame < mixed.Frames; frame++)
        {
            short mix = mixed.Sample(frame, 0);
            int difference = Math.Abs(files.Sum(file => file.Sample(frame, 0)) - mix);
            worst = Math.Max(worst, difference);
            mixClipped |= mix >= 32767 || mix <= -32768;
            trackClipped |= files.Any(file => file.Sample(frame, 0) >= 32767 || file.Sample(frame, 0) <= -32768);
            // Where the mix is clear of full scale the tracks still add up to it.
            if (Math.Abs((int)mix) < 32000) Assert.IsTrue(difference <= bound, $"frame {frame}: {difference} steps");
        }
        Assert.IsTrue(mixClipped, "The mix must clip in this fixture.");
        Assert.IsFalse(trackClipped, "No single track may clip in this fixture.");
        Assert.IsTrue(worst > 10 * bound, $"Where the mix clips the tracks must add up to much more than the mix, not within {bound} steps (worst {worst}).");
    }

    [TestMethod]
    public async Task Export_TheSameProjectAlwaysGivesTheSameBytes()
    {
        using var temp = new TempDirectory();
        var document = Check.Document();
        document.Settings.CountInBars = 1;
        document.Lanes = [new() { Name = "One", Pulses = 5, CycleQuarters = 3, Probability = 0.7 }, new() { Name = "Two", Pulses = 3 }];
        await WaveStemExporter.ExportAsync(temp.File("first.zip"), Check.Plan(document, 2, 8000, true), LaneNames(document));
        await Task.Delay(2200);
        await WaveStemExporter.ExportAsync(temp.File("second.zip"), Check.Plan(document, 2, 8000, true), LaneNames(document));
        Check.Sequence(await File.ReadAllBytesAsync(temp.File("first.zip")), await File.ReadAllBytesAsync(temp.File("second.zip")));
    }

    [TestMethod]
    public async Task Write_ASinkThatRunsOutOfRoomFailsTheExportInsteadOfEndingItQuietly()
    {
        var document = Check.LaneDocument();
        var plan = Check.Plan(document);
        var sink = new LimitedStream(50);
        var error = await Check.ThrowsAsync<IOException>(() => WaveStemExporter.WriteAsync(sink, plan, ["Lane"]));
        StringAssert.Contains(error.Message, "disk");
        Assert.IsTrue(sink.Written.Length <= 50);
    }

    [TestMethod]
    public async Task Write_StopsWhenItIsCancelledWhileWritingAndWritesNoLaterTrackAndNoManifest()
    {
        var document = Check.Document();
        document.Lanes = [new() { Name = "A" }, new() { Name = "B" }, new() { Name = "C" }];
        var plan = Check.Plan(document, 4);
        using var source = new CancellationTokenSource();
        // The first track's header is written, then the export is cancelled: no other track and no manifest may follow.
        var sink = new LimitedStream(long.MaxValue, written => { if (written >= 30) source.Cancel(); });
        await Check.ThrowsAsync<OperationCanceledException>(() => WaveStemExporter.WriteAsync(sink, plan, LaneNames(document), source.Token));
        string text = Encoding.ASCII.GetString(sink.Written);
        Assert.IsTrue(source.IsCancellationRequested);
        Assert.IsFalse(text.Contains("manifest.json", StringComparison.Ordinal), "The manifest was written after the export was cancelled.");
        Assert.IsFalse(text.Contains("lane-03.wav", StringComparison.Ordinal), "A later track was written after the export was cancelled.");
    }

    [TestMethod]
    public async Task Export_CancelledWhileItWritesKeepsTheOldFileAndLeavesNoTemporaryArchive()
    {
        using var temp = new TempDirectory();
        string path = temp.File("slow.zip");
        await File.WriteAllTextAsync(path, "retained");
        // A long project (3,000 bars: 100 minutes), so that the export takes longer than it takes to cancel it once its temporary file exists.
        var document = Check.Document();
        document.Lanes = [new() { Name = "A" }, new() { Name = "B" }, new() { Name = "C" }];
        var plan = Check.Plan(document, 3000);
        using var source = new CancellationTokenSource();
        var export = Task.Run(() => WaveStemExporter.ExportAsync(path, plan, LaneNames(document), source.Token));
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!export.IsCompleted && Directory.GetFiles(temp.Root, "*.tmp").Length == 0 && DateTime.UtcNow < deadline) await Task.Delay(1);
        source.Cancel();
        Exception? failure = null;
        try { await export.WaitAsync(TimeSpan.FromSeconds(60)); }
        catch (OperationCanceledException error) { failure = error; }
        if (failure is null) Assert.Inconclusive("The export finished before it could be cancelled; the cancelling itself is covered by Write_StopsWhenItIsCancelledWhileWritingAndWritesNoLaterTrackAndNoManifest.");
        Assert.AreEqual("retained", await File.ReadAllTextAsync(path));
        temp.NoTemps();
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length);
    }

    [TestMethod]
    public void GetUncompressedAudioBytes_IsFourBytesAFrameForEveryTrack()
    {
        Assert.AreEqual(Check.Plan().TotalFrames * 4, WaveStemExporter.GetUncompressedAudioBytes(Check.Plan()));
        var document = Check.Document();
        document.Lanes = [new(), new(), new()];
        var plan = Check.Plan(document, 3, 48000, false);
        Assert.AreEqual(plan.TotalFrames * 4 * 4, WaveStemExporter.GetUncompressedAudioBytes(plan));
        Check.Throws<ArgumentNullException>(() => WaveStemExporter.GetUncompressedAudioBytes(null!));
    }
}
