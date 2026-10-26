using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class DocumentStoreTests
{
    [TestMethod]
    public async Task Save_DisabledPracticeLimitsOmitNewFieldsAndLegacyImportsDefaultToDisabled()
    {
        using var temp = new TempDirectory();
        await DocumentStore.SaveAsync(temp.File(), new PulseDocument());
        var json = await File.ReadAllTextAsync(temp.File());
        Assert.IsFalse(json.Contains("\"PracticeSeconds\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("\"PracticeBeats\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("\"PracticeBars\"", StringComparison.Ordinal));
        await File.WriteAllTextAsync(temp.File(), "{\"SchemaVersion\":1}");
        var loaded = await DocumentStore.LoadAsync(temp.File());
        Assert.AreEqual(0, loaded.Settings.PracticeSeconds);
        Assert.AreEqual(0, loaded.Settings.PracticeBeats);
        Assert.AreEqual(0, loaded.Settings.PracticeBars);
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Load_CompactImportExceedingExpandedSaveBudget_IsRejectedBeforePublication()
    {
        using var temp = new TempDirectory();
        var program = string.Join(",", Enumerable.Repeat("{}", 1024));
        var song = "{\"Document\":{\"SchemaVersion\":1,\"Program\":[" + program + "]}}";
        var json = "{\"SchemaVersion\":1,\"Setlist\":[" + string.Join(",", Enumerable.Repeat(song, 32)) + "]}";
        Assert.IsTrue(Encoding.UTF8.GetByteCount(json) < DocumentValidator.MaximumDocumentBytes);
        var expanded = JsonSerializer.Deserialize<PulseDocument>(json)!;
        DocumentValidator.Validate(expanded);
        Assert.IsTrue(JsonSerializer.SerializeToUtf8Bytes(expanded, new JsonSerializerOptions { WriteIndented = true }).Length >
            DocumentValidator.MaximumDocumentBytes);
        await File.WriteAllTextAsync(temp.File(), json);

        var error = await Check.ThrowsAsync<InvalidDataException>(() => DocumentStore.LoadAsync(temp.File()));

        Assert.IsTrue(error.Message.Contains("4 MiB", StringComparison.Ordinal));
        Assert.AreEqual(json, await File.ReadAllTextAsync(temp.File()));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task SaveLoad_AllAuthoredAndNestedFields_RoundTripsDeepValues()
    {
        using var temp = new TempDirectory();
        var d = new PulseDocument
        {
            Name = "Étude ♩", Seed = -2026,
            Settings = new() { Tempo = 80, BeatsPerBar = 6, BeatDenominator = 8, BeatUnitQuarters = 1.5,
                Subdivision = 2, Swing = 0.75, CountInBars = 2, PracticeSeconds = 60, PracticeBeats = 17, PracticeBars = 3, Accents = [Accent.Strong, Accent.Soft],
                Groups = [3, 3], AudibleBars = 2, SilentBars = 1, BlindGaps = false, MasterGain = 0.3f, Sound = "Kick" },
            Lanes = [new() { Name = "Triplets", Pulses = 3, CycleQuarters = 3, OffsetQuarters = -0.5,
                Volume = 0.4f, Pan = -0.25f, Muted = true, Solo = true, Probability = 0.75, Sound = "HiHat",
                Events = [new() { PositionQuarters = new Rational(1, 3), Accent = Accent.Soft, Probability = 0.25, Gain = 0.6f, Tie = true }] },
                new() { Name = "Pattern", Pattern = [Accent.Normal, Accent.Silent] }],
            Program = [new() { Name = "Ramp", StartTempo = 80, EndTempo = 100, Ramp = "LinearBeat", Bars = 3, Repeats = 2,
                BeatsPerBar = 6, BeatDenominator = 8, AudibleBars = 1, SilentBars = 1 }],
            Setlist = [new() { Name = "Nested", Notes = "Keep relaxed\nSecond line", Document = new() { Name = "Child", Seed = 42 } }]
        };
        await DocumentStore.SaveAsync(temp.File(), d);
        string json = await File.ReadAllTextAsync(temp.File());
        Assert.IsTrue(json.Contains("\"1/3\"", StringComparison.Ordinal));
        var loaded = await DocumentStore.LoadAsync(temp.File());
        Assert.AreEqual("Étude ♩", loaded.Name); Assert.AreEqual(1, loaded.SchemaVersion);
        Assert.AreEqual(-2026, loaded.Seed); Assert.AreEqual("event-hash-v1", loaded.ProbabilityAlgorithm);
        var s = loaded.Settings;
        Assert.AreEqual(60, s.PracticeSeconds);
        Assert.AreEqual(17, s.PracticeBeats);
        Assert.AreEqual(3, s.PracticeBars);
        Assert.AreEqual((80d, 6, 8, 1.5, 2, 0.75, 2), (s.Tempo, s.BeatsPerBar, s.BeatDenominator, s.BeatUnitQuarters, s.Subdivision, s.Swing, s.CountInBars));
        Check.Sequence(new[] { Accent.Strong, Accent.Soft }, s.Accents); Check.Sequence(new[] { 3, 3 }, s.Groups);
        Assert.AreEqual((2, 1, false, 0.3f, "Kick"), (s.AudibleBars, s.SilentBars, s.BlindGaps, s.MasterGain, s.Sound));
        var l = loaded.Lanes[0];
        Assert.AreEqual(("Triplets", 3, 3d, -0.5, 0.4f, -0.25f), (l.Name, l.Pulses, l.CycleQuarters, l.OffsetQuarters, l.Volume, l.Pan));
        Assert.AreEqual((true, true, 0.75, "HiHat"), (l.Muted, l.Solo, l.Probability, l.Sound));
        Assert.AreEqual(0, l.Pattern.Length);
        var e = l.Events.Single();
        Assert.AreEqual(new Rational(1, 3), e.PositionQuarters);
        Assert.AreEqual((Accent.Soft, 0.25, 0.6f, true), (e.Accent, e.Probability, e.Gain, e.Tie));
        Check.Sequence(new[] { Accent.Normal, Accent.Silent }, loaded.Lanes[1].Pattern);
        Assert.AreEqual(0, loaded.Lanes[1].Events.Count);
        var p = loaded.Program.Single();
        Assert.AreEqual(("Ramp", 80d, 100d, "LinearBeat", 3, 2), (p.Name, p.StartTempo, p.EndTempo, p.Ramp, p.Bars, p.Repeats));
        Assert.AreEqual((6, 8, 1, 1), (p.BeatsPerBar, p.BeatDenominator, p.AudibleBars, p.SilentBars));
        var song = loaded.Setlist.Single();
        Assert.AreEqual("Nested", song.Name); Assert.AreEqual("Keep relaxed\nSecond line", song.Notes);
        Assert.AreEqual("Child", song.Document.Name); Assert.AreEqual(42, song.Document.Seed);
        Assert.AreNotSame(d.Settings, loaded.Settings); temp.NoTemps();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("{")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("{\"schemaVersion\":1}")]
    [DataRow("{\"SchemaVersion\":2}")]
    [DataRow("{\"SchemaVersion\":\"1\"}")]
    [DataRow("{\"SchemaVersion\":1,\"Unknown\":0}")]
    [DataRow("{\"SchemaVersion\":1,\"SchemaVersion\":1}")]
    [DataRow("{\"SchemaVersion\":1,\"Settings\":{\"Tempo\":120,\"Tempo\":121}}")]
    [DataRow("{\"SchemaVersion\":1,\"Settings\":{\"tempo\":120}}")]
    [DataRow("{\"SchemaVersion\":1,\"Settings\":{\"Unknown\":0}}")]
    [DataRow("{\"SchemaVersion\":1,\"Settings\":{\"Tempo\":0}}")]
    [DataRow("{\"SchemaVersion\":1,\"Settings\":null}")]
    [DataRow("{\"SchemaVersion\":1,\"Lanes\":[{\"Events\":[{\"PositionQuarters\":\"1/0\"}]}]}")]
    public async Task Load_MalformedUnknownDuplicateCaseAndSchemaInputs_ThrowsInvalidDataException(string json)
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(temp.File(), json);
        await Check.ThrowsAsync<InvalidDataException>(() => DocumentStore.LoadAsync(temp.File()));
        Assert.AreEqual(json, await File.ReadAllTextAsync(temp.File())); temp.NoTemps();
    }

    [TestMethod]
    public async Task Load_DocumentByteAndDepthLimits_RejectsBeforeUnboundedParsing()
    {
        using var temp = new TempDirectory();
        const string valid = "{\"SchemaVersion\":1}";
        var bytes = Encoding.UTF8.GetBytes(valid + new string(' ', 4194304 - valid.Length));
        await File.WriteAllBytesAsync(temp.File(), bytes);
        Assert.AreEqual(1, (await DocumentStore.LoadAsync(temp.File())).SchemaVersion);
        await File.AppendAllTextAsync(temp.File(), " ");
        await Check.ThrowsAsync<InvalidDataException>(() => DocumentStore.LoadAsync(temp.File()));
        string deep = "{\"SchemaVersion\":1,\"Unknown\":" + new string('[', 65) + "0" + new string(']', 65) + "}";
        await File.WriteAllTextAsync(temp.File(), deep);
        await Check.ThrowsAsync<InvalidDataException>(() => DocumentStore.LoadAsync(temp.File()));
        string child = "{\"SchemaVersion\":1}";
        for (int i = 0; i < 9; i++) child = "{\"SchemaVersion\":1,\"Setlist\":[{\"Document\":" + child + "}]}";
        await File.WriteAllTextAsync(temp.File(), child);
        await Check.ThrowsAsync<InvalidDataException>(() => DocumentStore.LoadAsync(temp.File()));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Save_InvalidGraphAndCycles_RejectsBeforeReplacingTarget()
    {
        using var temp = new TempDirectory(); byte[] sentinel = [7, 1, 9, 3];
        await File.WriteAllBytesAsync(temp.File(), sentinel);
        var invalid = Check.Document(); invalid.Settings.Tempo = 0;
        await Check.ThrowsAsync<ArgumentException>(() => DocumentStore.SaveAsync(temp.File(), invalid));
        Check.Sequence(sentinel, await File.ReadAllBytesAsync(temp.File()));
        var cycle = Check.Document(); cycle.Setlist.Add(new() { Document = cycle });
        await Check.ThrowsAsync<ArgumentException>(() => DocumentStore.SaveAsync(temp.File(), cycle));
        Check.Sequence(sentinel, await File.ReadAllBytesAsync(temp.File())); temp.NoTemps();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Save_ExistingAndNewTargets_CommitsCompleteDocumentAndCleansTemps(bool existing)
    {
        using var temp = new TempDirectory();
        if (existing) await File.WriteAllTextAsync(temp.File(), "sentinel");
        var d = Check.Document(); d.Name = "Committed";
        await DocumentStore.SaveAsync(temp.File(), d);
        var loaded = await DocumentStore.LoadAsync(temp.File());
        Assert.AreEqual("Committed", loaded.Name); Assert.AreEqual(120d, loaded.Settings.Tempo);
        Assert.AreEqual(1, Directory.GetFiles(temp.Root).Length); temp.NoTemps();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Save_PreCanceledValidDocument_PreservesSentinelAndCleansTemps(bool existing)
    {
        using var temp = new TempDirectory();
        if (existing) await File.WriteAllTextAsync(temp.File(), "sentinel");
        await Check.ThrowsAsync<OperationCanceledException>(() =>
            DocumentStore.SaveAsync(temp.File(), Check.Document(), new CancellationToken(true)));
        if (existing) Assert.AreEqual("sentinel", await File.ReadAllTextAsync(temp.File()));
        else Assert.IsFalse(File.Exists(temp.File()));
        temp.NoTemps();
    }

    [TestMethod]
    public async Task Load_PreCanceledValidPath_ThrowsCancellation()
    {
        using var temp = new TempDirectory(); await DocumentStore.SaveAsync(temp.File(), Check.Document());
        await Check.ThrowsAsync<OperationCanceledException>(() => DocumentStore.LoadAsync(temp.File(), new CancellationToken(true)));
        Assert.AreEqual(1, (await DocumentStore.LoadAsync(temp.File())).SchemaVersion); temp.NoTemps();
    }

    [TestMethod]
    public async Task Store_LocalPathContract_RejectsUnsupportedPathsWithoutNetworkAccess()
    {
        using var temp = new TempDirectory();
        string[] rejected = ["relative.json", @"\\server\share\file.json", "//server/share/file.json",
            @"\\?\C:\device.json", "https://example.invalid/project.json", temp.File("bad*.json"),
            temp.File("bad?.json"), temp.File("ads:stream"), temp.File("trailing."), temp.File("trailing "), temp.File("bad\0name")];
        foreach (var path in rejected)
        {
            await Check.ThrowsAsync<ArgumentException>(() => DocumentStore.SaveAsync(path, Check.Document()));
            await Check.ThrowsAsync<ArgumentException>(() => DocumentStore.LoadAsync(path));
        }
        string missing = Path.Combine(temp.Root, "absent", "file.json");
        await Check.ThrowsAsync<DirectoryNotFoundException>(() => DocumentStore.SaveAsync(missing, Check.Document()));
        await Check.ThrowsAsync<DirectoryNotFoundException>(() => DocumentStore.LoadAsync(missing));
        Assert.AreEqual(0, Directory.GetFiles(temp.Root).Length); temp.NoTemps();
    }

    [TestMethod]
    public async Task Store_DirectoryDestination_RejectsAndRetainsPriorArtifacts()
    {
        using var temp = new TempDirectory(); string occupied = temp.File("occupied");
        Directory.CreateDirectory(occupied); await File.WriteAllTextAsync(Path.Combine(occupied, "keep"), "retained");
        await Check.ThrowsAsync<ArgumentException>(() => DocumentStore.SaveAsync(occupied, Check.Document()));
        Assert.AreEqual("retained", await File.ReadAllTextAsync(Path.Combine(occupied, "keep")));
        Assert.AreEqual(1, Directory.GetDirectories(temp.Root).Length); temp.NoTemps();
    }

    [TestMethod]
    public async Task Save_ValidGraphExceedingSerializedByteBudget_PreservesTarget()
    {
        using var temp = new TempDirectory(); await File.WriteAllTextAsync(temp.File(), "sentinel");
        var d = Check.Document();
        d.Setlist = Enumerable.Range(0, 128).Select(_ => new SongEntry { Notes = new string('x', 32768) }).ToList();
        DocumentValidator.Validate(d); // 128*32768 notes already fill four MiB before JSON overhead.
        var error = await Check.ThrowsAsync<ArgumentException>(() => DocumentStore.SaveAsync(temp.File(), d));
        Assert.IsTrue(error.Message.Contains("4 MiB", StringComparison.Ordinal));
        Assert.AreEqual("sentinel", await File.ReadAllTextAsync(temp.File())); temp.NoTemps();
    }
}
