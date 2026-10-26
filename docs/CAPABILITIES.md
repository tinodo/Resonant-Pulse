# Capability evidence and limitations

The full design in design/metronome-design.md remains the product target.
This is not a declaration that the preview completes that target. "Implemented"
below means source exists; installed-app behavior requires Windows/device
evidence separately.

| Family | Implemented source | Not yet delivered or established |
|---|---|---|
| Core time and rendering | Rational scheduling, explicit note unit, meters/groups, subdivisions/swing, accents, layered cycles, seeded probability, gaps, two ramp domains and synthesized PCM | Configurable lane phase-transition policies, sustained tones, physical-device timing/soak results |
| Play and transport | Native pulse instrument with an exact-tempo editor, live -/TAP/+, sheet-music time-signature picker with a custom builder, beat-strength strip, shared AudioGraph transport, pause/stop/mute, keyboard timing observations; first-use Windows default output, visible endpoint picker and compact Play screen; live seconds/written-beats/complete-bars auto-stop with saved-limit acknowledgment; live sound, beat-strength, tempo, meter, tempo-unit, subdivision, swing, group and gap-bar edits spliced at the next pulse | Acoustic/hardware confirmation of live splices (automated checks have not played audible sound; a silent real-device run has passed); structural edits during a finite program (they need Stop + Start); calibrated audible position; tap-in and free-running/no-bar mode. Full visual/usability acceptance remains outstanding |
| Studio | Lane/event controls, original built-ins and generators | Pickups, nested tuplets, metric modulation, comprehensive groove/microtiming, multiselect/clipboard and notation authoring |
| Train | Finite bar-authored programs, ramps and gaps | Nested/time-authored programs, live hold/repeat/skip, adaptive coaching, goals/journals/history, recorded takes |
| Stage | Large instrument/setlist presentation and song controls | Seamless transitions: Next stops/flushes/restarts. No separate conductor/compact window, pedal/global/media control |
| Library and files | Local projects/preferences/recovery, WAV and checked MIDI export | Stem/media/chart/PDF processing, pitch-preserving stretch, tempo-map import, CSV history and comprehensive exchange packages |
| Sound and integration | Synthesized clicks, per-lane gain/pan, output-only operation | Imported samples, voice/drone, multichannel routing, MIDI/microphone capture and calibration, live clocks/learn/Link/network sync |
| Accessibility/localization | Five-language source dictionaries, keyboard and automation handling, theme-aware controls | Musician translation review, Narrator/Contrast/enlarged-text/high-DPI installed-app acceptance |
| Distribution | Published GitHub preview 0.1.0-preview.7 with x64/ARM64 ZIPs and checksums; passing hosted tests, x64 native startup/first-use controls, development-package install/activation/identity/uninstall, and earlier installed activation with the reserved Store identity | No Store publication. ARM64 hardware/install acceptance, full-product acceptance, first Store submission and certification not established |

No missing integration is advertised as functioning. In particular, arrival-time
keyboard analysis is not microphone calibration, MIDI file export is not live
MIDI clock, a static concept is not an app screenshot, and a development MSIX is
not a signed Store release.

Play makes the large, centered metronome and its BPM readout the primary
surface; its square geometry scales to the available area. The readout opens an
exact tempo editor; -, TAP and + sit beneath it, with the sheet-music
time-signature picker, the beat-strength strip and the sound selector below.
Practice, Project and Help are a quiet rail (a horizontal strip at narrow
widths). Long strips scroll within their own bounded area with an overflow cue.
Every panel has Cancel and Done side by side; Done applies.

Live edits prepare immutable replacement revisions off the audio callback.
Sound and beat-strength changes switch at the next audio quantum on the same
musical clock (also during a finite program) and preserve prior attack tails.
Tempo, meter, tempo unit, subdivision, swing, groups and gap bars are spliced in
at the first primary pulse at or after the render cursor: the bar position
continues when meter, unit and loop length are unchanged, and otherwise restarts
at beat 1 without replaying the count-in. Earlier clicks keep ringing under the
new revision; the wall-clock practice timer keeps its absolute stop frame, while
beat and bar limits are counted from the restart. Only the newest unapplied
request survives, so rapid taps coalesce. A finite program keeps its own clock:
Play replaces its tempo and meter controls with an explanation, and structural
changes made elsewhere need Stop + Start. Explicit pulse edits take precedence
over automatic grouping emphasis only for edited slots; per-slot
`ManualAccentIndices` and the explicit All Normal override are saved in
projects. Count-in still uses its own cue accents and applies at the next Start.
The splice arithmetic is covered by offline tests that compare rendered samples;
no listening or hardware-latency acceptance is claimed. Releases up to
0.1.0-preview.5 need Stop + Start for tempo and meter edits.

First use resolves the Windows default render endpoint, rather than taking the
first enumerated device. The selection is then remembered. Losing a saved device
never routes playback elsewhere automatically. Start is unavailable until an
enumerated output is selected. The transport's Audio button exposes output and
volume; unavailable-output guidance remains visible without opening that panel.
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
is not in preview.2; preview.3 below delivers it.

Source `e0f1443` passed hosted run 40251307317: 224 Core passes with four
platform skips and 98 App passes; Windows x64/ARM64/native-package gates passed.
Its x64 candidate was exercised on the real saved output: four beats won over
eight seconds and two bars, a 17-bar stop crossed the actual 16-bar loop seam
and froze on bar 17/beat 4 at 00:36 including count-in, and a paused 16-beat run
resumed to completion at 00:10. Original preview.2 and preferences were restored.
The native first-use verifier now repeats these scenarios when an output exists,
and reports `practiceStops=not-run-no-output` otherwise. This is not acoustic or
full-product acceptance.
The exact verifier also passed on the real-output PC. Its read-only searches
reacquire the window and wait for temporarily rebuilt peers, and commands wait
for enabled controls. Actions are not blindly retried.
Subsequent review found that enabled controls alone do not establish completion
of asynchronous Apply. The verifier now waits for a fresh success generation
containing the requested three values, exposed only after publication and
recovery saving return successfully. The same surface visibly confirms saved
limits in all five languages; failed or locked edits do not report success.
Synchronization source `0e4676a` passed all eight required gates, including
hosted run 40253517046. Its identical-tree x64 candidate `403c6e6` passed the
exact repository verifier on the real saved output, including all three counted
scenarios and acknowledgment of the final reset. Original preview.2 was reopened
stopped after exact preference restoration. Review 5646381643 reported no open
issues; it was a commented automated review, not human/full-product acceptance.

[Preview 0.1.0-preview.3](https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.3)
delivers counted stops from protected main `165785f`. Main build 40254604426
and CodeQL 40254604425 passed; release 40254973052 passed fresh hosted tests
(224 Core passes of 228 cases, four platform skips, and 98 App passes),
Windows x64/ARM64 builds and x64 native/package checks. Hosted audio scenarios
explicitly reported `not-run-no-output`.
The actual public x64 ZIP checksum and embedded source/version/run were checked.
On the real saved output, the exact verifier passed first Start/position advance,
competing limits, the 17-bar loop seam, counted pause/resume and acknowledged
final reset. Original preferences were restored byte-for-byte, diagnostic
recovery removed, and preview.3 reopened responsive and stopped with limits off.
The per-user Start menu shortcut then targeted preview.3. This does not establish
acoustic accuracy, full-product acceptance or Store publication.

[Preview 0.1.0-preview.6](https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.6)
delivers the Play redesign and live edits from protected main `44a60d6`. Main
build 40371460154 and CodeQL 40371460083 passed; release 40372330283 passed fresh
hosted tests (232 Core passes of 236 cases, four platform skips, and 260 App
passes), Windows x64/ARM64 builds and x64 native/package checks. Hosted audio,
live-edit and counted-stop scenarios explicitly reported `not-run-no-output`.
The actual public x64 ZIP checksum and embedded source/version/run were checked.
The build was then installed on the development PC after a dry run on copies of
its saved state: it started responsive and stopped, the saved preferences and
recovery draft were byte-identical after startup, and the per-user Start menu
shortcut then targeted preview.6. No audio was played, so this does not establish
acoustic accuracy, live-splice latency, full-product acceptance or Store
publication. A review of its source listed three findings that preview.7 fixes.

[Preview 0.1.0-preview.7](https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.7)
fixes three findings from Copilot's re-review of the Play redesign, which listed
them as previously missed while reporting no open inline issues: the position
readout could pair a pre-splice frame with the new revision, a live gap-bar edit
could leave up to 50 ms of the previous click ringing into a bar that is now
silent, and the caption icons ignored Windows Contrast themes. Protected main
`0050cdc`: main build 40380680897 and CodeQL 40380680942 passed; release
40381406205 passed fresh hosted tests (234 Core passes of 238 cases, four
platform skips, and 271 App passes), Windows x64/ARM64 builds and x64
native/package checks. Hosted audio, live-edit and counted-stop scenarios
explicitly reported `not-run-no-output`.
The actual public x64 ZIP checksum and embedded source/version/run were checked.
The build was installed on the development PC after a dry run on copies of its
saved state: it started responsive and stopped, the saved preferences and
recovery draft were byte-identical after startup, and the per-user Start menu
shortcut now targets preview.7. Scratch instances of that installed binary then
passed the exact verifier's audio scenarios three times in a row on the Windows
default output at an inaudible master gain (0.0001, isolated data folder):
playback advanced, live sound, beat-strength, tempo and meter edits were
published without stopping, and the counted stops passed. An earlier attempt had
failed intermittently because the verifier's first-position check raced the
count-in, whose status text stays fixed for the whole two-second bar; playback
advanced normally in preview.5, 6 and 7, and the check now waits for the text to
change. The instance installed for the owner played no audio, so this does not
establish acoustic accuracy, live-splice latency, Contrast-theme appearance,
full-product acceptance or Store publication.
