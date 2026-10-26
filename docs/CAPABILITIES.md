# Capability evidence and limitations

The full design in design/metronome-design.md remains the product target.
This is not a declaration that the preview completes that target. "Implemented"
below means source exists; installed-app behavior requires Windows/device
evidence separately.

| Family | Implemented source | Not yet delivered or established |
|---|---|---|
| Core time and rendering | Rational scheduling, explicit note unit, meters/groups, subdivisions/swing, accents, layered cycles, seeded probability, gaps, two ramp domains and synthesized PCM | Configurable lane phase-transition policies, sustained tones, physical-device timing/soak results |
| Play and transport | Native pulse instrument, tempo/meter controls, shared AudioGraph transport, pause/stop/mute, keyboard timing observations; first-use Windows default output, visible endpoint picker and compact Play screen; live seconds/written-beats/complete-bars auto-stop with saved-limit acknowledgment | Safe-boundary live edits; calibrated audible position; tap-in and free-running/no-bar mode. Current edits require Stop + Start. Full visual/usability acceptance remains outstanding |
| Studio | Lane/event controls, original built-ins and generators | Pickups, nested tuplets, metric modulation, comprehensive groove/microtiming, multiselect/clipboard and notation authoring |
| Train | Finite bar-authored programs, ramps and gaps; immutable completed keyboard-arrival report and CSV export in development | Public CSV delivery/interactive export acceptance; nested/time-authored programs, live hold/repeat/skip, adaptive coaching, goals/journals/history, recorded takes |
| Stage | Large instrument/setlist presentation and song controls | Seamless transitions: Next stops/flushes/restarts. No separate conductor/compact window, pedal/global/media control |
| Library and files | Local projects/preferences/recovery, WAV and checked MIDI export; aligned stereo PCM16 stem ZIP and manifest in development | Public stem delivery and DAW/file-picker acceptance; media/chart/PDF processing, pitch-preserving stretch, tempo-map import, CSV history and comprehensive exchange packages |
| Sound and integration | Synthesized clicks, per-lane gain/pan, output-only operation | Imported samples, voice/drone, multichannel routing, MIDI/microphone capture and calibration, live clocks/learn/Link/network sync |
| Accessibility/localization | Five-language source dictionaries, keyboard and automation handling, theme-aware controls | Musician translation review, Narrator/Contrast/enlarged-text/high-DPI installed-app acceptance |
| Distribution | Published GitHub preview 0.1.0-preview.3 with x64/ARM64 ZIPs and checksums; passing hosted tests, x64 native startup/first-use controls, development-package install/activation/identity/uninstall, and earlier installed activation with the reserved Store identity | No Store publication. ARM64 hardware/install acceptance, full-product acceptance, first Store submission and certification not established |

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
The per-user Start menu shortcut targets preview.3. This does not establish
acoustic accuracy, full-product acceptance or Store publication.

Tap-in is now being implemented in source, not in preview.3: explicitly armed
2–16 consecutive valid taps, visible progress/rejection, proportional program
tempo scaling, cancellation and no restored arming. The existing count-in is
preserved. Publication/recovery and audio preparation precede playback; there
is no phase-alignment or acoustic-start guarantee. Active render-endpoint
monitoring cancels selected-device removal/update intent without requiring
manual refresh; reconnecting cannot rearm it. Monitoring failure disables
tap-in with visible guidance, independently of manual Start.

Tap-in source `f2c81f3` passed all eight required gates, including hosted
[run 40259904811](https://github.com/tinodo/Resonant-Pulse/actions/runs/40259904811):
224 Core passes of 228 cases (four platform skips), 124 App passes, and
Windows x64/ARM64 native/package checks. Automated review 5646802630 reported
no open issues; it did not establish human/product acceptance.
The checksum/provenance-verified identical-tree x64 candidate `ef501ef`
passed real-output playback, counted limits and rejected-tap checks. The
editing-field Escape check remains blocked because Windows is locked and
LockApp owns keyboard focus. No keyboard input was sent to another process.
The public preview.3 and original preferences were restored after each attempt.
The verifier now targets the NumberBox's actual text editor and waits for
that exact owned element's focus identity, rather than assuming a focusable
wrapper or immediate focus transfer. Fresh full native completion, including
automatic tap-in playback, is still required before delivery. Physical
unplug/reconnect acceptance has not been established.

The approved approximate Italian tempo guide is being implemented on a stacked
feature branch, not delivered in preview.3. It lives under More tempo controls
and follows the numeric field, including tap-in synchronization, without changing
the document or transport. Multiple overlapping labels remain visible rather
than implying a uniquely correct musical classification. Bands use the
[Tunable tempo reference](https://tunableapp.com/tempo/) as approximate musical
background, not an interoperability standard; the extreme bands cover the app's
1..1000 range, and Larghissimo reaches 25 to cover fractional input continuously.
Beat unit and musical context remain explicit. Five-language guidance includes
invalid numeric entry feedback.
Source `be14ac3` passed hosted
[run 40262883080](https://github.com/tinodo/Resonant-Pulse/actions/runs/40262883080):
224 Core passes of 228 cases (four platform skips), 148 App passes and Windows
x64/ARM64 build/package gates. The hosted first-use report exposed initial
overlapping guide names; its audio/count/tap-in scenarios explicitly reported
`not-run-no-output`. CodeQL 40262883059 passed. Manually requested Copilot
review 5646968834 reported no open issues. The main-only review-completion
gate correctly skipped this stacked base; it must pass afresh after retargeting.
This is neither real-output guide acceptance nor delivery.

A read-only Help destination is being added on the next stacked branch, not
delivered in preview.3. It preserves the Play layout and shared transport and
offers quick-start and musical-reference text in all five languages. It is not
a new audio engine, automatic exercise or capture workflow. The hosted native
check visits Help, reads accessible guidance, returns to Play and requires the
stopped transport and idle tap-in state to remain unchanged.
Source `e256221` passed hosted
[run 40264310332](https://github.com/tinodo/Resonant-Pulse/actions/runs/40264310332):
224 Core passes of 228 cases (four platform skips), 149 App passes and Windows
x64/ARM64 build/package gates. The native report records
`help=read-only-help-navigation-no-automatic-start` and initial tempo-guide
exposure; playback/count/tap-in scenarios explicitly report `not-run-no-output`.
CodeQL 40264310378 passed. Manually requested Copilot review 5647035183 reported
no open issues, while retaining the prerequisite native/human acceptance caveat.
The main-only review-completion gate correctly skipped this stacked base and
must pass afresh after retargeting. This is hosted Help navigation evidence,
not real-output, full accessibility/visual acceptance or public delivery.

Aligned stem export is being implemented on the next stacked branch, not
delivered in preview.3. A single local atomic ZIP contains main/subdivision
audio, every authored lane and a manifest; every WAV shares frame zero and
the full finite duration. Count-in is audible only in main. Original lane
indices and compiled audible decisions are retained, including probability,
mute/solo, pan, gain and gap masks. Silent authored lanes remain represented,
even when there are no performer targets. The existing mixed-WAV and stem
paths share the same streaming PCM16 writer.
The UI confirms track count/uncompressed size before choosing a ZIP. Per-track
peak protection and PCM16 rounding do not promise bit-identical recombination.
Source `5e65447` passed hosted
[run 40266107491](https://github.com/tinodo/Resonant-Pulse/actions/runs/40266107491):
236 Core passes of 241 cases (five Windows-only skips), 150 App passes,
34 Windows export-integrity passes, eight Windows workspace passes and
x64/ARM64 build/package gates. Existing mixed-WAV tests still pass through
the extracted writer. The first Windows run exposed an overly narrow test
expectation: sharing denial can be IOException or UnauthorizedAccessException.
The regression now requires the actual denial codes, retained destination,
temporary cleanup and successful retry after unlocking.
Copilot identified indistinguishable concurrent audio fixtures; requests now
vary tempo, gain and pan and must have distinct durations. Review 5647171396
on `5e65447` reports no open issues, not human/prerequisite acceptance.
CodeQL 40266107475 passed. The hosted native report records
`stems=size-confirmation-cancelled-without-playback`, Help navigation and
tempo-guide exposure; audio/count/tap-in explicitly report `not-run-no-output`.
The main-only completion gate correctly skipped this stacked base and must
pass after integration. Actual file-picker/DAW behavior, acoustic/accessibility
acceptance and public delivery remain unestablished.

Completed timing-report CSV is being added on the next stacked branch, not
delivered in preview.3. Finishing a keyboard Timing Lab run freezes its original
frame window, sample rate, target, tolerance, expected/observed timestamps and
analysis. Original indices distinguish matches, misses and extras. Starting a
new measurement clears the prior export source; later project edits cannot
rewrite an already completed source. Active/invalidated runs are not exportable.
The local file uses atomic replacement, UTF-8/BOM, CRLF, eight fixed columns
and invariant round-trip numbers. User project/target labels are JSON-string
cells, preserving arbitrary content without formula or record injection.
Unmeasurable offset/drift/coverage fields are blank. This is neither automatic
practice history nor calibrated microphone/MIDI/acoustic timing.
Hosted numerical/file-integrity/localization checks and native disabled-before-
completion exposure are pending. Real keyboard-run/file-picker acceptance and
public delivery remain separate.
