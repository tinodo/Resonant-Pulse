# Pages and panels design (preview.10)

Scope: the Play panels (Timer, Project, Help) and the Settings, Library, Stage, Train and Studio pages. Play itself
is specified in `metronome-design.md` and is not changed here.

## 1. What was wrong (owner review of preview.9, plus an audit of every page)

- Fields stretch to the width of the window. A box for "100" or "1000" is 1400 px wide; a dropdown with three options
  is as wide as the page.
- "Apply edits" buttons sit in the middle of forms (Settings, every Studio lane and step, Stage notes). A change
  should simply take effect.
- Copy is written for developers: "graph-completed frames", "deterministic seed", "armed", "flushed output", "no silent
  musical simplification", and a Settings section titled "Capability status / remaining design scope".
- Every page is the same stack of full-width controls: no hierarchy, no grouping, walls of equal buttons (Library has
  eleven preset buttons under one heading), settings with no explanation ("Reduced motion", "Export bars without a
  program").
- Empty pages say nothing useful (Studio and Train show a form or a blank chart before anything exists).

## 2. Principles

1. **Size to the content.** A field is as wide as the longest thing it can hold, never as wide as the window.
2. **One surface, one way.** No duplicate controls and no Apply button: a change takes effect as soon as it is made.
   (The Play panels keep Cancel and Done, which the owner asked for: Done applies, Cancel discards.)
3. **Say what it does, in plain words.** Every setting states what it changes. Limits of the implementation and
   unfinished work are not interface text.
4. **One idea per card.** A card has a title, at most one sentence of purpose, then its controls. The card's main
   action is the visually primary one.
5. **Empty states teach the next step.**
6. **One skeleton.** Every page is built from the same parts, so a person who has learned one page has learned them all.

## 3. The kit

| Part | Rule |
|---|---|
| Page | Title (Bahnschrift 28) and one line of purpose, then cards. Content column at most 960 DIP wide, centred, 16 DIP between cards. Cards sit in two columns from 900 DIP up and in one below. |
| Card | The raised surface the app already uses for its instrument card: 1 px outline, 16 DIP corners, 20 x 16 padding. Title 16 semibold, optional description 13 muted, optional actions at the right of the title. |
| Setting row | Title and description on the left, the control on the right, a hairline between rows. Below 560 DIP the control moves under the text. |
| Field widths | Computed from what the field can hold, never from the window: a number box from its minimum, maximum, decimals and the Windows text size (a 3-digit tempo is ~130 DIP, the largest timer limit ~170); a dropdown from its longest option plus chrome (120 to 320); a name or search box fills its card up to 420. Percentages and levels are sliders. The 160/320 DIP caps are checked at 100 % text size; at larger text sizes the field grows with the text and is checked for clipping instead. |
| Captions | Above the field, 13 DIP regular, outside the control, linked to it for screen readers. A unit ("seconds") sits right of the box, or below it when the row is narrow. |
| Commands | Primary (accent) once per card; secondary (outlined); tertiary (text, icon + label); destructive is tertiary and asks first. Every command has an icon and a label. Control height 44 DIP, as everywhere else. |
| Live apply | See the commit contract below. No page has an Apply button. |

**Commit contract.** A field has a *draft* (what is typed) and a *committed* value (what the project holds).
- Dropdowns, switches and steppers commit at once.
- Text and number boxes validate while typing but write nothing. A valid draft commits once: on Enter, when focus leaves, or when the user navigates away or closes the window (those flush first, then wait for the recovery save). Esc restores the committed value. An invalid draft on blur, navigation or close reverts to the committed value and says why, next to the field.
- Sliders commit when the gesture ends (pointer release, key up); arrow-key steps within half a second are one gesture. A drag is never an undo step per tick.
- One undo step per completed gesture. Edits to the same field in one session coalesce.
- Every change is queued on the UI thread and published in order, so a second quick edit is never dropped or refused. Structural commands (add, remove, move) wait for the queue first. If a commit fails, the control returns to the committed value and shows the reason.
- Every setting says when it is heard: *now*, *next beat* or *next Start*. While the metronome plays, tempo, meter, sound and beat strength follow live; lane and program edits (Studio) are saved at once and heard the next time you press Start, and the page says so in one sentence.
- After a change that rebuilds part of a page, focus, caret and scroll position stay where they were; after a removal, focus moves to the neighbouring item or to the Add command.

## 4. Pages

### Settings
- **Appearance:** Language, Theme, Reduce motion (switch). Each row explains itself. Reduce motion: "The marker on the
  dial jumps from beat to beat instead of gliding, and the scrolling tape of upcoming clicks is hidden. It is also on
  whenever Windows has animation effects turned off." (The rule is: reduced if the switch is on **or** Windows says so.)
- **About:** name and version, the privacy promise in one sentence, and an honest line about timing (clicks are scheduled
  on the audio clock; the delay to the speakers is not measured).
- Removed: "Output device" paragraph (the Audio panel carries it), "Capability status / remaining design scope",
  "Export bars without a program" (moves to Library > Export, where it is used).

### Play panels
- **Timer:** three rows, "Time / Beats / Bars", each a narrow box with its unit; one sentence ("Whichever limit is
  reached first stops the metronome. 0 means no limit. The count-in does not count."); a plain-language summary of what
  is saved. Cancel and Done stay, side by side and equal. Heard: next Start.
- **Tempo and Time signature:** same kit (natural-width fields), same behaviour.
- **Project:** the name (up to 360) and two commands, *Save as file* and *Open file*.
- **Help:** six short entries (start, tempo, time signature, beat strength, live changes, shortcuts). No internals.

### Library
- **My projects:** search (up to 280), the list with name, *Open* (primary) and *Save current to library*; *New*,
  *Open file*, *Save as file* in the card header. States: loading ("Looking for projects..."), empty (what the library
  is and how to fill it), partial ("Showing the first 1000 projects"), read failures (named, with the rest still usable).
- **Export:** one sentence per format; *Length when the project has no program: [16] bars* ("this many bars are
  rendered"); *Export WAV*, *Export MIDI*; a failure names the file and the reason.
- **Ready-made rhythms:** tiles with a name, one plain sentence and a meta line computed from the preset (meter, layers,
  program steps). Choosing one asks first ("Replace the current project?") and says the current project stays in the undo
  history.

### Stage
- **Now playing** and **Up next** are separate, always labelled in words. Selecting a row only inspects it (its notes
  show); *Make next* queues it; *Next + count-in* starts the queued song. *Lock editing* blocks edits to the setlist and
  notes, never playback.
- Left: song title, tempo (large), meter, position, the dial, *Next + count-in* (primary).
- Right: the setlist (Now playing and Up next marked with text and icon), *Add current project*, up, down, remove; notes
  for the selected song (saved when focus leaves). *Lock editing* and *Always on top* are switches in the page header.
- Below 900 DIP the setlist moves under the display, but a compact *Up next* selector with *Next + count-in* stays
  above the fold.

### Train
- **Program:** current step, progress, the planned tempo as a compact chart, the steps as a table, *Edit in Studio*.
  Without a program: what programs are for, and *Build a program* (Studio) / *Ready-made rhythms* (Library).
- **Live view:** the dial and position.
- **Timing check** (was "Timing Lab"): states plainly what it does: it compares the moment each key press or Hit
  arrives with the moment each click was scheduled; it does not hear your instrument and does not measure speaker delay.
  Fields: *Rhythm to follow*, *Accept presses within* (ms), *Include beats that are not played (silent, muted or
  chance)*; *Start*, *Hit (F)*, *Finish*; the result as short lines.

### Studio
- Header: project name; Undo and Redo as icon buttons with their shortcuts; *Add lane* as a menu (empty lane, 3 in 4,
  3 over 4, Euclidean...). One sentence under the header: "Changes are saved as you make them. While the metronome plays
  they are heard the next time you press Start."
- **Lanes:** one card per lane: name, mute and solo, a *More* menu (rotate, invert, duplicate, remove). Three groups:
  *Rhythm* (the *Beat emphasis* strip, tap to change as on Play, then pulses, cycle, offset), *Sound* (sound, volume,
  pan) and *Variation* (chance). *Custom beat positions* is an expander for the per-event table, explained as exact
  placement within the cycle.
- **Euclidean** opens from the *Add lane* menu as a panel: hits, steps, rotation, cycle, a one-line explanation ("spreads
  5 hits as evenly as possible over 8 steps"), a live pattern preview (● ○ ● ● ○ ...), *Create* and *Cancel*.
- **Program:** one card per step: name, move up and down, remove; tempo from and to, bars, repeats, ramp, meter; "play N
  bars, then N silent" as one sentence-shaped row. *Add step*.
- **Advanced:** the random seed, with what it does ("the same seed gives the same random pattern every time").
- Empty state: what a lane is, with *Add lane* and *3 in 4*.

## 5. Copy rules

Words not allowed in interface text: onset, armed, deterministic, graph, flush, document (say *project*), frame,
quantum, spliced. Every new string exists in all five languages and keeps its placeholders. Numbers and units are
formatted with the language's culture. New terms are taken from a glossary built from the strings the app already
ships (maat, tel, maatsoort ...), so a word means one thing everywhere. The translations of new text are mine and have
not been reviewed by a native musician; the release notes say so and the glossary is kept for review.

## 6. Accessibility contract

- Every control has a stable logical AutomationId and a name that includes what it belongs to ("Lane 1, volume").
  A caption or unit outside a control is linked to it, so a screen reader never meets an unnamed edit box.
- Each cell of a beat-emphasis strip is a 44 x 44 DIP button named "Lane 1, beat 3, strong; press to change"; Left and
  Right move between cells, Up and Down change the strength, Space and Enter cycle it. A long strip scrolls inside its
  card instead of making the page scroll sideways.
- Focus, caret and scroll position survive any change that rebuilds part of a page.
- High contrast uses the system colours; every state (selected, now playing, up next, muted) is also stated in text or by
  an icon, never by colour alone; focus is always visible.
- Layout reflows from the measured width and the text size, not from fixed breakpoints alone, and never clips or scrolls
  sideways at 200 % text.

## 7. States

| Page | Loading | Empty | Partial or failed | Busy or locked |
|---|---|---|---|---|
| Library | "Looking for projects..." | what it is, how to fill it | first 1000 shown; unreadable files named | buttons disabled with the reason |
| Stage | none | no songs: what a setlist is, *Add current project* | a song that cannot start says why | locked: switch shown, setlist edits disabled and explained |
| Train | none | no program: what it is, where to build one | a timing run that was invalidated says so | a run in progress: *Start* disabled, *Finish* enabled |
| Studio | none | no lanes: *Add lane*, *3 in 4* | an edit that cannot be saved reverts and says why | Undo and Redo disabled when empty, with their labels |

Destructive actions (remove lane, step or song; replace the project) name the thing and the consequence, and say whether
Undo brings it back.

## 8. Verification

- Pixel captures of every page and panel at 520, 800, 1040 and 1400 DIP wide, in English, Dutch, German, French, Spanish
  and the light theme, and at a larger text size.
- UI Automation gates in the first-use verifier: on every page no number box is wider than 180 DIP and no dropdown wider
  than 320 (at 100 % text), no button called Apply, content never wider than its window, every page reachable, the
  expected control ids present; Settings changes take effect without a button. Row coordinates of the wrapping panel are
  not asserted (they depend on text size and language).
- Unit tests for the width rules, the wrap rule, the live-apply queue (latest value wins, one undo per gesture, barriers,
  failure reverts), and that every string exists in five languages with matching placeholders.

## 9. Delivery

The new kit is opt-in: pages move to it one at a time and the old helpers are deleted when nothing uses them. Four PRs,
each leaving main shippable, then one release: (1) kit, Play panels, Settings; (2) Library, Stage, Train; (3) Studio:
edit queue, lane cards, beat-emphasis strip; (4) Studio program steps, exact positions, polish.
