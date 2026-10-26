# Resonant Pulse preview

Native WinUI 3/.NET 10 metronome and rhythm workstation under active development.

## What's new in preview.8

An alignment and clarity pass, driven by hands-on feedback:

- **Time signatures** are centred in their chip and in the picker.
- **Mute / Unmute** no longer says "click": the sound can be changed.
- **Timer, Project and Help** are now one strip under Settings, in the same
  full-contrast text and level with Settings' right edge (they looked disabled
  and unaligned before). It drops its words, then takes its own row, as the
  window narrows, and never covers the dial. "Practice" is now "Timer", so the
  Dutch and German Play page no longer has two items called "Oefenen" / "Üben".
- **Cancel and Done** are exactly the same size in every panel, and the audio
  output list and its refresh button share one height and line.
- **Every page**: dropdowns, number boxes and text boxes are one height, rows of
  buttons match, checkbox labels sit on their box, Train's dial no longer fills
  the page, Stage's readouts are centred and an empty Library no longer leaves a
  blank block.
- **Clicks out of step with the circle**: a saved project whose BPM unit does not
  divide its bar (8/8 counted in dotted halves, for example) now shows a notice
  with a one-click fix. The BPM number is kept, so the click rate is unchanged.
- The log now records what asked the window to close (the X button, Alt+F4,
  another program, or Windows ending the session) and how long ago the last key
  or mouse input was.

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
