# ResonantPulse.Core

The UI-free engine behind [Resonant Pulse](https://tinodo.github.io/Resonant-Pulse/), a metronome and rhythm workstation for Windows.
It turns a rhythm document into an exact schedule of clicks, renders that schedule to audio and writes it as WAV, one WAV per track
(ZIP) or MIDI.

- **Model and files**: `PulseDocument` (settings, lanes, events, program steps, setlist) with `DocumentStore` for the local JSON
  format and `DocumentValidator`.
- **Schedule**: `ScheduleCompiler.Compile` places every click on an exact sample, with tempo ramps, meters, additive groups, lanes,
  gaps and seeded probability. Rhythm positions are exact fractions of a quarter note (`Rational`).
- **Audio**: `PcmRenderer.Fill` renders a compiled plan into a buffer without allocating; `WaveExporter` and `WaveStemExporter` write it
  to disk; `MidiExporter` writes a standard MIDI file.
- **Practice**: `TapTempoEstimator`, `TimingAnalyzer` and `TimingCsvExporter`, and ready-made rhythms in `BuiltInLibrary`,
  `RhythmGenerators` (Euclidean rhythms, rudiments) and `TempoPrograms`.

It targets .NET 10, has no package dependencies, and has no UI, Windows or audio-device dependency: the application's own audio
adapter is not part of it.

## Quick start

```csharp
using ResonantPulse.Core;

var document = new PulseDocument { Name = "Waltz" };
document.Settings.Tempo = 96;       // beats per minute, counted in the beat unit (a quarter note by default)
document.Settings.BeatsPerBar = 3;  // 3/4

// Place every click of four bars on an exact sample of a 48 kHz timeline.
RenderPlan plan = ScheduleCompiler.Compile(document, sampleRate: 48_000, bars: 4, includeCountIn: false);

// Render the first 100 ms: interleaved stereo floats.
var buffer = new float[2 * 4_800];
PcmRenderer.Fill(buffer, plan, startFrame: 0, channels: 2, masterGain: 0.5f, loop: false);

// Write the plan as a WAV file and the document as a MIDI file.
await WaveExporter.ExportAsync("waltz.wav", plan);
await MidiExporter.ExportAsync("waltz.mid", document, bars: 4);
```

Each package version is built from the tagged release of the application with the same version number, and the release workflow of the
repository pushes it only after the release itself is published. The library is the application's engine: it is versioned with the
application and has no separate stability guarantee, so read the
[release notes](https://github.com/tinodo/Resonant-Pulse/blob/main/docs/RELEASE-NOTES.md) before you move to a new version.

## Getting the package

GitHub Packages requires authentication to restore any NuGet package, public or not. Create a
[personal access token (classic)](https://github.com/settings/tokens) with the `read:packages` scope, and never commit it.

```powershell
dotnet nuget add source "https://nuget.pkg.github.com/tinodo/index.json" --name github --username <github-user> --password <token> --store-password-in-clear-text
dotnet add package ResonantPulse.Core
```

The flag stores the token unencrypted in your user-level NuGet configuration; NuGet can encrypt it on Windows only, so you can omit it
there. To take only `ResonantPulse.*` from GitHub Packages and everything else from nuget.org, add a `nuget.config` next to your
solution:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="github">
      <package pattern="ResonantPulse.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

[MIT license](https://github.com/tinodo/Resonant-Pulse/blob/main/LICENSE) · [Source](https://github.com/tinodo/Resonant-Pulse) ·
[Privacy](https://tinodo.github.io/Resonant-Pulse/privacy.html)
