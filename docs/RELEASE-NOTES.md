# Resonant Pulse preview

Native WinUI 3/.NET 10 metronome and rhythm workstation under active development.

## What's new in preview.9

Two refinements, driven by hands-on feedback:

- **The "clicks and circle are out of step" notice is one row.** It appears for a
  saved project whose BPM unit does not divide its bar (8/8 counted in dotted
  halves, for example) and offers a one-click fix that keeps the BPM number. The
  title and the fix now share a line (about 50 DIP high instead of 109), so the
  dial keeps more of its size. Preview.8's fix button was as tall as every other
  button, which made the notice move it to a second row at any width. On a
  narrow window the fix still wraps below the title.
- **The beat strip's scroll bar no longer covers the beat numbers, and 12/8 no
  longer needs one.** The strip sized its beats as if the gaps between additive
  groups did not exist, so 12/8 overflowed by about 20 DIP, cut off its last
  beat and showed a scroll bar although every beat fits. Now the gaps are taken
  out of the room first. A strip that really has to scroll (more beats than fit
  beside the meter and sound controls) keeps room under its numbers for the bar
  and shows the "More beats" cue.

The metronome is the large, centered primary surface on Play, with the tempo
inside the cyan/magenta dial. Click the number to type an exact tempo; the -,
**TAP** and + buttons answer immediately (hold to repeat; Ctrl steps by ten).
The time signature is a sheet-music picker (2/4, 3/4, 4/4, 6/8 and more) with
**Custom...** for the full builder, and every written beat is an audible click,
so 8/8 gives eight. Beat strengths are a row of bars (tall, medium, short or a
rest) that you click to change. Timer, Project and Help are page tools in one
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
seconds and choose Apply; zero disables it. Stop and Start to use a changed
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
