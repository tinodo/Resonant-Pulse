# Separate tracks (ZIP), schema version 1

**Export separate tracks (ZIP)** in the Library page's Export card saves one ZIP archive with a WAV file for the main beat, a WAV
file for each lane of the project, and a `manifest.json`. All the WAV files start at the same moment and are the same length, so
they line up when you put them at the same place in music software. This page says exactly what is in the archive and what it
depends on. The same archive is written in every language of the app, on every computer.

## What it is, and what it is not

- **The entries.** `main.wav`, then `lane-01.wav` to `lane-NN.wav` (one for each lane of the project, in the order of the lanes:
  `lane-01.wav` is the first lane, also when that lane is muted), then `manifest.json`. There are no folders and no other entries.
  The names are made from numbers and never from the name of a lane: a lane called `../../outside.wav`, `CON` or nothing at all is
  still `lane-02.wav` where it is the second lane, and its name is only text in the manifest (the tests try those names, and names
  with a line break, a quote and 256 characters). A project with no lane has `main.wav` and `manifest.json`.
- **What is in each WAV file.** `main.wav` holds the main beat (the clicks of the time signature, with their strengths), the clicks
  that split a beat (subdivision and swing) and the count-in. `lane-NN.wav` holds that lane's clicks, with the lane's sound, volume
  and pan, and nothing else: it is silent during the count-in.
- **Mute, Solo and silent bars work as they do in the mixed WAV.** A muted lane is silent. While any lane is on Solo, every other lane
  **and the main beat** are silent (the count-in still sounds in `main.wav`), so with a lane on Solo most of the files are silent. The
  bars that a program or the gap setting leaves silent are silent in every file.
- **A lane that does not sound still has its file**, silent and as long as the others: a lane that is muted, one that another lane's
  Solo leaves out, one with a chance of 0 and one without a click are all silent files, so the number of files is always the
  number of lanes plus one. `AudibleOnsetCount` in the manifest says how many clicks sound in a file (0 for a silent one).
- **The same moment, the same length.** Every file starts at frame 0 and is exactly `TotalFrames` frames long (the manifest says
  both). The length is the program's when the project has a program, and the export length that the Library page shows (the number
  of bars) when it has not, plus the count-in. The practice limits of the Timer panel (seconds, beats, bars) do not shorten it, as
  they do not shorten the exported WAV. A click that would ring on past the end is cut at the end, in every file.
- **Cut out of the finished project, not made again.** The tracks are taken out of the project after the clicks that sound were
  decided (the seeded chances and the gaps) and the count-in was placed, so they are the clicks of the mixed WAV, split over the
  files. Leaving lanes out before that step would change which clicks a chance lets through and would start the count-in again in
  every file.
- **Not a mix.** The files added together are not guaranteed to give the mixed WAV back. Each file has the master gain, is rounded to
  16 bits and is held at full scale on its own. Where nothing is at full scale the sum of *k* files is at most *k* + 1 halves of a
  step away from the mixed WAV, that is at most (*k* + 1) / 2 steps rounded down, a step being 1/32768 of full scale (the tests
  hold this to the sample, for a project with three lanes); where the mix would be held at full scale the sum of the files can be
  far more than that away from it (the tests hold that too). Nothing here is a statement about how it sounds in any program.

## What it holds when the project changes afterwards

The export takes a copy of the project when the command is pressed, and the question that is asked first describes that copy, so
what it says is what is saved:

- **The project is edited, renamed or replaced** while the question is open or while the file is written: the archive holds the project as
  it was when the command was pressed, and the lanes' names as they were.
- **The export length is changed** after the command was pressed: the archive has the length that was in the box when it was pressed.
- **The metronome plays** meanwhile (as it may while a WAV is exported): the export reads only its copy. It does not change what plays and
  does not follow its edits. Whether the rendering can make the audio stutter on a slow machine is not established.
- **The window is closed** while the file is written: the export stops, the temporary file is removed and a file that was already at the
  destination is as it was.

## Format

- **ZIP.** Each entry is compressed (Deflate, the default level of the .NET library), dated 1 January 1980 00:00 (the earliest a ZIP can
  say), so the same project gives the same bytes in one build of the app (another .NET runtime may compress the same data to other bytes)
  and the archive says nothing about when it was made. The tests read the archive with
  the .NET library, with a strict reader of their own that accepts no gap, no extra field, no comment and no ZIP64 structure and checks every
  CRC-32 (which the .NET library does not do when it reads), and the end-to-end run below with Python's `zipfile` module.
- **WAV.** The file the mixed WAV export writes, by the same code: a 44-byte header, PCM, two channels, 16 bits, `SampleRate` frames a second
  (48,000 in the app), samples rounded away from zero and held to the range of 16 bits. With no lane, `main.wav` is the mixed WAV, byte for byte
  (the existing tests of the mixed WAV pass unchanged, and a test compares the bytes).
- **`manifest.json`.** UTF-8 without a byte order mark, indented, with the property names of the project file (initial capitals). The words in it that
  describe how the files were made (`PeakProtection`, `Recombination`, `MainTrack`, `MixPolicy`, `TailPolicy`) are English in every language,
  like everything else in the file.

| Property | What it holds |
|---|---|
| `SchemaVersion` | 1 |
| `Format` | `PCM16 stereo` |
| `SampleRate` | Frames a second, the same in every file |
| `TotalFrames` | The length of every WAV file in frames |
| `CountInFrames` | The frames of the count-in at the start (0 without one); `lane-NN.wav` is silent for that long |
| `LaneCount` | The number of lanes in the project, which is the number of `lane-NN.wav` entries |
| `OriginFrame` | 0: every file starts here |
| `UncompressedAudioBytes` | The audio of all the files together, 4 bytes a frame for each of the `LaneCount` + 1 files, without the headers and the manifest: the number the question gives as the size |
| `BeatUnitQuarters`, `Seed`, `ProbabilityAlgorithm` | What the mixed WAV's own manifest holds (the note value the tempo counts, the seed of the chances and how they are decided) |
| `MasterGain` | The project's master gain, which every file has |
| `PeakProtection`, `Recombination`, `MainTrack`, `MixPolicy`, `Loop`, `TailPolicy` | What is said above, in a sentence each (`Loop` is `false`) |
| `Tracks` | One entry for each file, in the order of the archive: `File`, `Kind` (`main` or `lane`), `LaneIndex` (0 is the first lane; null for the main file), `Name` (the lane's name as it is in the project, possibly empty; null for the main file) and `AudibleOnsetCount` |
| `TempoSegments`, `SectionBoundaries`, `SilentRanges` | The tempo from frame to frame, the program's sections (the count-in is section -1) and the stretches of the project that are silent (silent bars), as in the mixed WAV's manifest |
| `AudibleOnsets` | Every click that sounds in any file: `Frame`, `LaneIndex` (-1 and -2 for the main file, 0 and up for a lane), `Accent`, `Gain`, `Pan` and `Sound` |

## Writing it

The file is written atomically: to a temporary file next to the destination, then moved over it. If anything goes wrong (the file is open in
another program, it is read-only, the disk is full, the export is cancelled or the window is closed) the file that was there stays as it was, the
temporary file is removed, and the page says that the tracks could not be saved and that a file that was already there has not been changed. One
export of tracks runs at a time, and the command is off while one does; a notice says that the tracks are being saved. A track longer than a WAV file
can be (4 GiB, about 6 hours at 48,000 Hz) is refused in words, naming the setting that can be changed, before a file is chosen. Everything about the
file is checked before anything is written.

## What it costs

@@MEASURED: table of the sizes, times and memory (Release build), the manifest at the largest size, and the question's 512 MiB line.

## What has not been tried

@@MEASURED: the end-to-end run and what it showed; what was not tried (save dialog, music software, ZIP64, listening, text sizes).
