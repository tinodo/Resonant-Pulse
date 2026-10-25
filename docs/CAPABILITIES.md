# Capability evidence and limitations

The full design in design/metronome-design.md remains the product target.
This is not a declaration that the preview completes that target. "Implemented"
below means source exists; installed-app behavior requires Windows/device
evidence separately.

| Family | Implemented source | Not yet delivered or established |
|---|---|---|
| Core time and rendering | Rational scheduling, explicit note unit, meters/groups, subdivisions/swing, accents, layered cycles, seeded probability, gaps, two ramp domains and synthesized PCM | Configurable lane phase-transition policies, sustained tones, physical-device timing/soak results |
| Play and transport | Native pulse instrument, tempo/meter controls, shared AudioGraph transport, pause/stop/mute, keyboard timing observations; first-use Windows default output, visible endpoint picker and a compact Play screen with expandable advanced controls; hosted first-use checks and local Start/advancing-position observation | Safe-boundary live edits; calibrated audible position; tap-in, free-running/no-bar mode and additional stop conditions. Current edits require Stop + Start. Full visual/usability acceptance remains outstanding |
| Studio | Lane/event controls, original built-ins and generators | Pickups, nested tuplets, metric modulation, comprehensive groove/microtiming, multiselect/clipboard and notation authoring |
| Train | Finite bar-authored programs, ramps and gaps | Nested/time-authored programs, live hold/repeat/skip, adaptive coaching, goals/journals/history, recorded takes |
| Stage | Large instrument/setlist presentation and song controls | Seamless transitions: Next stops/flushes/restarts. No separate conductor/compact window, pedal/global/media control |
| Library and files | Local projects/preferences/recovery, WAV and checked MIDI export | Stem/media/chart/PDF processing, pitch-preserving stretch, tempo-map import, CSV history and comprehensive exchange packages |
| Sound and integration | Synthesized clicks, per-lane gain/pan, output-only operation | Imported samples, voice/drone, multichannel routing, MIDI/microphone capture and calibration, live clocks/learn/Link/network sync |
| Accessibility/localization | Five-language source dictionaries, keyboard and automation handling, theme-aware controls | Musician translation review, Narrator/Contrast/enlarged-text/high-DPI installed-app acceptance |
| Distribution | Passing hosted tests, x64/ARM64 builds, x64 native startup and first-use controls, development-package install/activation/identity/uninstall verification, guarded release/Store workflows and reserved Store identity | No published GitHub release or Store listing yet. ARM64 hardware/install acceptance, first Store submission and certification not established |

No missing integration is advertised as functioning. In particular, arrival-time
keyboard analysis is not microphone calibration, MIDI file export is not live
MIDI clock, a static concept is not an app screenshot, and a development MSIX is
not a signed Store release.

First use resolves the Windows default render endpoint, rather than taking the
first enumerated device. The selection is then remembered. Losing a saved device
never routes playback elsewhere automatically. Start is unavailable until an
enumerated output is selected, and the output picker is on the transport surface.
The hosted native check verifies essential controls are visible and exercises
Start/Stop only when the runner has an output; its report explicitly records
`not-run-no-output` otherwise. This is not physical speaker acceptance.

Hosted run [40241390383](https://github.com/tinodo/Resonant-Pulse/actions/runs/40241390383)
passed the native startup, first-use and development-package checks for source
`b8ec875`. Separately, the delivered `f198cc0` preview selected the Windows-default
Realtek output on the development PC; invoking Start advanced through count-in to
bar 18, beat 4, at 00:37. Playback was then stopped and the app left open.
This establishes local playback progression, not audible quality, acoustic timing,
Contrast/Narrator acceptance or completion of the full product design.
