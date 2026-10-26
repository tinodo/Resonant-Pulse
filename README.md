# Resonant Pulse

[![Build and test](https://github.com/tinodo/Resonant-Pulse/actions/workflows/ci.yml/badge.svg)](https://github.com/tinodo/Resonant-Pulse/actions/workflows/ci.yml)

A native Windows metronome and rhythm workstation in C#/.NET 10 and WinUI 3,
with the cyan/magenta instrument aesthetic of Resonant.

**Development status:** implementation in progress. The full reviewed design is
the product target, not a claim that every feature is delivered. See
[delivery evidence](docs/DELIVERY.md), [architecture](docs/ARCHITECTURE.md)
and [release operations](docs/RELEASING.md).

## Downloads

**Published previews:** [GitHub downloads](https://github.com/tinodo/Resonant-Pulse/releases),
with x64/ARM64 downloads and SHA-256 checksums. This is not the completed full product.

Verified preview ZIPs are published to [GitHub Releases](https://github.com/tinodo/Resonant-Pulse/releases)
only after the release workflow's engine tests and Windows builds pass. Extract
the entire ZIP and run `ResonantPulse.exe` on Windows 11 24H2 or later with a matching
architecture. Do not copy only the executable. Downloads include .NET and WinUI runtime
dependencies. Unsigned previews may show SmartScreen warnings; inspect the source
and SHA-256 checksum rather than disabling platform protection.

ARM64 compilation is distinct from ARM64 hardware validation. Do not interpret
the presence of an ARM64 artifact as proof of instrument/audio behavior.

**Microsoft Store:** reserved as product `9NRFH2C8X2QL`, but not yet published.
The exact Partner Center identity is configured. First submission, certification
and API authorization remain required; update automation is separately guarded.

## Development and verification

Play centers the large cyan/magenta metronome and its integrated BPM readout.
The dial fills the remaining performance area; it is not a thumbnail next to a
tempo form. Rhythm, Tempo, Practice, Project and Help open bounded panels.
Precise numeric tempo entry is in Tempo; tap, +/-1, meter, sound and pulse
strengths remain below the dial. Audio on the compact transport opens output
and volume settings. Long pulse grids scroll within their own bounded strip.
Main click sound and pulse accents (including Silent) now publish prepared
audio revisions during playback without restarting the musical clock. Existing
click tails finish in their original sound. A manual pulse choice overrides
automatic group emphasis only for that pulse slot; unedited group boundaries
keep their original emphasis, including across dotted-unit bars. The written
groups are preserved. Count-in retains
its own cue accents. Other tempo/rhythm edits still require Stop + Start.
This repair is not in preview.3; use a release built from the repaired source.
Revision-specific evidence is recorded in [PR 14](https://github.com/tinodo/Resonant-Pulse/pull/14).
Hosted checks without an audio endpoint do not establish hardware/listening acceptance.

Use the pinned .NET SDK and a Windows build agent. This repository's owner uses
a shared local machine: builds/tests run in GitHub Actions, not locally.

```powershell
# These commands are intended for CI or an isolated developer machine.
dotnet test tests\ResonantPulse.Core.Tests\ResonantPulse.Core.Tests.csproj -c Release
.\packaging\Generate-Assets.ps1
dotnet publish src\ResonantPulse.App\ResonantPulse.App.csproj -c Release -r win-x64 -p:Platform=x64 -p:DistributionMode=Portable --self-contained true
```

No account, cloud service, advertising or telemetry is required for the core
metronome. Persistence is local. Recording/input analysis, if available in a
particular build, must be explicitly enabled. See [privacy](docs/PRIVACY.md).

MIT licensed. Dependencies retain their respective licenses; see
[third-party notices](THIRD-PARTY-NOTICES.md).
