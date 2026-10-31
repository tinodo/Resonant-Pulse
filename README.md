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
bar to change its strength (there is a bar for every click of the bar, including
the clicks that split a beat); pick the sound (Mute silences it whatever it is).
**Timer**, **Project** and **Help** sit in one quiet strip under Settings and
open compact panels in which **Done** applies and **Cancel** discards; Cancel
and Done are always the same size. If a saved project counts beats in a unit that
does not divide its bar (say 8/8 counted in dotted halves), Play says so and one
button switches to the unit that matches the time signature.

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

Settings, Library, Stage, Train and Studio follow one page design
([design notes](docs/design/pages-design.md)): titled pages of cards, fields as
wide as what they hold, plain-language text and no Apply button, because a change
takes effect as you make it. **Reduce motion** in Settings makes the marker on the
circle jump from beat to beat instead of gliding and hides the scrolling tape of
upcoming clicks. The export length is in Library, next to the export buttons: it
is the number of bars written to a WAV file, a MIDI file or the tracks of a ZIP when the project has no program.

In Library, **Export separate tracks (ZIP)** saves the project as one ZIP with a WAV file for the main beat, one for each
lane and a manifest. All the files start at the same moment and are equally long, so they line up when you put them at the
same place in music software. It asks first how many tracks it will save, how long each is and how much room they take
unzipped, and only then for the file. The count-in is in the main file only, and a lane that is muted, or left out by another
lane's Solo, still has its file, silent. The files added together are not guaranteed to give the exported WAV back: each one is
rounded to 16 bits and held at full scale on its own. Closing the window while it saves stops it and removes the half-written
file; closing waits up to 4 seconds for that, after its wait for the save, and an export that has not stopped by then is left
behind (the log says so), possibly with a temporary file beside the destination. The archive is described in [docs/WAV-STEMS.md](docs/WAV-STEMS.md).

On Train, the timing check compares when each key press (F, or the Hit button)
reached the app with when each click was due. When a check has finished, **Export
result (CSV)**, the last command of the check, saves that one result as a file for a
spreadsheet: the totals, then each click and each press. It holds the last finished
check only, until you start another one or close the app (it is not a practice
history), and it says when the keyboard delivered each press, not when anything was
heard, so it is not a calibration of your keyboard, speakers or Bluetooth device. The
column names and numbers in the file are English and the same in every language, so
a script reads every file the same way, and its fields are separated by commas: where
the Windows list separator is a semicolon (the Dutch, German, French and Spanish settings),
Excel may need the file imported with the comma as the delimiter instead of opened by double-clicking
(not tried; see [the file format](docs/TIMING-CSV.md)).

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
