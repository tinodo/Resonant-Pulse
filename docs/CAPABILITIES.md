# Capability evidence and limitations

The full design in design/metronome-design.md remains the product target.
This is not a declaration that the preview completes that target. "Implemented"
below means source exists; installed-app behavior requires Windows/device
evidence separately.

| Family | Implemented source | Not yet delivered or established |
|---|---|---|
| Core time and rendering | Rational scheduling, explicit note unit, meters/groups, subdivisions/swing, accents, layered cycles, seeded probability, gaps, two ramp domains and synthesized PCM | Configurable lane phase-transition policies, sustained tones, physical-device timing/soak results |
| Play and transport | Native pulse instrument with an exact-tempo editor, live -/TAP/+, Tap to start (a switch at the end of the tempo controls: 2 to 16 steady taps, counted in the Tempo panel, set the tempo in the project's note unit and then start the metronome; a project with a program is scaled in proportion), sheet-music time-signature picker with a custom builder, beat-strength strip with a bar for every click (the clicks that split a beat included, each with its own strength; a bar whose unit does not divide it, or of more than 128 clicks, shows its beats alone), shared AudioGraph transport, pause/stop/mute, keyboard timing observations; a read-only Help panel (a five-step quick start, Tap to start and its limits, what is heard at once and what waits for Stop and Start, the keyboard shortcuts and where they work, and a short musical reference with an example each: BPM with a note value, time signatures and the click, subdivision and swing, the strength of a beat); a tempo guide in the Tempo panel that names the approximate Italian tempo markings of the speed it holds (below); first-use Windows default output, visible endpoint picker and compact Play screen; live seconds/written-beats/complete-bars auto-stop with saved-limit acknowledgment; live sound, beat-strength, tempo, meter, tempo-unit, subdivision, swing, group and gap-bar edits spliced at the next pulse | Acoustic/hardware confirmation of live splices (automated checks have not played audible sound; a silent real-device run has passed); structural edits during a finite program (they need Stop + Start); calibrated audible position; free-running/no-bar mode. Tap to start is not phase-aligned and not acoustically calibrated, nobody has listened to it, and unplugging a real output while it listens has not been tried (see below). The Help text is checked against the engine by tests and against the code by reading, not by listening, and where its keyboard shortcuts work (T only on Play with no panel open, Space only when no button has the focus, Esc only with no panel open) is read from the code: no gate presses Space, T, F or Esc. The tempo guide's bands are approximate background from one published table, not a standard, and the guide has been checked by tests and a window-layout gate, not by a musician or a screen reader. Full visual/usability acceptance remains outstanding |
| Studio | Lane cards (name, Mute, Solo, rotate/invert/copy/remove, the strength of each beat as a strip, clicks per cycle, cycle length, start offset, sound, volume, pan, chance), an Add lane menu with original built-ins and a Euclidean generator with a preview, practice-program steps as cards (tempo curve, bars, repeats, own time signature, silent bars, a grouped-beats project handled with Clear grouping), custom beat positions kept as exact fractions with strength, volume, chance and tie, edits applied in the order made, Undo/Redo | Pickups, nested tuplets, metric modulation, comprehensive groove/microtiming, multiselect/clipboard and notation authoring. A change made in Studio while the metronome plays is saved at once but heard only after Stop and Start |
| Train | Practice program status, progress, planned-tempo chart and readable step rows; finite bar-authored programs, ramps and gaps (authored in Studio); a timing check whose Start, Hit and Finish are on only when they make sense, with an Export result (CSV) command beside them that saves the last completed check as one CSV file (below, docs/TIMING-CSV.md) | Nested/time-authored programs, live hold/repeat/skip, adaptive coaching, goals/journals/history (the export holds one check, in memory, until another check starts or the app closes: it is not a history), recorded takes. The export is of what the keyboard or the Hit button delivered to the app, not of what was played or heard: see below |
| Stage | Now playing (the tempo once, large) beside a Setlist; the selected song as Up next, said in words, and selecting never interrupts playback; Next + count-in on the first screen; Lock editing and Always on top switches; notes saved when focus leaves the box | Seamless transitions: Next stops/flushes/restarts. No separate conductor/compact window, pedal/global/media control |
| Library and files | Local projects/preferences/recovery; My projects with search, name and date, and Open once a project is chosen; ready-made rhythms with a name, a sentence, meter and tempo; WAV and checked MIDI export, with the export length (the bars written when the project has no program) beside the export buttons | Stem/media/chart/PDF processing, pitch-preserving stretch, tempo-map import, CSV history (the Train page exports only the last completed timing check, as one CSV file that you save: that is not a history, because no earlier check is kept) and comprehensive exchange packages |
| Sound and integration | Synthesized clicks, per-lane gain/pan, output-only operation | Imported samples, voice/drone, multichannel routing, MIDI/microphone capture and calibration, live clocks/learn/Link/network sync |
| Accessibility/localization | Five-language source dictionaries (a test checks that every string exists in all five with matching placeholders), keyboard and automation handling, theme-aware controls, a Reduce motion switch (the marker jumps from beat to beat and the tape of upcoming clicks is hidden; also on whenever Windows has animation effects off), fields that give way in narrow windows and at larger text sizes | Musician translation review (the new and rewritten Dutch, German, French and Spanish text, the Help panel's and the tempo guide's sentences included, was written with GitHub Copilot; docs/design/glossary.md lists the terms to check first), Narrator/Contrast/enlarged-text/high-DPI installed-app acceptance, Windows text sizes above 100% on the real setting |
| Distribution | Published GitHub preview 0.1.0-preview.11 with x64/ARM64 ZIPs and checksums; passing hosted tests, x64 native startup/first-use controls, development-package install/activation/identity/uninstall, and earlier installed activation with the reserved Store identity | No Store publication. ARM64 hardware/install acceptance, full-product acceptance, first Store submission and certification not established |

No missing integration is advertised as functioning. In particular, arrival-time
keyboard analysis is not microphone calibration, MIDI file export is not live
MIDI clock, a static concept is not an app screenshot, and a development MSIX is
not a signed Store release.

Play makes the large, centered metronome and its BPM readout the primary
surface; its square geometry scales to the available area. The readout opens an
exact tempo editor; -, TAP and + sit beneath it, with Tap to start at their end and the sheet-music
time-signature picker, the beat-strength strip and the sound selector below.
Timer (the practice timer and auto-stop), Project and Help are page tools, not
app settings: they act on the session being played, so they live on Play, in one
strip in the top-right corner directly under the Settings item, in the same
full-contrast text and level with Settings' right edge. As the window narrows the
strip drops its words, then takes a row of its own, and it never covers the dial.
Long strips scroll within their own bounded area with an overflow cue. A strip that fits does not scroll: the gaps between additive groups come out of its room first, so 12/8 shows all twelve beats beside the meter and sound controls. A strip that does not fit keeps room under the beat numbers for its scroll bar, which would otherwise be drawn over them.
The strip has a bar for every click of the bar (a bar that is too long, or whose unit does not divide it, shows its beats alone, as below). When beats are split (Subdivision above 1, as in the ready-made "Compound 6/8": counted in dotted quarters, each beat split in three) the clicks between the beats have bars of their own, smaller than a beat's, with a little space before each beat so they read as belonging to it. When the BPM unit is a whole number of written beats they are numbered by the written beat they fall on (6/8 felt in two reads 1 2 3 4 5 6, as the dial and the status line count) or with a dot when they fall between two written beats (a split click is placed where Core plays it, so one that swing pulls off a written beat is a dot too); with any other unit (an eighth note in 4/4) the beats are numbered 1, 2, 3 and on and the clicks that split them are dots, so no two beats share a number. Their names say which beat they belong to ("Beat 1, click 2 of 3, Soft"). The lit bar follows the click that is sounding, and nothing is lit while the audio has not yet taken a change of unit, subdivision or swing (it takes it a beat after the change); a count-in plays the beats alone, so during it the beat that sounded last stays lit. A tap sets a click's strength as it does a beat's, also while playing, and the strengths are saved with the project (`SubdivisionAccents`); a tap that reaches the project after a change of subdivision, meter or unit has already replaced the strip it was made on changes nothing, and a stored pattern longer than the bar (a program section in a longer meter reads the rest) keeps its length. A split click that is Soft plays at 65% of a Soft beat, as split clicks always have; a Medium or Strong one is as loud as a beat of that strength. A bar of more than 128 clicks shows its beats alone, and so does a bar whose saved unit does not divide it evenly (an imported project; the unit notice offers the fix), because Core cuts the clicks of its last, partial beat at the bar line. Choosing a time signature from the picker sets the click unit to the written beat (every written beat is a click): the strengths of the split clicks end with the old bar, and when the click unit changes the beats are no longer split (a split is kept when the unit stays the same, as from 4/4 to 3/4). If the new beat is exactly what the split clicks were (6/8 felt in two becoming 6/8 in eighths), the tempo is raised by the old split, to at most 1000 BPM, so the same clicks keep coming at the same speed unless that limit is reached; otherwise the BPM number is kept.
Every panel that edits something has Cancel and Done side by side and the same size; Done applies. Help only shows text, so it has Done alone.
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

Tap to start listens for taps instead of waiting for Start. It is explicit (a switch beside TAP, or nothing),
one-shot (never saved: the application always opens stopped, silent and not listening) and available only while
the metronome is stopped and an output is selected. It sits beside -, TAP and + with its words when there is room
on both sides of them (the controls stay centred under the dial), as an icon alone when there is only room for that,
as an icon at the end of the controls when the window is narrower still, and on a row of its own only in the narrowest
windows, which costs the dial some height; the dial keeps its size at 800x600 and 1000x720. The room for these forms is
the page's width, not the width of the row that holds the switch (a row wider than the page is clipped at its own width and
reports that as its room): the code merged with PR #33, which was never released, showed 2 DIP of the switch at a window
width of 320 DIP on a 100% display, and PR #34 fixed it; the switch is on its own row, 124 DIP wide (152 in Dutch), at
320 DIP at 100% and at 150%, and the first-use verifier fails on a switch narrower than 40 DIP. The number of taps (2 to 16,
4 until changed) is an app preference kept in the Tempo panel: Cancel discards what was typed, Done keeps it, and it
counts the next time the switch goes on. Done writes only what was changed in the panel, so a number of taps changed
alone neither ends Tap to start nor touches the project (a tapped tempo that is being saved stays); a tempo or unit
changed there ends it, as a tempo chosen by hand does. While it listens, TAP and the T key count the taps ("3 of 4 taps"), the dial
shows the tempo they make so far, and Stop, Esc or the switch cancel. The tempo is the estimate to the nearest 0.1 BPM
in the project's own note unit, so the unit is never changed; the last tap saves it as one edit of the project (one
step of Undo) and only then runs the normal Start, so the project's count-in still plays. A tap that is not steady
(a bounce, a tap far from the beat of the earlier ones, a tap that is not later than the one before) ends the run and
the count begins again ("Tap again"); a pause of more than four seconds between two taps begins a new count, however slow
the taps were; so taps that are not steady never start playback. A project with a program is scaled in proportion: the first step starts at the tapped tempo and
every step's start and end tempo is multiplied by the same exact ratio (meters, ramps, repeats, count-in and the
setlist are untouched); TAP works only while the switch is on, because there is no tempo of its own to tap, and the
number of taps cannot be changed then (the tempo readout is not a button). A tempo outside 1 to 1000 BPM, a project that
is locked, or a tempo that could not be saved starts nothing and ends Tap to start (the notice for a tempo outside the
limits says to switch it on again and tap a different tempo). Stop, a start or pause by hand, a tempo chosen by hand,
leaving Play or changing the language, a change or loss of the chosen output, sleep and closing the window all cancel
it, and nothing arms it again. While it listens a Windows device watcher reports the chosen output being removed or
updated, so the intent ends then and not after the last tap; the watcher exists only while the intent does, and if
Windows cannot watch the outputs the switch stays off and says so (Start still works). It is not phase-aligned (a tap
is timed when the window handles it, not when the key went down; the start follows the last tap by the time saving and
preparing the audio take, plus the count-in) and it is not acoustically calibrated.

Help is read-only (Done alone, since there is nothing to cancel) and grows the panel Play already had: no page, no control on
Play, and the dial keeps its size. It opens at its first line every time, and its text scrolls inside the panel, so Done is on
screen at every window size down to 320x900; the text area is a tab stop. It reads in this order: a five-step quick start (the
output, Start or Space (when no button has the focus; a new project counts in one bar first), the tempo, the time signature, the
strength of a beat); Tap to start, how it works and its limits (2 to 16 taps, only while stopped and with an output chosen, an
uneven tap or a pause of more than four seconds begins the count again, Stop cancels it and so does Esc when no panel is open,
the metronome starts a moment after the last tap); While it
plays (Pause keeps the place, Stop goes back to the beginning so the next Start plays the count-in again, Mute keeps the count
going; sound and the strength of a beat change at once, tempo, time signature, note value, subdivision and swing from the next
beat, and the count-in, the Timer limits and everything in Studio only after Stop and Start; a project with a program takes its
tempo and time signature from the program, so their controls are off); the keyboard
shortcuts and where they work (this window only; T only on Play with no panel open; Space starts or pauses only when no button
has the focus, and presses that button otherwise; Space, T and F do nothing while a box has the focus; Ctrl+M, Ctrl+S and
Ctrl+O still work); and a musical reference with one example each (80 BPM counted in dotted quarters in 6/8 is as fast
as 240 BPM in eighths; with Subdivision 1, a time signature gives one click for every written beat, so 6/8 clicks six times a bar,
or twice with a dotted-quarter note value (a subdivision is kept when the note value changes, so with Subdivision 3 that is six
again); a subdivision of 3 gives three clicks a beat, swing needs an even subdivision, and with quarter notes as the note value
and Subdivision 2 about 0.67 swings the eighth notes (with eighth notes as the note value the same setting swings sixteenth
notes); a Silent click makes no sound and keeps its
place, and the clicks between beats start Soft). Each sentence names a control with the label it has in the language shown,
inserted from the control's own string, so a renamed control renames the Help; the few names that are written into a sentence
(the Tempo panel, the Subdivision and Swing fields) are pinned to their labels by a test. It explains Play and what Play shares with the
other pages; it does not walk through Studio, Train, Stage, Library or Settings. Tests compute each claim and example from the
engine (HelpFactsTests) and check the order, the length, the labels and the absence of the engine's words in all five languages
(HelpContentTests). The first-use verifier opens the panel at the first-use size, 800x600, 1000x720, 400x720 and 320x900 and in
Dutch, and requires the text area, Done and the first step to be whole and inside the window and large enough (Done at least 100
by 40 DIP, the text area at least 200 DIP high, a step at least 150 DIP wide), the text to scroll to its last line and back, the panel to
open at its first line again after being closed at its last, no
Apply button, the transport, the dial and the error notice to be as they were once it is closed, and, where keys can be sent
(a hosted runner), Page Down, End and Home to scroll the text and Tab to go on to Done. The verifier does not read the words: a
sentence that is wrong but fits the panel passes it, and only the unit tests catch it.

The Tempo panel names the approximate Italian tempo markings of the speed it holds (Larghissimo, Grave, Largo, Larghetto, Adagio, Andante,
Moderato, Allegro, Vivace, Presto, Prestissimo) in a line at the right of its title, the tempo guide. It adds no row to Play and the dial
keeps its size. The bands are in quarter notes a minute and overlap on purpose (120 is Moderato and Allegro, 42 is Grave and Largo); they
follow one published table of plain BPM (tunableapp.com/tempo, read on 30 October 2026) and are background, not a standard: the line says
"approximate" because the speed that suits a marking varies with the piece, the period and the performer. The slowest band is open
downwards and the fastest upwards, so every tempo the box accepts (1 to 1000 BPM) has a marking. A marking is about a speed, so the line
counts quarter notes a minute whatever note value the number counts: the tempo in the box times the length of its note in quarter notes
(the time-signature picker sets the note to the written beat, so 6/8 counts eighth notes), which makes 120 counted in eighth notes 60
quarter notes a minute (Largo and Larghetto) and 80 counted in dotted quarters 120. A tempo counted in quarter notes is the speed itself,
which the note value beside the box says, so the line only names the markings; with any other note value it also says the speed in quarter
notes that they are for ("Approximate markings at 60 BPM in quarter notes: Largo / Larghetto"). The line names the tempo that Done would
apply (Done rounds it to a tenth), not what the project holds: it follows the box when the box takes its number (Enter, leaving the box, a
stepper, Half or Double tempo; not each key typed) and when the note value changes, before Done, and Cancel discards the draft with it. An
emptied box, or a tempo or note value the project would refuse, has no marking, and the line says which tempos it can name instead. A
project with a program has no tempo readout to open the panel from, so it has no guide either. The names are the same in all five languages
and are code, not entries of the translated text; only the sentences around them are translated, with no-break spaces that keep a pair of
names, and a number with BPM, on one line where the line wraps. It reads two fields and writes nothing: it changes nothing of how the app
plays, the project, the note value or the transport. Where it fits beside the title (one line) it costs the panel's fields nothing; where
it wraps (two lines at 400x720 and at 320x900, three at 320x900 for a note other than a quarter) it takes up to 5.3 DIP (21.3 for
three lines) from the room of the fields, the box for the number of taps stays whole, and what is taken comes out of the caption under that
box (docs/DELIVERY.md has the measurements). The first-use verifier opens the panel at the first-use size, 800x600, 1000x720, 400x720 and
320x900 and in Dutch, and requires the line to be whole, inside the window and the panel, above the tempo box and at least 100 by 14 DIP,
the box for the number of taps to be whole above Done wherever it is whole without the guide (not at 800x600, where the panel is already
cut), and the dial and the transport to be as they were once the panel is closed; with a tempo and a note value typed into the panel it
reads the markings the line names (90 names Andante; 42 names Grave and Largo; 120 counted in eighth notes names Largo and Larghetto at
60 BPM in quarter notes; an emptied box names none and says what to enter; a panel opened again after Cancel names the project's own tempo,
not the draft). It reads which markings are named and the speed printed, not whether the Dutch is good Dutch: the unit tests pin the
sentences of all five languages, and no musician has read them. Not established: what a screen reader says (the line is linked to the tempo
box as what describes it (DescribedBy) and is not a live region, so nothing announces it when it changes; Narrator has not been used), Windows text sizes above
100% on the real setting, the bands against any table but the one they follow, and how any of it sounds, because it plays nothing.

The timing check on the Train page can save its last completed result as a CSV file. Export result (CSV) is the fourth command in the
check's row, after Finish, and docs/TIMING-CSV.md describes the file column by column. It is on only when a check has finished and been
analysed: not before the first check, not while a check runs, not after a check that was ended by a change (Stop, Pause or Start by hand
while it ran, a new project or the next song in Stage, a change of output, sleep, Tap to start, the audio failing) or by an error. Starting another check takes the
old result away from that moment, also when the new check never finishes; Stop and another page leave a finished result alone. It keeps one
check, in memory, until another check starts or the app closes, which is why a CSV history stays on the list of what is not delivered. The
check keeps a copy of what it measured, so the file names the project and the rhythm as they were when the check ended and holds the times
and the analysis of that run, whatever is edited, renamed, opened or removed afterwards; a lane that was followed and then removed keeps its
name in the file, and its place is the place it had in the project that played. The page says the same before anything is saved ("Ready to
export: the project, the rhythm, when it ended"). The file is UTF-8 with a byte order mark, CRLF, every field quoted, numbers with a decimal
point and every digit that is needed to read them back; a value that is not available (the median and the spread when nothing matched, the
coverage when no clicks were followed, a drift that needs four matches on clicks at different times) is empty and never 0; the names are
JSON strings, so no name can become a formula in a spreadsheet or add a record. The column names, the metadata and the main beat's name are
English and the same in every language and on every computer, by design: a script reads every file the same way, and the hint under the
command says that the column names and numbers are the same in every language; only the words on the page are translated. The file is
written atomically (a file that is open elsewhere, read-only or on a full disk stays as it was, and the error is shown). It holds when the
keyboard (or Hit) delivered each press to the app and where the app's audio stream stood, not when anything was heard: keyboard, system,
display, speaker and Bluetooth delays are not corrected for and nothing is recorded, so it is not a calibration. The unit tests hold the
writer to exact bytes, to a reader that knows only this format, to the .NET library's own CSV reader and to a decoder for the names, and the
Windows file-integrity job runs its file-sharing cases. The first-use verifier opens the page at the first-use size, 800x600, 1000x720,
400x720 and 320x900 and in Dutch and requires the four commands to be whole, inside the window and the card, at least as large as they are
(Begin 100 by 40 DIP, Hit 70 by 40, Finish 150 by 40, Export 100 by 40), not overlapping, with Export after Finish, and the hint to be whole,
at least 150 by 14 DIP, inside the card and below Export; on a run with audio it finishes a check and requires Export to come on, the ready
line to name the project and the rhythm, a new check, a Stop during a check and a renamed project to behave as above. Not established: the
save dialog (it is Windows' own and cannot be driven on a hosted runner, so no gate saves a file through it), that a spreadsheet program
reads the file (none has opened one), the enabled state on a hosted runner (it has no audio output, so no check can finish there; the
local private-desktop run with silent audio checks it), what a screen reader says (the hint is linked to the command as its description and
Narrator has not been used), a musician's reading of the Dutch, German, French and Spanish sentences (docs/design/glossary.md lists the
questions) and Windows text sizes above 100% on the real setting.

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
Play replaces its tempo and meter controls with an explanation (TAP then works only for Tap to start, above), and structural
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

[Preview 0.1.0-preview.11](https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.11)
answers two questions from using preview.10: the app opened on Settings, and the
beat-strength bar of the ready-made *Compound 6/8* showed two bars while the dial,
the tape and the status line counted six clicks. The owner was not available to
choose, so the first option was taken (read-only bars, or a label alone, are the
alternatives). The app now always opens on Play, and the strip has a bar for every
click (a bar whose unit does not divide it, or of more than 128 clicks, shows its beats alone), with the strengths of the clicks that split a beat saved in the new optional
`SubdivisionAccents`; a project that sets one cannot be opened by preview.10 or
earlier. Choosing a time signature from the picker sets the click unit to the written
beat (so every written beat is a click) and ends the strengths of the split clicks; the
split itself ends only when the click unit changes (4/4 to 3/4 keeps a split), and when
the new beat is exactly what the split clicks were the tempo is raised by the old
split, to at most 1000 BPM. Copilot reviewed the change five times; it raised five inline findings and listed
three more that were not inline threads, and all eight were fixed before the merge
(DELIVERY.md has the details). The hosted runner passes 550 App tests and the 240
Core tests that run (4 skip, as before), and the hosted first-use verifier checks the
start page, the six named bars, a tap on one split click and the picker conversion.
The local silent-audio gate (inaudible master gain, isolated data folder, private
desktop) passed on the final head's code, with live sound, tempo, meter and split-click
edits published without stopping. Three of four local gate runs on that code stopped at
8-second waits in Studio; replaying those steps showed Studio operations of 1.4 seconds to more than 8 on both preview.10 and preview.11 on that desktop, so they predate this change,
and the verifier's page waits now allow 30 seconds. The shipped x64 binary then passed the whole gate once with those waits (live sound, beat-strength, tempo, meter and split-click edits published without stopping, the counted stops passed, and one expected error in its app log); two runs with the 8-second waits had stopped at the Studio Undo wait. The released ZIP was dry-run on private-desktop copies of the owner's
saved state, one with its saved last page changed to Settings: both opened on Play with
six named strength bars and left the saved state byte-identical. It was installed on the
development PC without being started: preview.10 stays on disk, and the Start menu
shortcut now targets preview.11. No audio was played in any instance started for the
owner. This does not establish listening (the loudness of a Strong split click relative
to a beat comes from the renderer's constants, not from ears), why Studio is slow on the
private desktop, a native-speaking musician's review of the new Dutch, German, French and
Spanish text, Windows text sizes above 100%, or the owner's acceptance of editable split
clicks. When Windows activates the packaged app, keyboard focus starts on Settings
(initial focus is not managed yet), so its focus ring can show at launch.
