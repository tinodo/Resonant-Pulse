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

### Stem export in development

The next preview's Library includes **Export WAV stems (ZIP)**. It creates
`main.wav` (main pulses, subdivisions and count-in), one numbered stereo PCM16
WAV per authored lane, and `manifest.json` in one locally selected archive.
Import every track at the same DAW origin: all retain frame zero and the same
finite length, including leading count-in silence on lane tracks. Lane names
are metadata, never archive paths.

Exports preserve the compiled seed/probability realization, lane mute/solo,
gaps, pan, lane gain and document master gain. Suppressed lanes remain silent
tracks. Program length or the configured export-bar count determines duration;
live practice limits do not shorten it. Independent 16-bit rounding and peak
protection mean re-summing need not reproduce the mixed WAV bit-for-bit.
The confirmation shows track count and uncompressed audio size before choosing
a destination. Each WAV is subject to RIFF's 4 GiB limit; RF64 is not supported.
The archive uses .NET's supported
[ZipArchive API](https://learn.microsoft.com/dotnet/api/system.io.compression.ziparchive?view=net-10.0).
This source feature is not included in published preview.3.

### Timing-report export in development

The next preview also adds **Export timing report (CSV)** in Train. Finish a
keyboard Timing Lab run first. The export freezes that completed run's original
project/target, frame window, tolerance, summary and original-index matches,
misses and extra attacks, even if the project is subsequently edited.
It is UTF-8 with a BOM, CRLF rows and eight fixed, invariant-numeric columns.
Project/target label cells are JSON strings: decode them once to recover exact
text, including quotes/control characters, without spreadsheet formula cells.
Unavailable offset/drift/coverage metrics are blank rather than invented zeroes.
This is keyboard input-arrival analysis, not calibrated acoustic performance,
microphone capture or automatic practice history. It is not in preview.3.

### CI

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
