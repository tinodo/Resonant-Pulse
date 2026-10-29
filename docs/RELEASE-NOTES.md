# Resonant Pulse preview

Native WinUI 3/.NET 10 metronome and rhythm workstation under active development.

## What's new in preview.11

Three things from using preview.10.

- **Every click has a strength bar.** A beat can be split into smaller clicks (the
  ready-made *Compound 6/8*, "6/8 felt in two", counts the dotted quarter and splits
  each of its two beats in three), and the strength bar only had a bar for the two
  beats: the dial, the clicks and the status line said six, the bar said two, and the
  four small clicks could not be changed. The strip now has a bar for every click of
  the bar, so *Compound 6/8* shows six, numbered 1 to 6 as the dial and the status
  line count them. The clicks that split a beat are smaller and set a little apart
  with the beat they belong to. A tap sets their strength as it does a beat's
  (Soft, Medium, Strong or Silent), also while playing, and it is saved with the
  project. They are Soft until you change them, as they always were, so nothing
  sounds different until you do. *All normal* makes them Medium too and *Default
  accents* makes them Soft again.
  The screen reader names them "Beat 1, click 2 of 3, Soft", and **Subdivision** in
  Time signature ▸ Custom now says "clicks between beats" instead of "soft clicks".
- **Choosing a time signature no longer keeps an old split.** After a rhythm that
  split its beats, choosing 6/8 from the picker kept the old three-way split inside
  every eighth note. It now gives one click per written beat, as the picker says.
  When the new beat is exactly what the split clicks were, the same clicks keep
  coming at the same speed: the tempo rises by the old split (80 dotted quarters,
  each split in three, becomes 240 eighth notes). Otherwise the BPM number is kept,
  as it always was. A split is kept when the click unit stays the same, as from
  4/4 to 3/4.
- **The app opens on Play.** It reopened on the page you had left, which could be
  Settings.

A project in which you changed the strength of a split click cannot be opened by
preview.10 or earlier, which refuse a file with a setting they do not know. Projects
you did not change that way open everywhere.

Known issue: when Windows activates the packaged app, keyboard focus starts on
Settings (initial focus is not managed yet), so its focus ring can show at
launch.

The metronome is the large, centered primary surface on Play, with the tempo
inside the cyan/magenta dial. Click the number to type an exact tempo; the -,
**TAP** and + buttons answer immediately (hold to repeat; Ctrl steps by ten).
The time signature is a sheet-music picker (2/4, 3/4, 4/4, 6/8 and more) with
**Custom...** for the full builder, and every written beat is an audible click,
so 8/8 gives eight. Beat strengths are a row of bars (tall, medium, short or a
rest) that you click to change, one for every click of the bar, including the
clicks that split a beat. Timer, Project and Help are page tools in one
quiet strip under Settings; every panel has Cancel and Done side by side, the
same size, and Done applies. The
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
