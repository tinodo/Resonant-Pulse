# Resonant Pulse preview

Native WinUI 3/.NET 10 metronome and rhythm workstation under active development.

## What's new in preview.10

A page-design round, driven by an owner review of preview.9. Timer, Project,
Help and Settings had fields as wide as the window, an "Apply edits" button in
the middle of a form and settings nobody could explain, and Library, Stage,
Train and Studio had had little thought. Settings, Library, Stage, Train and
Studio, and the panels on Play, now follow one design.

- **One design for pages and panels.** A titled page of cards; fields as wide
  as what they hold (a tempo box fits three digits, a drop-down its longest
  option); plain-language text; an empty state that says what to do next; and no
  Apply button, because a change takes effect as you make it. The Play panels
  keep **Cancel** and **Done**, where Done applies. A field gives way instead of
  being clipped in a narrow window or at a larger Windows text size, and a
  change the project refuses goes back to the value it holds and says why.
- **Settings.** Language and Theme are sized to their options. **Reduce motion**
  is a switch that says what it does: the marker on the circle jumps from beat
  to beat instead of gliding, and the scrolling tape of upcoming clicks is
  hidden. It is also on whenever Windows has animation effects turned off. An
  About card shows the version.
- **Export length** moved to Library, next to the export buttons. It is the
  number of bars written to a WAV or MIDI file when the project has no program
  (a program decides its own length). It takes whole numbers and applies at
  once.
- **Timer, Project, Help and the tempo and time-signature panels** use the same
  fields: three aligned boxes with their units for the timer, a capped name box,
  three short Help sections with the shortcuts as keycaps.
- **Library** has *My projects* (search, each project's name and date, Open
  only once one is chosen), *Export* (a sentence for each format) and
  *Ready-made rhythms* (a name, one plain sentence, its meter and tempo).
- **Stage** shows *Now playing* (the tempo once, large) beside the *Setlist*.
  The selected song is *Up next*, said in words, and selecting never interrupts
  playback. **Next + count-in** is on the first screen, **Lock editing** and
  **Always on top** are switches, and notes save when focus leaves the box.
- **Train** has the practice program (status, progress, the planned tempo as a
  chart, the steps as readable rows) and a timing check whose Start, Hit and
  Finish are on only when they make sense.
- **Studio.** Each lane is a card: its name, **Mute**, **Solo** and **More**
  (rotate, invert, copy, remove after a question), then its rhythm (the
  strength of each beat as a strip you tap, clicks per cycle, cycle length,
  start offset), sound and chance. **Add lane** is one menu: 4 over 4, 3 over 4
  (polyrhythm), 3 over 3 (polymeter) and Euclidean, with a preview of the
  pattern. The practice program is one card per step: its tempo curve (steady
  or a ramp) decides which tempo boxes show, beside bars, repeats, the step's
  own time signature and optional silent bars, and a program the project would
  refuse is explained in words. *Custom beat positions* are rows that open to
  place a click exactly (a whole number, a fraction such as 1/3 or a decimal,
  kept exact) with its strength, volume, chance and tie; a pattern lane
  converts to positions in one step, and Undo brings the pattern back. Every
  field applies as it is changed. What you change in Studio while the
  metronome plays is saved at once and heard after Stop and Start.
- **Edits are applied in the order you make them,** even when you make them
  quickly. A Start waits for the edits asked for before it, and Stop cancels a
  Start that is still waiting.
- **Fixed on the way.** A row of commands could push its last button out of
  sight in Dutch, because layout rounding disagreed between measuring and
  arranging; rows are now arranged as they were measured.
- **Languages.** All of the new text is in English, Dutch, German, French and
  Spanish. The new and rewritten text in the last four has not been reviewed by
  a native-speaking musician; docs/design/glossary.md lists the musical terms to
  check first.

Known issue: when Windows activates the packaged app, keyboard focus starts on
Settings (initial focus is not managed yet), so its focus ring can show at
launch.

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
