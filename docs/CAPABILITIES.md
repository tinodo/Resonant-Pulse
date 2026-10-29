# Capability evidence and limitations

The full design in design/metronome-design.md remains the product target.
This is not a declaration that the preview completes that target. "Implemented"
below means source exists; installed-app behavior requires Windows/device
evidence separately.

| Family | Implemented source | Not yet delivered or established |
|---|---|---|
| Core time and rendering | Rational scheduling, explicit note unit, meters/groups, subdivisions/swing, accents, layered cycles, seeded probability, gaps, two ramp domains and synthesized PCM | Configurable lane phase-transition policies, sustained tones, physical-device timing/soak results |
| Play and transport | Native pulse instrument with an exact-tempo editor, live -/TAP/+, sheet-music time-signature picker with a custom builder, beat-strength strip with a bar for every click (the clicks that split a beat included, each with its own strength), shared AudioGraph transport, pause/stop/mute, keyboard timing observations; first-use Windows default output, visible endpoint picker and compact Play screen; live seconds/written-beats/complete-bars auto-stop with saved-limit acknowledgment; live sound, beat-strength, tempo, meter, tempo-unit, subdivision, swing, group and gap-bar edits spliced at the next pulse | Acoustic/hardware confirmation of live splices (automated checks have not played audible sound; a silent real-device run has passed); structural edits during a finite program (they need Stop + Start); calibrated audible position; tap-in and free-running/no-bar mode. Full visual/usability acceptance remains outstanding |
| Studio | Lane cards (name, Mute, Solo, rotate/invert/copy/remove, the strength of each beat as a strip, clicks per cycle, cycle length, start offset, sound, volume, pan, chance), an Add lane menu with original built-ins and a Euclidean generator with a preview, practice-program steps as cards (tempo curve, bars, repeats, own time signature, silent bars, a grouped-beats project handled with Clear grouping), custom beat positions kept as exact fractions with strength, volume, chance and tie, edits applied in the order made, Undo/Redo | Pickups, nested tuplets, metric modulation, comprehensive groove/microtiming, multiselect/clipboard and notation authoring. A change made in Studio while the metronome plays is saved at once but heard only after Stop and Start |
| Train | Practice program status, progress, planned-tempo chart and readable step rows; finite bar-authored programs, ramps and gaps (authored in Studio); a timing check whose Start, Hit and Finish are on only when they make sense | Nested/time-authored programs, live hold/repeat/skip, adaptive coaching, goals/journals/history, recorded takes |
| Stage | Now playing (the tempo once, large) beside a Setlist; the selected song as Up next, said in words, and selecting never interrupts playback; Next + count-in on the first screen; Lock editing and Always on top switches; notes saved when focus leaves the box | Seamless transitions: Next stops/flushes/restarts. No separate conductor/compact window, pedal/global/media control |
| Library and files | Local projects/preferences/recovery; My projects with search, name and date, and Open once a project is chosen; ready-made rhythms with a name, a sentence, meter and tempo; WAV and checked MIDI export, with the export length (the bars written when the project has no program) beside the export buttons | Stem/media/chart/PDF processing, pitch-preserving stretch, tempo-map import, CSV history and comprehensive exchange packages |
| Sound and integration | Synthesized clicks, per-lane gain/pan, output-only operation | Imported samples, voice/drone, multichannel routing, MIDI/microphone capture and calibration, live clocks/learn/Link/network sync |
| Accessibility/localization | Five-language source dictionaries (a test checks that every string exists in all five with matching placeholders), keyboard and automation handling, theme-aware controls, a Reduce motion switch (the marker jumps from beat to beat and the tape of upcoming clicks is hidden; also on whenever Windows has animation effects off), fields that give way in narrow windows and at larger text sizes | Musician translation review (the new and rewritten Dutch, German, French and Spanish text was written with GitHub Copilot; docs/design/glossary.md lists the terms to check first), Narrator/Contrast/enlarged-text/high-DPI installed-app acceptance, Windows text sizes above 100% on the real setting |
| Distribution | Published GitHub preview 0.1.0-preview.10 with x64/ARM64 ZIPs and checksums; passing hosted tests, x64 native startup/first-use controls, development-package install/activation/identity/uninstall, and earlier installed activation with the reserved Store identity | No Store publication. ARM64 hardware/install acceptance, full-product acceptance, first Store submission and certification not established |

No missing integration is advertised as functioning. In particular, arrival-time
keyboard analysis is not microphone calibration, MIDI file export is not live
MIDI clock, a static concept is not an app screenshot, and a development MSIX is
not a signed Store release.

Play makes the large, centered metronome and its BPM readout the primary
surface; its square geometry scales to the available area. The readout opens an
exact tempo editor; -, TAP and + sit beneath it, with the sheet-music
time-signature picker, the beat-strength strip and the sound selector below.
Timer (the practice timer and auto-stop), Project and Help are page tools, not
app settings: they act on the session being played, so they live on Play, in one
strip in the top-right corner directly under the Settings item, in the same
full-contrast text and level with Settings' right edge. As the window narrows the
strip drops its words, then takes a row of its own, and it never covers the dial.
Long strips scroll within their own bounded area with an overflow cue. A strip that fits does not scroll: the gaps between additive groups come out of its room first, so 12/8 shows all twelve beats beside the meter and sound controls. A strip that does not fit keeps room under the beat numbers for its scroll bar, which would otherwise be drawn over them.
The strip has a bar for every click of the bar. When beats are split (Subdivision above 1, as in the ready-made "Compound 6/8": counted in dotted quarters, each beat split in three) the clicks between the beats have bars of their own, smaller than a beat's, with a little space before each beat so they read as belonging to it. When the BPM unit is a whole number of written beats they are numbered by the written beat they fall on (6/8 felt in two reads 1 2 3 4 5 6, as the dial and the status line count) or with a dot when they fall between two written beats; with any other unit (an eighth note in 4/4) the beats are numbered 1, 2, 3 and on and the clicks that split them are dots, so no two beats share a number. Their names say which beat they belong to ("Beat 1, click 2 of 3, Soft"). The lit bar follows the click that is sounding; a count-in plays the beats alone, so during it the beat that sounded last stays lit. A tap sets a click's strength as it does a beat's, also while playing, and the strengths are saved with the project (`SubdivisionAccents`); a tap that reaches the project after a change of subdivision, meter or unit has already replaced the strip it was made on changes nothing, and a stored pattern longer than the bar (a program section in a longer meter reads the rest) keeps its length. A split click that is Soft plays at 65% of a Soft beat, as split clicks always have; a Medium or Strong one is as loud as a beat of that strength. A bar of more than 128 clicks shows its beats alone. Choosing a time signature from the picker gives one click per written beat: the strengths of the split clicks end with the old bar, and when the click unit changes the beats are no longer split. If the new beat is exactly what the split clicks were (6/8 felt in two becoming 6/8 in eighths), the tempo is raised by the old split so the same clicks keep coming at the same speed; otherwise the BPM number is kept.
Every panel has Cancel and Done side by side and the same size; Done applies.
Form rows on every page share one rule: a dropdown, a number box and a text box
are the same height, buttons in a row match, and a checkbox's label sits on its
box's centre line.

A saved BPM unit that does not divide the written bar (for example 8/8 counted in
dotted halves) makes the clicks drift against the dial. The unit picker never
offers such a unit, but an older or imported project can carry one, so Play shows
a notice with a one-click fix that applies the unit matching the time signature
and keeps the BPM number, so the click rate is unchanged. The notice is one row,
about 50 DIP high, with the fix beside the title; it wraps the fix below the title
only when the window is too narrow for both.

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
`ManualAccentIndices`, the strengths of split clicks (`SubdivisionAccents`) and the explicit All Normal override are saved in
projects. A project that sets one of those split-click strengths cannot be opened by earlier builds, which refuse a file with a
member they do not know. Count-in still uses its own cue accents and applies at the next Start.
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

[Preview 0.1.0-preview.8](https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.8)
polishes the Play page and the controls it shares with the other pages after an
owner review of preview.7. The time-signature digits are centred in their chip,
the mute button reads "Mute"/"Unmute", the audio picker and its refresh button
share one height, Cancel and Done are one size in every panel, and the Timer,
Project and Help buttons (Practice is now called Timer) form one labelled strip
under Settings in full-contrast text. A saved project whose tempo unit disagrees
with its written meter (for example eight beats counted in dotted halves, where
clicks fall every three quarter notes while the dial goes round once per four)
now shows a closable one-click notice that rewrites the unit and keeps the BPM
number, so the click rate does not change. Close requests are logged with their
source and the time since the last keyboard or mouse input. Protected main
`acfa43a`: main build 40422313141 and CodeQL 40422313125 passed; release
40422834681 passed fresh hosted tests (234 Core passes of 238 cases, four
platform skips, and 303 App passes), Windows x64/ARM64 builds and x64
native/package checks. The hosted first-use report recorded the digits centred
(0 px offset) and the page tools labelled and 44 DIP high at 800x600 and
1000x720. Hosted audio, live-edit and counted-stop scenarios explicitly reported
`not-run-no-output`. The actual public x64 ZIP checksum and embedded
source/version/run were checked; the ARM64 checksum file matched the GitHub
digest of the ARM64 ZIP, which was not downloaded and re-hashed.
The build was installed on the development PC after a dry run on copies of its
saved state: it started responsive and stopped, the saved preferences and
recovery draft were byte-identical after startup, and the per-user Start menu
shortcut now targets preview.8. Scratch instances of the shipped x64 binary
then ran the exact verifier's audio scenarios four times on the Windows default
output at an inaudible master gain (0.0001, isolated data folder, private
desktop). The first run failed before reaching them, at a Cancel/Done pair read
taken while a panel was still sliding in; that is a race in the verifier, not in
the product, and is fixed by waiting until the panel is still (PR #20). The other
three runs, one of them with the fixed verifier, passed: playback advanced, live
sound, tempo and meter edits were published without stopping, and the counted
stops passed. The instance installed for the owner played no audio, so this does
not establish acoustic accuracy, live-splice latency, Windows text-size or
Contrast-theme appearance, full-product acceptance or Store publication. At a
window width of about 320 DIP the page content still overflows the window, as in
preview.7; whether to impose a minimum window size is an open product decision.

[Preview 0.1.0-preview.9](https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.9)
makes two refinements after an owner review of preview.8. The unit-mismatch notice
is one row, about 51 DIP high instead of 109: an InfoBar lays its title and action
on one row only while the action button is at most 40 DIP tall, and the 44 DIP
every button in the theme had sent the fix to a second row. The beat strip no
longer shows a scroll bar for 12/8 (its cells were sized without the gaps between
additive groups, so the strip overflowed by about 20 DIP, cut off its last beat and
showed no "More beats" cue), and a strip that does have to scroll keeps room under
its beat numbers for the scroll bar, which used to be drawn over them. Protected
main `ea488bf`: main build 40469135784 and CodeQL 40469135742 passed; release
40469536270 passed fresh hosted tests (234 Core passes of 238 cases, four platform
skips, and 318 App passes), Windows x64/ARM64 builds and x64 native/package checks,
including the first-use verifier's new gates (the four meters it steps through,
6/8, 3/4, 12/8 and 4/4, fit their strip without a scroll bar or cue, 12/8 being
the widest of the twelve quick picks, and a 64-beat strip leaves room under its
numbers). Hosted audio, live-edit and counted-stop scenarios
explicitly reported `not-run-no-output`. The actual public x64 ZIP checksum and
embedded source/version/run were checked; the ARM64 checksum file matched the
GitHub digest of the ARM64 ZIP, which was not downloaded and re-hashed. Local
captures of a build from the same source, using the owner's saved project and its
language variants, measured the notice at 51 DIP on one row (the fix level with
the title) in English, Dutch, German, French, Spanish and the light theme, wrapping
to two rows (97 DIP) at 460 DIP wide, and the 12/8 strip with all twelve beats and
no bar at 1040x780 and 800x600; the scroll bar forced into its expanded form sat
under the beat numbers without touching them. The first-use verifier fails on the
preview.8 build at the 12/8 gate (the run stops there, so the scroll-bar-room gate
was not exercised against it) and passes on the new build, silent audio
scenarios included. The shipped x64 binary then ran the exact verifier's audio
scenarios once more on the Windows default output at an inaudible master gain
(0.0001, isolated data folder, private desktop): playback advanced, live sound,
tempo and meter edits were published without stopping, and the counted stops
passed. It was dry-run on private-desktop copies of the owner's saved state (the
preview.8-era project, whose notice measured 50.7 DIP with the fix centred, and the
current project) and installed on the development PC: the owner had closed
preview.8 (the log records the close command and input 0 s before), so Explorer
started the new build, it came up responsive and stopped, the saved preferences
and recovery draft were byte-identical to the pre-install backup, and the Start
menu shortcut now targets preview.9. No audio was played in the instance installed
for the owner. This does not establish real-pointer hover or the Windows "always
show scroll bars" setting on a real desktop, acoustic accuracy, live-splice
latency, Windows text-size or Contrast-theme appearance, full-product acceptance or
Store publication.

[Preview 0.1.0-preview.10](https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.10)
is a page-design round after an owner review of preview.9. Timer, Project, Help and
Settings had fields as wide as the window, an "Apply edits" button in the middle of a
form and settings nobody could explain, and Library, Stage, Train and Studio had had
little thought. They now follow one design (docs/design/pages-design.md), and the
fields in the Play panels are the same: titled pages of cards, fields as wide as what
they hold, plain-language text, empty states that say what to do next and no Apply
button on the pages, because a change takes effect as it is made and one the project
refuses goes back to the value it holds and says why. The Play panels (Timer, Project,
Help, tempo and time signature) keep Cancel and Done: a draft waits until Done applies
it, and Cancel discards it. "Reduce motion" is a switch that makes the marker on
the circle jump from beat to beat instead of gliding and hides the scrolling tape of
upcoming clicks; it is also on whenever Windows has animation effects off. The export
length (the bars written to a WAV or MIDI file when the project has no program; a
program decides its own length) moved to Library, next to the export buttons. Studio
lanes and practice-program steps are cards, custom beat positions are rows that keep a
click exact (a whole number, a fraction or a decimal), and edits are applied in the
order they are made: a Start waits for the edits asked for before it, and a Stop
cancels a Start that is still waiting. What is changed in Studio while the metronome
plays is saved at once and heard after Stop and Start. Protected main `e3c6836`: main
build 40779798042 and CodeQL 40779798161 passed; release 40780682093 passed fresh
hosted tests (234 Core passes of 238 cases, four platform skips, with no engine change
since preview.9, and 499 App passes), Windows x64/ARM64 builds and x64 native/package
checks, including the first-use verifier's gates for the redesigned pages. Hosted
audio, live-edit and counted-stop scenarios explicitly reported `not-run-no-output`.
The actual public x64 ZIP checksum and embedded source/version/run were checked; the
ARM64 checksum file matched the GitHub digest of the ARM64 ZIP, which was not
downloaded and re-hashed. The hosted Windows x64 check failed repeatedly on the
page-design branch (and on a rerun of the previous green run): a step name box that
was taken away with its page after a Move up committed a stale draft and left an error
notice open under the page. That is fixed, and the first-use verifier now reports any
open error notice. The shipped x64 binary then ran the exact verifier's audio
scenarios on the Windows default output at an inaudible master gain (0.0001, isolated
data folder, private desktop): playback advanced, live sound, beat-strength, tempo and
meter edits were published without stopping, and the counted stops passed. It was
dry-run on private-desktop copies of the owner's saved state (the current state,
whose last page is Settings, so the app opened there, and the preview.8-era state,
which opened on Play) and installed
on the development PC without being started: preview.9 stays on disk, the saved
preferences and recovery draft were byte-identical after startup and exit, and the
Start menu shortcut now targets preview.10. No audio was played in any instance
started for the owner. This does not establish acoustic accuracy, live-splice latency,
Windows text sizes above 100% on the real setting, Contrast-theme appearance, a
native-speaking musician's review of the new Dutch, German, French and Spanish text
(docs/design/glossary.md lists the terms to check first), full-product acceptance or
Store publication. When Windows activates the packaged app, keyboard focus starts on
Settings (initial focus is not managed yet), so its focus ring can show at launch.
