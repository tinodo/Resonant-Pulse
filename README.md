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

Play centers the large cyan/magenta metronome and its BPM readout. Click the
number to type an exact tempo; use -, **TAP** and + under the dial (hold - or +
to repeat, Ctrl for steps of ten); choose a sheet-music time signature (2/4,
3/4, 4/4, 6/8 and more, with **Custom...** for the full builder); click a beat
bar to change its strength; pick the click sound. Practice, Project and Help
open compact panels in which **Done** applies and **Cancel** discards.

A running metronome follows every Play control without stopping. Sound and beat
strengths switch at the next audio quantum. Tempo, time signature, tempo unit,
subdivision, swing, groups and gap bars are spliced in at the next beat: the bar
carries on when the meter is unchanged and restarts at beat 1 when it changes,
and the tails of earlier clicks finish in their original sound. Count-in and
practice limits apply at the next Start. A project with a Studio/Train program
keeps the program's own tempo and meter (Play says so instead of editing an
unused value); sound and beat strengths still follow it live. Hosted checks
without an audio endpoint, and the unit tests that compare rendered samples,
do not establish hardware or listening acceptance.

Use the pinned .NET SDK on Windows. The owner has authorized local builds and
tests on the development PC; keep them serialized, silent and isolated. A test
instance must set `RESONANT_PULSE_HOME` to a scratch folder so real projects and
settings are never touched. After building, `packaging\Verify-Local.ps1` runs
the native first-use checks against your own build on a private desktop: no
window appears, nothing is played, no screen is copied and no keystroke can reach
another window. `packaging\Verify-FirstUse.ps1` is the underlying verifier;
outside GitHub Actions it plays no audio, copies no screen and sends no keys
unless asked (`-RunAudio`, `-Screenshots`, `-Keyboard`). GitHub Actions remains the
authoritative gate.

```powershell
dotnet test tests\ResonantPulse.Core.Tests\ResonantPulse.Core.Tests.csproj -c Release
dotnet test tests\ResonantPulse.App.Tests\ResonantPulse.App.Tests.csproj -c Release
.\packaging\Generate-Assets.ps1
dotnet publish src\ResonantPulse.App\ResonantPulse.App.csproj -c Release -r win-x64 -p:Platform=x64 -p:DistributionMode=Portable --self-contained true
```

No account, cloud service, advertising or telemetry is required for the core
metronome. Persistence is local. Recording/input analysis, if available in a
particular build, must be explicitly enabled. See [privacy](docs/PRIVACY.md).

MIT licensed. Dependencies retain their respective licenses; see
[third-party notices](THIRD-PARTY-NOTICES.md).
