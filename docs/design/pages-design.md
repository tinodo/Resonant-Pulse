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
- A value that belongs to the *project* is published only after it is accepted, and every change is queued on the UI thread in order, so a second quick edit is never dropped or refused. Structural commands (add, remove, move) wait for the queue first, and while one runs the fields of the page are off with the other commands (an edit made then would be queued with the old page and refused). A page that is built while edits are still being applied (Studio, after a quick trip to another page) stays off until they are in, its fields and its Undo, Redo and Add lane commands included, and is then built again from the project as it is, so it never changes under someone who has started to use it. A name box that is taken away with its page after the project changed under it (a step moved, a lane removed) does not commit: losing the focus because it was taken away is not the person leaving the box, and what it holds was written for whatever was at its place before. A fresh Start waits for the edits asked for before it (a tempo step that is still on its way included), so what plays includes them; Stop stays available while it waits and cancels the start at once, as do a change of output, a power transition and a replaced project. If a change is refused, the control returns to the value the project holds, or to the value of an earlier change that still waits in the queue (a blank name is refused at once, before the name typed a moment ago has been applied), and shows the reason.
- A count is checked, never truncated: a number box *shows* 1.9 as 2 but reports 1.9, so a fractional bar count is refused with a message instead of saving 1.
- Every setting says when it is heard: *now*, *next beat* or *after Stop, then Start*. While the metronome plays, tempo, meter, sound and beat strength follow live; lane and program edits (Studio) are saved at once and heard after Stop and Start (a Start that resumes a pause keeps the revision it paused on), and the page says so in one sentence.
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
- **Tempo and Time signature:** same kit (natural-width fields), same behaviour. The Tempo panel also holds *Taps to start*
  (a narrow box with its unit, 2 to 16, and one line that says it counts the next time *Tap to start* is switched on). It is a
  preference of the app, not of the project: Cancel discards it with the rest of the panel, Done keeps it and saves it like
  every other preference (best effort; if the save fails the error is shown and the value stays). Done writes only what was
  changed in the panel, so changing the number alone never puts the tempo the panel showed when it opened over a newer one
  (the tempo a *Tap to start* has just saved). With a program there is no
  tempo readout to open the panel from, so the number cannot be changed until the program is removed.
- **Tempo guide (the Tempo panel's title row):** at the right of the title, one line of muted 12 pt text names the approximate
  Italian markings of the speed the panel holds ("Approximate markings: Moderato / Allegro"). It is a reading, not a control:
  nothing to click, no state of its own, nothing it works out is written. It sits in the row the title already takes, not in a
  row of its own: the panel is only as tall as the room between the dial's readout and the bottom of the window, so a line
  under the note value (the first version) pushed the box for *Taps to start* partly out of view at 400x720, and a row on Play
  would cost the dial size (the dial's own caption is 18 pt inside a viewbox, too small to read at 400x720 and hidden on Stage).
  It wraps in its own column when the panel is narrow (two lines at 400x720 and 320x900) and a no-break space keeps a pair of
  names, and a number with BPM, on one line. The markings are about a speed, so they count quarter notes a minute whatever note
  value the number counts: a tempo counted in quarter notes only names them (the note value beside the box says what the number
  counts), any other note value also says the speed in quarter notes ("Approximate markings at 60 BPM in quarter notes: Largo /
  Larghetto" for 120 counted in eighth notes, the note of 6/8). It follows the number when the box takes it (Enter, leaving the
  box, a stepper, Half or Double tempo) and the note value as they change, before Done; Cancel discards the draft with the
  guide. An emptied box, or a tempo the project would refuse, has no marking and the line says which tempos it can name. The
  Italian terms are the same in every language and are code, not entries of the translated text; only the sentences are
  translated (docs/design/glossary.md).
- **Project:** the name (up to 360) and two commands, *Save as file* and *Open file*.
- **Help:** read-only, so Done alone (there is nothing to cancel). It grows the panel Play already had instead of adding a
  page or a control to Play: the dial keeps its size, and a beginner finds the quick start where they begin. It opens at its
  first line, and its text scrolls inside the panel with Done always on screen, down to 320x900. In reading order:
  *Quick start* (five steps: the output, Start, the tempo, the time signature, the strength of a beat), *Tap to start* (how it
  works, then its limits), *While it plays* (Pause, Stop and Mute; what is heard at once, what from the next beat and what
  waits for Stop and Start; a program sets the tempo and time signature itself), *Keyboard shortcuts* (T for a tap, and where
  the keys work) and *Musical reference* (BPM and the note value, the time signature and the click, subdivision and swing, the
  strength of a beat), each with one example in a quiet box. No internals: a sentence names a control with the label it has in
  the language shown, taken from the same string as the control, so a renamed control renames the Help; the few names that
  have to be written into a sentence (the Tempo panel, the Subdivision and Swing fields) are pinned to their labels by a test.
  Tests check the claims and the examples against the engine (HelpFactsTests) and reject the words of the engine in all five
  languages (HelpContentTests).

### Library
- **My projects:** search (up to 280), the list with name, *Open* (primary) and *Save current to library*; *New*,
  *Open file*, *Save as file* in the card header. States: loading ("Looking for projects..."), empty (what the library
  is and how to fill it), partial ("Showing the first 1000 projects"), read failures (named, with the rest still usable).
  Empty and "no project matches" are said only about a library that was read successfully: while it is being looked at, or
  when it could not be read, the status line is all the page says, whatever is typed in Search.
- **Export:** one sentence per format; *Length when the project has no program: [16] bars* ("this many bars are
  rendered"); *Export WAV*, *Export MIDI* and *Export separate tracks (ZIP)* as one row of commands (the third wraps under the other
  two where the card is too narrow for three), with a hint under the row that says what the ZIP holds (a WAV file for the main beat
  and one for each lane, all starting at the same moment, to line up in music software); a failure of the WAV or the MIDI export
  names the file and the reason, and one of the tracks says what to check (the location, the free space, another program that has
  the file open) and that a file that was already there is unchanged, when the file could not be written.
  The tracks command asks first (how many tracks, how long each is, what they take unzipped, where the count-in is and what a muted
  lane gives), because the ZIP can be large, and only then chooses the file; it is off from the press until the end, with a notice
  while the file is written (*Saving the tracks...*), and closing the window stops it once the file is being written (it waits for
  that for at most 4 seconds, after the wait for the save; a question that is open when closing begins is answered No). docs/WAV-STEMS.md is the specification.
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
  chance)*; *Start*, *Hit (F)*, *Finish*; the result as short lines. *Export result (CSV)* is the fourth command in the same
  row, after *Finish*: it is on only when a check has finished (not before the first, not while one runs, not after one was
  ended by a change or an error), and a line under the result says what an export would hold (the project as it was called when
  the check began, the rhythm and when the check ended), as the check had them, whatever was edited since. A hint under the commands says what the file is, how
  long the result stays and that its column names and numbers are the same in every language (the file is English and
  invariant by design, for the scripts and spreadsheets that read it; docs/TIMING-CSV.md). It is a command of the check, not
  of the page header and not a row of its own: a command that exists only for a finished result belongs with the other commands
  of the check (the Train page's own rule is commands that are on only when they make sense), it has an icon and a label and is
  not accented (the card's one accent is *Start*), and it costs the page no extra row where the row has room. It sits below the
  first screen with the whole card, so no primary control gave up its place.

### Studio
- Header: one sentence ("Editing “name”. Changes are saved as you make them. Press Stop, then Start, to hear them.");
  Undo and Redo as icon buttons (there are no keyboard shortcuts for them yet); *Add lane* as a menu: 4 clicks over 4
  beats, 3 over 4 (polyrhythm), 3 over 3 (polymeter) and *Euclidean...*. While there are no lanes the one *Add lane*
  button sits in the empty-state card instead of the header.
- **Lanes:** one card per lane: name, mute and solo, a *More* menu (rotate, invert, duplicate, remove with a
  confirmation). Three groups: *Rhythm* (the *Beat strength* strip, tap to change as on Play, then clicks per cycle,
  cycle length, start offset), *Sound* (click sound, volume, pan) and *Variation* (chance). *Custom beat positions* is an
  expander for exact placement within the cycle (see below).
- **Edits apply as they are made.** Nothing rebuilds the page, so focus, caret and scroll stay; a slider commits when the
  drag or the held key ends. Undo steps: one for each field per burst (the same field again within 2 seconds, measured from
  the moment each change was made, is the same step), one for each add, remove, duplicate, rotate and invert. While one of
  those runs (Remove asks first), the fields of every lane are off, and the *More* menu is not; they are on again when it ends. While the project is locked (Stage) every card is
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
- **Program:** a heading (what a program is, with *Add step*), then one card per step: its name with *Move up*, *Move down*
  and a *More* menu (copy, remove after a question), then *Tempo* (steady, or from one tempo to another: the choice decides
  which boxes show, so a steady step has one tempo box and a ramp has a start and an end tempo, with one sentence saying
  what the two kinds of ramp mean), *Length* (bars, repeats), *Time signature* (beats and beat note; both are off, with the
  reason, while the project groups its beats, because the project then insists on its own time signature; the card offers
  *Clear grouping*, which takes the grouping off the project and keeps the program, so each step can have its own time
  signature again, and Undo brings the grouping back: Play offers no time signature while there is a program, so the way
  out is on the card) and *Practice
  aids* (a switch for leaving some bars silent, which shows "Play [2] bars, then [2] bars silent"; the sentence is cut at
  its places, so a language that puts the numbers in another order gets its boxes in that order). With no steps the heading
  gives way to a card that says what a program is and offers *Add step*. Every field applies as it is changed, with one undo
  step for each field per burst; adding, moving, copying and removing change which cards are on the page, so it is built
  again and the focus goes to the new step's name, to the command that was being used on the step that moved (the other one
  when it has reached the end), to the copy, or to the neighbour of the step removed (to *Add step* when none is left). A
  program the project would refuse (more than 100,000 bars in all, more than 4,096 steps once repeats are counted, more
  than 1,024 steps) is explained in words, with what to change, and the box goes back. Switching to a steady tempo makes
  the end tempo the start tempo, which the project insists on; choosing a ramp keeps both, so the step sounds the same until
  the end tempo is changed. The steady box and the start box are one tempo shown in two places, and only one is on screen:
  what is typed in either is one submission, a value that waits for the project is shown in both, and once it has been
  answered (applied, or refused with the reason) both show what the project holds, or a newer value that is still on its
  way. Which boxes show follows the same rule: the newest choice of the tempo curve (or of the silent-bars switch) that the project has not
  answered yet decides, so of several quick changes the first answers do not make the boxes come and go. The two silent-bars numbers always change together (both above zero, or both zero). Choosing a
  time signature with grouped beats on Play while a step has another one is explained in words (every step must use it), not
  as a problem with the groups.
- **Custom beat positions** (inside a lane): what exact positions are ("place each click exactly, measured in quarter notes
  from the start of the cycle; use fractions such as 1/3 for triplets"). A lane that plays a pattern offers *Convert the
  pattern to positions* (the same clicks, now movable one by one; Undo brings the pattern back). A lane with positions lists
  them, one row each: "Position 2 · 0.75 · Medium", with "tied" and so on when it applies, so a closed list can be read. A
  row opens to *Position* (a whole number, a fraction or a decimal, in the language's own decimal format; kept exact, never
  rounded to a grid), *Strength*, *Volume*, *Chance* and *Tied*, which apply as they are changed, and *Remove* after a
  question. A position outside the cycle, one that another position already has, one more precise than the project keeps (a
  fraction of more than 256 bits, such as 1e-100), and text that is no position are refused in words and the box goes back.
  A position moved past another puts the list in order again. The rows are not built again: each stays on screen, so a draft
  in a box, the focus and whether a row is open stay where they were, and only the numbers change ("Position 3" is
  "Position 2" once a position has gone before it). The row that has the focus does not move; the others move around it, so a
  person who typed a position and pressed Tab is still on the strength, one who was typing in another row is still typing,
  and a slider keeps its focus. A row is told apart by where its position is, not by its place in the list, when an edit is
  applied: edits wait in a queue, so a strength changed right after a position was moved (or after it was moved twice) still
  changes that position. The Undo step an edit belongs to is told apart by the row as well, not by where its position is, so
  moving a position twice in a short while is one step back. A position typed as the place it already has (0.75, then 3/4)
  changes nothing, however many such edits wait, and the answer to an earlier edit never replaces what has been typed in the
  box since. What the project holds is shown even when the recovery file could not be written afterwards (the error is still
  reported): a position that moved is in its new place, an added one is on the list and a removed one is gone. *Add
  position* puts a new one in the middle of the longest gap, open, with its box selected; when a lane is full (256) the
  button is off and its tooltip says why. The fields of a row are built when it is opened, so a lane with many positions costs nothing until one is looked at. Removing the last
  position leaves one silent click, so the lane stays quiet instead of starting to play an even pattern.
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
| Studio | none | no lanes: what a lane is, *Add lane*; no steps: what a program is, *Add step* | an edit that cannot be saved reverts and says why; a position, a program or a name the project would refuse is explained in words | Undo and Redo disabled when empty, with their labels; locked: every card disabled and explained |

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
