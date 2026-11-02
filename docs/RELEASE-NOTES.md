# Resonant Pulse preview

Native WinUI 3/.NET 10 metronome and rhythm workstation under active development.

## What's new in preview.12

Five new things since preview.11, and the repairs below.

- **Tap to start.** A new switch next to TAP: switch it on, tap the beat on TAP (or press T), and after four steady taps the metronome starts at your tempo. Choose 2 to 16 taps in the Tempo panel. A tap that is off the beat starts the count again, a pause of more than four seconds begins a new count, Stop or Esc cancels, and it is never remembered when you close the app. With a practice program, the first step starts at your tempo and the other steps keep their proportions.
- **Help starts you off.** The Help panel on Play now begins with a five-step quick start, from the first launch to a steady click at your own tempo and time signature. It also says how Tap to start works and where it stops, what Pause, Stop and Mute do, which changes you hear at once, which from the next beat and which only after Stop and Start, where the keyboard shortcuts work, and, in a short musical reference with an example each, what *80 BPM with dotted quarter notes* means, how a time signature sets the click, what subdivision and swing do, and what each beat strength is. It is in English, Dutch, German, French and Spanish, scrolls inside the panel so Done is always on screen, and can be read with the keyboard (Tab to the text, then Page Down, End and Home).
- **The Tempo panel names the tempo.** Beside its title, the Tempo panel now shows the approximate Italian markings of the speed in the box (120 BPM: Moderato / Allegro). The names count quarter notes a minute whatever note the number counts, so 120 counted in eighth notes, the note of 6/8, reads *60 BPM in quarter notes: Largo / Larghetto*. It follows what you type before you choose Done, and it changes nothing about how the metronome plays. In English, Dutch, German, French and Spanish.
- **Train: export a finished timing check as CSV.** When a timing check has finished, **Export result (CSV)** saves it as a file for a spreadsheet: the totals, then each click and each press. It keeps the last finished check only, until you start another one or close the app. The column names and numbers in the file are the same in every language, and the fields are separated by commas: where Windows uses a semicolon as the list separator (the Dutch, German, French and Spanish settings), Excel may need the file imported with the comma as the delimiter instead of opened by double-clicking (not tried). The text for a drift that could not be worked out now says what it needs.
- **Library: separate tracks as WAV files in one ZIP.** In Library, **Export separate tracks (ZIP)** saves your project as one ZIP with a WAV file for the main beat and one for each lane, all starting at the same moment, so you can line them up in music software. It asks first how many tracks it will save and how much room they take, and you can cancel.
- **Repair: a tempo you typed is no longer overwritten.** A tempo typed in the Tempo panel is no longer lost when it is applied while a nudge (the - and + buttons, TAP, the wheel or the arrow keys on the dial) is still being applied: what was typed is the tempo the project, the dial and the audio end on, and closing the window right after keeps it.
- **Repair: a nudged tempo and something that replaces it.** A nudge of the tempo (the - and + buttons, TAP, the wheel or the arrow keys on the dial) that is still being applied when you open a project, start a new one, choose a ready-made rhythm or move to the next song of a setlist is no longer written into the new project, and the dial shows the new project's own tempo. A ready-made time signature that changes the click unit (6/8 from 4/4 with every beat split in two doubles the number) now comes after the nudges that are on their way, so the project, the dial and the audio end on the converted tempo; and when you choose a time signature and then another one in the Custom meter panel, the one you chose last is the one you get. A Tempo panel that you open again while an earlier Done is still being applied shows the tempo and the click unit that the project holds, and keeps what you typed in it.
- **Repair: a file window that cannot open.** If Windows cannot open the window for choosing a file when you save or open a project, the app now says so in plain words, and that your edits stay in the window, instead of reporting a problem with the audio output.
- **Repair: closing while a file is being written.** When you close the window while a file is being saved or exported (project, library copy, WAV, MIDI, separate tracks, timing result), the app now stops that write and removes its half-written temporary file; a file that was already there stays as it was. Closing waits up to 4 seconds for the write to stop; if it does not stop, the window closes anyway, the log names the file and a temporary file may remain.
- **Repair: a file that stays behind is named.** If a save or an export fails and its half-written temporary file cannot be removed either, the app now says which file stays, for Save, Save to library, Export WAV, Export MIDI and the timing result's CSV, as it already did for the separate tracks, and that your edits remain in the window.
- **Repair: the reason a close was refused stays on screen.** After a close was refused (the window stays open and says why), a file that was still being written, or that finishes while the close is being decided, no longer replaces that reason with *Saved*. A write that you start afterwards still tells you *Saved*.
- **Repair: keyboard focus after the tracks export.** If *Export separate tracks* had the keyboard focus when you pressed it, it gets the focus back after you cancel its question, after it saves and after an error, unless you moved the focus yourself.
- **Repair: where the keyboard starts.** When the app starts, the keyboard focus is on *Start* (on *Audio* while there is no audio output), not on *Settings*, so no focus ring shows on Settings when Windows activates the packaged app.
- **Repair: the Train page keeps its place.** After *Start check* and *Finish and show result* the page no longer jumps to the top: it stays where you were, the keyboard focus moves on to the command that takes over (*Hit*, then *Export result (CSV)*), and a finished result is brought into view.
- **Repair: narrow windows.** At the narrowest width the *Settings* tab shows its gear alone where its name does not fit (the name stays as the tooltip and for screen readers), *Stummschalten* is no longer broken in the middle of the word, and the Tempo panel shows its box for the number of taps in full at 800x600 (*Half tempo* and *Double tempo* now share its row).

Under the hood: the Windows App SDK that the app is built on is updated from 2.5.1 to 2.6.4 (Microsoft's reliability fixes for WinUI 3, the app lifecycle and notifications). Nothing should look different: the hosted start-up and packaged-start checks show the same controls and sizes as before and the local gates passed on it. Whether any of Microsoft's fixes affects this app was not tested.

A project in which you changed the strength of a split click (preview.11) cannot be opened by
preview.10 or earlier, which refuse a file with a setting they do not know. Projects
you did not change that way open everywhere.

Known issues: text in Dutch, German, French and Spanish has not been read by a native speaker; nothing was listened to (no hosted runner has an audio output, and the local checks play at an inaudible volume); nobody has opened a tracks ZIP in music software; the native save dialog was not driven by an automatic check (it cannot be on a runner or on the developer PC's private desktop), so a failed or cancelled real dialog, and a close while it is open, were tried only with a stand-in; a long export (WAV, MIDI, CSV) shows no notice while it writes (only the separate tracks do); a Custom meter panel that is opened while a ready-made time signature is still being applied shows the time signature that the project held then, and its Done writes what it shows (the wait is as long as a nudge takes); Windows text sizes above 100%, Narrator, the Contrast themes and ARM64 hardware were not tried.

The metronome is the large, centered primary surface on Play, with the tempo
inside the cyan/magenta dial. Click the number to type an exact tempo; the -,
**TAP** and + buttons answer immediately (hold to repeat; Ctrl steps by ten), and
the switch next to TAP turns on Tap to start.
The time signature is a sheet-music picker (2/4, 3/4, 4/4, 6/8 and more) with
**Custom...** for the full builder, and every written beat is an audible click,
so 8/8 gives eight. Beat strengths are a row of bars (tall, medium, short or a
rest) that you click to change, one for every click of the bar, including the
clicks that split a beat (a bar of more than 128 clicks, or one whose beat unit
does not divide it, shows its beats alone). Timer, Project and Help are page tools
in one quiet strip under Settings; every panel that changes something has Cancel
and Done side by side, the same size, and Done applies (Help only shows text and
has Done alone). The
window uses a modern custom title bar, and restoring your last session is silent.

A running metronome follows Play without stopping. Sound and beat strengths
switch at the next audio quantum; tempo, time signature, tempo unit,
subdivision, swing, groups and gap bars are spliced in at the next beat. The bar
carries on when the meter is unchanged and restarts at beat 1 when it changes,
and the tails of earlier clicks finish in their original sound. Count-in and
practice-timer changes apply at the next Start. When a project has a
Studio/Train program, tempo and meter follow that program and Play says so.
Ctrl+M remains available inside settings panels.

The practice timer is in Play's **Timer** panel. Set a duration in
seconds and choose Done; zero disables it. Stop and Start to use a changed
duration. Count-in and pauses do not consume practice time, and a shorter finite
program ends first. The final five milliseconds fade out. This limits live
playback, not export length. Manual timing reports also respect the stop boundary.

Auto-stop also supports written beats and complete bars, excluding count-in.
Use Play's **Timer** panel; zero disables an individual
limit. The earliest seconds/beats/bars/program end wins. Mixed meters, tempo
ramps, repeats and silence follow the authored program rather than counting
audible click events.

Download the ZIP for your architecture, extract all files, and read README.md.
The ZIP includes .NET and WinUI runtime components and is not a Store installer.
SHA-256 checksum files accompany each download.

This is a preview, not the completed full product or a hardware-certified timing
instrument. See the repository's docs/DELIVERY.md for current capability evidence,
docs/CAPABILITIES.md for explicit known gaps, and docs/RELEASING.md for
Store-publication prerequisites. No claimed Store availability,
ARM64 hardware result or acoustic accuracy follows from this release.
