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
| Page | Title (Bahnschrift 28) and one line of purpose, then cards. Content column at most 960 DIP wide, centred, 16 DIP between cards. Cards sit in two columns from 900 DIP up and in one below. The column is *stretched* with a 960 maximum, never centred by content: a column as wide as its content would change its own width when a two-pane page changes layout, and never settle. |
| Card | The raised surface the app already uses for its instrument card: 1 px outline, 16 DIP corners, 20 x 16 padding. Title 16 semibold, optional description 13 muted, optional actions at the right of the title. |
| Setting row | Title and description on the left, the control on the right, a hairline between rows. Below 560 DIP the control moves under the text. |
| Field widths | Computed from what the field can hold, never from the window: a number box from its minimum, maximum, decimals and the Windows text size (a 3-digit tempo is ~130 DIP, the largest timer limit ~170); a dropdown from its longest option plus chrome (120 to 320); a name or search box fills its card up to 420. Percentages and levels are sliders. The 160/320 DIP caps are checked at 100 % text size; at larger text sizes the field grows with the text and is checked for clipping instead. A field is *hosted* at that width but never wider than the room it is offered (`FitWidth`): in a narrow window or at a large text size it gives way instead of being clipped, and a number box whose inline steppers no longer fit switches to compact ones (they appear over the box while it has focus). The widths follow the Windows text size while a page is open: a field keeps a rule (a function of the text size) and works its width out again when the size changes, without rebuilding the page, so a draft, the caret and the focus stay where they were. A drop-down's upper limit (320 DIP at 100 %) grows with the text size as well. |
| Captions | Above the field, 13 DIP regular, outside the control, linked to it for screen readers. A unit ("seconds") sits right of the box, or below it when the row is narrow; units in one group wrap together and stay closer to their own box than to the next. |
| Panel footer | Cancel and Done are one pair of equal-width buttons (120 DIP each). When the panel is narrower than the pair needs, both get narrower together; neither is clipped. |
| Commands | Primary (accent) once per card; secondary (outlined); tertiary (text, icon + label); destructive is tertiary and asks first. Every command has an icon and a label. Control height 44 DIP, as everywhere else. |
| Live apply | See the commit contract below. No page has an Apply button. |

**Commit contract.** A field has a *draft* (what is typed) and a *committed* value (what the project holds).
- Dropdowns, switches and steppers commit at once.
- Text and number boxes validate while typing but write nothing. A valid draft commits once: on Enter, when focus leaves, or when the user navigates away or closes the window (those flush first, then wait for the recovery save). Esc restores the committed value. An invalid draft on blur, navigation or close reverts to the committed value and says why, next to the field.
- Sliders commit when the gesture ends (pointer release, key up); arrow-key steps within half a second are one gesture. A drag is never an undo step per tick.
- One undo step per completed gesture. Edits to the same field in one session coalesce.
- A setting that belongs to the app (a preference, such as the export length) is applied at once, exactly like Theme or Language: the field shows it, the next use of it sees it, and remembering it for the next session is best effort. If that save fails the error is shown and the value stays, so the field, the setting and what is used next are never different values. The file is written from the values held when the write lock is taken, not when the save was asked for, so several overlapping saves always end with the latest value of every setting. Preferences are never rolled back: a rollback would leave a window in which another save could persist a value that is then withdrawn.
- A value that belongs to the *project* is published only after it is accepted, and every change is queued on the UI thread in order, so a second quick edit is never dropped or refused. Structural commands (add, remove, move) wait for the queue first. If a change is refused, the control returns to the value the project holds and shows the reason.
- A count is checked, never truncated: a number box *shows* 1.9 as 2 but reports 1.9, so a fractional bar count is refused with a message instead of saving 1.
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
  Empty and "no project matches" are said only about a library that was read successfully: while it is being looked at, or
  when it could not be read, the status line is all the page says, whatever is typed in Search.
- **Export:** one sentence per format; *Length when the project has no program: [16] bars* ("this many bars are
  rendered"); *Export WAV*, *Export MIDI*; a failure names the file and the reason.
- **Ready-made rhythms:** tiles with a name, one plain sentence and a meta line computed from the preset (meter, layers,
  program steps). Choosing one asks first ("Replace the current project?") and says the current project stays in the undo
  history.

### Stage
- **Now playing** is the loaded project; **Up next** is the song selected in the setlist, said in words ("Up next: …") and
  marked in the list. There is no separate inspect or queue step: selecting a row makes it next (its notes show) and never
  interrupts playback; *Next + count-in* starts it. Clearing the selection (Ctrl+click, an assistive technology) means no song
  is up next: the cue says so and the notes, Next, up, down and remove are off. *Lock editing* blocks edits to the setlist and
  notes, never playback.
- Left: project name, the tempo large (shown once: the dial's own numeral is hidden on this page), meter, position, the dial,
  *Next + count-in* (primary).
- Right: the setlist (the next song marked with text), *Add current project*, up, down, remove; notes for the selected
  song (saved when focus leaves, Esc puts back what was saved). Notes are saved in the order they were typed, none is dropped
  because another save is running, and selecting, moving, removing or starting a song (and closing the window) waits for them.
  Each request is told apart by its own number (not by its text) and belongs to the project it was typed in, so a later save
  of the same words never releases an earlier one and a draft never reaches a replaced project. A save that fails keeps the
  draft in the box and the song selected, and says why; the page is not rebuilt meanwhile, so what is typed next stays. A
  draft that failed and whose page is gone is asked for again when the window closes or the project is replaced. The setlist
  itself is switched off while songs are being added, moved or removed (and only then: switching it off for every operation
  would take the keyboard focus away from it). Replacing the project (Ctrl+O, New, a ready-made rhythm) is a Stage operation
  too: from before the notes are saved until the new project is shown the notes box and the setlist are switched off, so
  nothing typed can fall between the last save and the replacement, and they work again if the replacement fails. *Lock editing* and *Always on top* are
  switches on one line in the page header, each caption wrapping inside the room it is given; what the lock does is its
  tooltip, and the sentence is shown in the setlist card only while the lock is on, where it explains why the setlist
  commands are greyed out.
- Closing the window commits the field being typed in, then switches every control that edits the project or the settings
  off (the page area, the audio options and any open panel; a question that is open is answered No, and one that a request
  still loading would ask now is not asked at all), switches Start off too
  (Stop and Mute stay available, so nothing new starts to play while the last save is made), waits for the saves,
  and switches them on again if closing is refused, so nothing typed or confirmed during the last save can be missed by
  it. Undo and Redo in Studio wait for the notes saves first, so they act on the latest completed edit; when the song that
  was up next is gone after an undo, nothing is up next and the commands that need a song are off.
- Below 900 DIP the setlist moves under the display.

### Train
- **Program:** current step, progress (the time played of the whole program), the planned tempo as a compact chart, the steps as a table,
  *Edit in Studio*. Without a program: what programs are for, and *Build a program* (Studio) / *Ready-made rhythms* (Library).
  A change made in Studio while the metronome plays is heard from the next Start, so the status, progress and chart follow
  the program that plays (even one that was removed from the project, until Stop) and a note says that the steps listed are
  the changed program (or that the program was removed); the card's content is swapped in place when the run ends, by Stop
  or by itself (a completed run keeps its revision until Stop, but nothing plays any more), so the timing check beside it
  keeps its controls and the choices made in them.
- **Live view:** the dial and position.
- **Timing check** (was "Timing Lab"): states plainly what it does: it compares the moment each key press or Hit
  arrives with the moment each click was scheduled; it does not hear your instrument and does not measure speaker delay.
  Fields: *Rhythm to follow*, *Accept presses within* (ms), *Include beats that are not played (silent, muted or
  chance)*; *Start*, *Hit (F)*, *Finish*; the result as short lines.

### Studio
- Header: one sentence ("Editing “name”. Changes are saved as you make them and heard the next time you press Start.");
  Undo and Redo as icon buttons (there are no keyboard shortcuts for them yet); *Add lane* as a menu: 4 clicks over 4
  beats, 3 over 4 (polyrhythm), 3 over 3 (polymeter) and *Euclidean...*. While there are no lanes the one *Add lane*
  button sits in the empty-state card instead of the header.
- **Lanes:** one card per lane: name, mute and solo, a *More* menu (rotate, invert, duplicate, remove with a
  confirmation). Three groups: *Rhythm* (the *Beat strength* strip, tap to change as on Play, then clicks per cycle,
  cycle length, start offset), *Sound* (click sound, volume, pan) and *Variation* (chance). *Custom beat positions* is an
  expander for the per-event table, explained as exact placement within the cycle.
- **Edits apply as they are made.** Nothing rebuilds the page, so focus, caret and scroll stay; a slider commits when the
  drag or the held key ends. Undo steps: one for each field per burst (the same field again within 2 seconds is the same
  step), one for each add, remove, duplicate, rotate and invert. While the project is locked (Stage) every card is
  disabled and the page says why, naming the page and the switch as the language does. Undo and Redo rebuild the page, so the
  focus is put back on the command that was used (on the other one when it has nothing left to do), and a beat that was
  tapped while the number of clicks was being lowered is ignored, not reported as an error.
- **Rules the cards rely on:** fewer clicks cut the pattern to the new count (the project file refuses a pattern longer
  than its clicks); *Rotate* moves a lane later, for a pattern and for exact positions alike; a copy goes last, so the
  lanes before it keep their place and with them the random pattern behind *Chance*; *Solo* silences the main beat and
  every lane that is not solo, and its help says so, and that a muted lane does not count as *Solo* (soloing a muted lane
  changes nothing for the others); at most 8
  lanes. The cycle and offset boxes keep room for three decimals (about 200 DIP) and show only the digits a value needs.
- **Euclidean** opens from the *Add lane* menu as a panel: hits, steps, rotation, cycle, a one-line explanation ("spreads
  5 hits as evenly as possible over 8 steps"), a live pattern preview (● ○ ● ● ○ ...; a screen reader hears it as part of
  the preview's name), *Create* and *Cancel*. A blank or fractional box says to enter whole numbers; more hits than steps
  says that.
- **Program:** one card per step: name, move up and down, remove; tempo from and to, bars, repeats, ramp, meter; "play N
  bars, then N silent" as one sentence-shaped row. *Add step*. (Until step 4 of the delivery below, the older editor for
  each step sits inside one card.)
- **Advanced:** the random seed, with what it does ("the same seed gives the same random pattern every time").
- Empty state: what a lane is, with *Add lane*, which offers the 3 in 4 pattern among its kinds.

## 5. Copy rules

Words not allowed in interface text: onset, armed, deterministic, graph, flush, document (say *project*), frame,
quantum, spliced. Every new string exists in all five languages and keeps its placeholders. Numbers and units are
formatted with the language's culture. New terms are taken from a glossary built from the strings the app already
ships (maat, tel, maatsoort ...), so a word means one thing everywhere. The translations of new text are mine and have
not been reviewed by a native musician; the release notes say so and the glossary is kept for review.

## 6. Accessibility contract

- Every control has a stable logical AutomationId and a name that includes what it belongs to ("Lane 1, volume").
  A caption or unit outside a control is linked to it, so a screen reader never meets an unnamed edit box.
- Each beat of a strength strip is a button named "Lane 1, beat 3, Strong"; the strip is one tab stop, Left, Right, Home
  and End move between beats, Up and Down change the strength (they stop at the ends), Space and Enter step through the
  strengths and wrap around. A long strip scrolls inside its card instead of making the page scroll sideways.
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
| Studio | none | no lanes: what a lane is, *Add lane* | an edit that cannot be saved reverts and says why | Undo and Redo disabled when empty, with their labels; locked: every card disabled and explained |

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
