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
| Distribution | Published GitHub preview 0.1.0-preview.2 with x64/ARM64 ZIPs and checksums; passing hosted tests, x64 native startup/first-use controls, development-package install/activation/identity/uninstall, and earlier installed activation with the reserved Store identity | No Store publication. ARM64 hardware/install acceptance, full-product acceptance, first Store submission and certification not established |

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

The [published preview](https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.1)
was built from protected main `bee4205` by
[release run 40245073032](https://github.com/tinodo/Resonant-Pulse/actions/runs/40245073032).
The downloaded x64 ZIP's checksum and embedded source/version/run metadata were
verified before replacing the stopped local preview. The released app selected
the saved output, advanced from count-in into bar 1 after Start, then stopped
successfully and remained open.
[Store candidate run 40245073254](https://github.com/tinodo/Resonant-Pulse/actions/runs/40245073254)
passed installed activation with the reserved product identity; submission was
explicitly disabled. A candidate is not a certified or published Store app.

Practice-timer source adds a 0..86400-second live duration, localized
controls/countdown and an audio-frame deadline after count-in. Pauses preserve
remaining practice time; a finite program can end sooner. It does not truncate
the authored preview or exports. This source is not in preview 0.1.0-preview.1;
that version must not be described as including a timer.

Timer source `0a821dc` passed hosted
[run 40247889344](https://github.com/tinodo/Resonant-Pulse/actions/runs/40247889344):
214 Core tests passed with four platform skips, and 71 App tests passed;
Windows x64/ARM64 and native/package gates also passed. Its identical-tree
pull-request merge build `15c1c92` was exercised on the real saved Realtek output.
Count-in preserved the three practice seconds, automatic completion froze at
zero, pause froze musical position and countdown, and resume completed at zero.
The original public preview was then restored open and stopped, with preferences
restored byte-for-byte and no test recovery document retained.
These observations establish native state/processed-audio behavior, not
acoustic timing, listening quality or full-product acceptance.

[Preview 0.1.0-preview.2](https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.2)
now delivers the timer. Protected-main build 40249136627, CodeQL 40249136761
and release 40249141008 passed. The actual public x64 ZIP's checksum and embedded
`73b9266` source/version/run metadata were checked before local activation.
That released binary preserved three practice seconds during count-in, completed
at zero and held its position, then stopped. Test settings were removed and saved
preferences restored exactly before leaving the new release open and stopped.
No Store submission or acoustic/full-product acceptance is implied.

Beat/bar stopping is being extended in source: written meter beats and complete
authored bars, independent of audible clicks and BPM units. Enabled seconds,
beats, bars and finite-program limits compete; the earliest wins. This extension
is not in preview.2 and requires its own hosted/native verification and release.
