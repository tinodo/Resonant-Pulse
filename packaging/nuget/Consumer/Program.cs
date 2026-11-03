using System.Reflection;
using ResonantPulse.Core;

// What a user of the package does (the quick start of the package README), then checks on what came out of it. Run by
// packaging/Build-NuGetPackage.ps1 against the packed ResonantPulse.Core; any failure throws and the process exits non-zero.
var expectedVersion = args.Length == 2 ? args[0] : throw new ArgumentException("Pass the package version and the SHA-256 of the packaged ResonantPulse.Core.dll.");
var expectedHash = args[1];
var folder = Path.Combine(Path.GetTempPath(), "rp-consumer-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var document = new PulseDocument { Name = "Waltz" };
    document.Settings.Tempo = 96;
    document.Settings.BeatsPerBar = 3;

    RenderPlan plan = ScheduleCompiler.Compile(document, sampleRate: 48_000, bars: 4, includeCountIn: false);

    var buffer = new float[2 * 4_800];
    PcmRenderer.Fill(buffer, plan, startFrame: 0, channels: 2, masterGain: 0.5f, loop: false);

    var wavPath = Path.Combine(folder, "waltz.wav");
    var midiPath = Path.Combine(folder, "waltz.mid");
    await WaveExporter.ExportAsync(wavPath, plan);
    await MidiExporter.ExportAsync(midiPath, document, bars: 4);

    var assembly = typeof(PulseDocument).Assembly;
    var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
    Check(informational == expectedVersion || informational.StartsWith(expectedVersion + "+", StringComparison.Ordinal),
        $"the loaded assembly is version '{informational}', expected {expectedVersion}");
    // The build copies the package's assembly next to the program: it must be byte for byte the one inside the package.
    var actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(assembly.Location)));
    Check(string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase),
        $"the loaded assembly {assembly.Location} is not the one in the package (SHA-256 {actualHash}, expected {expectedHash})");

    // Four bars of 3/4 at 96 quarter notes a minute are 7.5 seconds.
    Check(plan.TotalFrames == 7 * 48_000 + 24_000, $"the plan is {plan.TotalFrames} frames, expected 360000");
    Check(plan.Onsets.Count >= 12, $"the plan has {plan.Onsets.Count} clicks, expected at least one a beat");
    Check(buffer.Any(sample => Math.Abs(sample) > 0.001f), "the first 100 ms of the plan are silent; the first click is on frame 0");

    var wav = await File.ReadAllBytesAsync(wavPath);
    Check(wav.Length > 44 && Ascii(wav, 0, 4) == "RIFF" && Ascii(wav, 8, 4) == "WAVE", "the WAV file has no RIFF/WAVE header");
    Check(BitConverter.ToInt32(wav, 24) == 48_000, "the WAV file is not at 48000 Hz");

    var midi = await File.ReadAllBytesAsync(midiPath);
    Check(midi.Length > 14 && Ascii(midi, 0, 4) == "MThd" && Ascii(midi, 14, 4) == "MTrk", "the MIDI file has no MThd and MTrk chunks");

    var documentPath = Path.Combine(folder, "waltz.pulse");
    await DocumentStore.SaveAsync(documentPath, document);
    var loaded = await DocumentStore.LoadAsync(documentPath);
    Check(loaded.Name == "Waltz" && loaded.Settings.Tempo == 96 && loaded.Settings.BeatsPerBar == 3, "the document did not survive a save and a load");

    var presets = BuiltInLibrary.Presets();
    Check(presets.Count > 0, "the built-in library has no presets");
    foreach (var preset in presets) ScheduleCompiler.Compile(preset.Document, 44_100, bars: 2);

    Console.WriteLine($"ResonantPulse.Core {informational} works from the package: {plan.Onsets.Count} clicks in {plan.TotalFrames} frames, " +
        $"{wav.Length} byte WAV, {midi.Length} byte MIDI, {presets.Count} presets compile.");
}
finally
{
    Directory.Delete(folder, recursive: true);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("Package check failed: " + message);
}

static string Ascii(byte[] bytes, int offset, int count) => System.Text.Encoding.ASCII.GetString(bytes, offset, count);
