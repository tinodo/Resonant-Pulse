# Separate tracks (ZIP), schema version 1

**Export separate tracks (ZIP)** in the Library page's Export card saves one ZIP archive with a WAV file for the main beat, a WAV
file for each lane of the project, and a `manifest.json`. All the WAV files start at the same moment and are the same length, so
they line up when you put them at the same place in music software. This page says exactly what is in the archive and what it
depends on. The same entries, the same audio and the same manifest are written in every language of the app. On one computer the bytes of the
archive itself are the same for one build of the app (see Format); the samples were not compared between computers, and the clicks are computed with
floating point functions that the runtime does not promise to the last bit across processors and operating systems.

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
  number of lanes plus one. `AudibleOnsetCount` in the manifest says how many clicks the schedule lets into a file: 0 for a file that is
  silent because nothing is scheduled in it (every case above). A click whose gain is 0 (a master gain, a lane volume or an event gain of
  0) is not left out by the schedule: it is counted and listed, and renders as silence (a test holds this).
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
  destination is as it was. Closing waits for the export to stop for at most 4 seconds, after the up to 4 seconds it already allows for saving
  the project (on the largest project here the process exited 0.6 seconds after the close was asked for); a close that is refused, because the
  project could not be saved, leaves the export running; and if the export had not stopped when the 4 seconds passed the window would close
  without it (the log says so), which was not seen and could leave a temporary file beside the destination. Closed earlier, while the plan is
  compiled or the question is open, nothing is written. Closing answers an open question No. When the close is refused (the project could
  not be saved) while the plan is still being compiled, that export is marked void and the page looks at the mark before it asks, so it ends without
  a question and the command is on again; a unit test holds the order (a compile that is held, a refused close, no question). A close that is
  refused while the save dialog is open does not stop the export, because the person has answered Continue.

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
| `UncompressedAudioBytes` | The audio of all the files together, 4 bytes a frame for each of the `LaneCount` + 1 files, without the headers (44 bytes a file) and without the manifest, which comes on top (11,748 bytes for the first project; 149 MB, about 4% of the audio, for the dense 1,024-bar project measured under *What it costs*): the number the question gives as the size |
| `BeatUnitQuarters`, `Seed`, `ProbabilityAlgorithm` | What the mixed WAV's own manifest holds (the note value the tempo counts, the seed of the chances and how they are decided) |
| `MasterGain` | The project's master gain, which every file has |
| `PeakProtection`, `Recombination`, `MainTrack`, `MixPolicy`, `Loop`, `TailPolicy` | What is said above, in a sentence each (`Loop` is `false`) |
| `Tracks` | One entry for each file, in the order of the archive: `File`, `Kind` (`main` or `lane`), `LaneIndex` (0 is the first lane; null for the main file), `Name` (the lane's name as it is in the project, possibly empty; null for the main file) and `AudibleOnsetCount` |
| `TempoSegments`, `SectionBoundaries`, `SilentRanges` | The tempo from frame to frame, the program's sections (the count-in is section -1) and the stretches of the project that are silent (silent bars), as in the mixed WAV's manifest |
| `AudibleOnsets` | Every click that the schedule lets into any file (one that a Mute, a Solo, a chance or a gap leaves out is not listed; one whose gain is 0 is listed and renders as silence): `Frame`, `LaneIndex` (-1 and -2 for the main file, 0 and up for a lane), `Accent`, `Gain`, `Pan` and `Sound` |

## Writing it

The file is written atomically: to a temporary file next to the destination, then moved over it. If anything goes wrong (the file is open in
another program, it is read-only, the disk is full, the export is cancelled or the window is closed) the file that was there stays as it was and
the temporary file is removed. When the temporary file cannot be removed either, Core throws a `TemporaryFileCleanupException` (an `IOException`
with the path of the file that stays, whose inner exception holds both causes) and the page says so and names the file: the tracks could not be
saved, a temporary file could not be removed, a file that was already there has not been changed, and the file can be deleted (a window that
closes before the export has stopped is the case described above). What the page says otherwise depends on what went wrong: for a file that is
open in another program, read-only or on a full
disk (an input/output or access error) it says that the tracks could not be saved and that a file that was already there has not been changed; for
another failure (a destination that is not a local path, a project that cannot be compiled) it shows the app's ordinary error for it; and when the
window is closed nothing is shown. One export of tracks runs at a time, and the command is off from the press to the end, also while the question
is open and the plan is compiled; a notice says that the tracks are being saved only while the file is written (after the save dialog), not
before. If another notice replaces it while the file is written (Saved, from Save to library, fades after 6 seconds), it comes back when that
one has faded. It does not replace a warning or an error that is open when the file begins to be written (a failure that still needs
attention; the command stays off meanwhile), and when the person dismisses a warning or an error while the file is still being written it comes
back. A track longer than a WAV file
can be (4 GiB, about 6 hours at 48,000 Hz) is refused in words, naming the setting that can be changed, before a file is chosen. Everything about the
file is checked before anything is written.

## What it costs

Measured in the Release build on one development PC (Windows, an 8-core Intel Core i9-10885H, shared with other programs that kept the
processor between 18 and 48% busy when the runs began and ended), by a throwaway test that is not in the repository: it compiles the
project, calls `WaveStemExporter.ExportAsync` into a scratch folder and reads the peak working set of the test process (about 55 MB
before the export starts, the test host's own). Each project was run three times, in separate processes; the ranges are what that machine
did, not a promise for another one.

| Project | Files | Audio unzipped | Compile | Write | ZIP | `manifest.json` (in the ZIP) | Peak memory |
|---|---|---|---|---|---|---|---|
| 16 bars, no lane (the first project) | 1 | 6.2 MiB | 91 to 126 ms | 159 to 242 ms | 0.34 MiB | 11,748 bytes (1,212) | 60 to 61 MB |
| 16 bars, 3 lanes | 4 | 24.9 MiB | 95 to 187 ms | 322 to 622 ms | 1.69 MiB | 46,908 bytes (2,539) | 61 to 62 MB |
| 1,024 bars, 8 lanes of 14 to 16 clicks to a 4-quarter cycle (125,956 clicks) | 9 | 3,378.3 MiB (3.3 GiB) | 0.5 to 1.2 s | 31.6 to 39.0 s | 140.18 MiB | 19,119,390 bytes (489,278) | 100 to 101 MB |
| 1,024 bars, 8 lanes of 118 to 120 clicks to a 4-quarter cycle (977,924 clicks) | 9 | 3,378.3 MiB (3.3 GiB) | 4.1 to 4.5 s | 54.7 to 62.1 s | 212.51 MiB | 148,631,331 bytes (3,932,497) | 331 to 334 MB |

1,024 bars is the most the Export length box takes and 8 the most lanes a project has, so the last two rows are the largest project the box
allows; a project with a program can be longer (a track of up to 4 GiB, about 6 hours, is accepted) and was not measured. For the two large
projects the write took 9.6 to 11.8 and 16.6 to 18.8 seconds for each GiB of audio. The question adds "This can take a while." from 512 MiB
unzipped; by proportion that size takes about 5 to 9 seconds here, which was not measured on its own. The plan is compiled before the
question, off the interface's thread, with the command off and nothing else on screen: the compile column is that wait. The densest
project's manifest is 149 MB of text, 3.9 MB in the ZIP. The peak memory above is for compiling and writing a project of that size.

The ZIP's compression is Deflate at the default level. Compared in the same build, three runs of the two small projects (the median of the
three writes; one run of each level in the third set was several times slower while the PC was busy, which the median leaves out):

| Level | 16 bars, no lane: ZIP, write | 16 bars, 3 lanes: ZIP, write |
|---|---|---|
| Optimal (used) | 0.34 MiB, 183 ms | 1.69 MiB, 468 ms |
| Fastest | 0.46 MiB (36% larger), 219 ms | 2.26 MiB (34% larger), 544 ms |
| Smallest size | 0.27 MiB (20% smaller), 316 ms | 1.43 MiB (15% smaller), 886 ms |
| No compression | 6.24 MiB, 300 ms | 24.95 MiB, 1,073 ms |

No other level was faster in the median, and the largest project made a ZIP of 252.66 MiB at the fastest level against 140.18 MiB at the
default (one run, so no time is given for it). The smallest size takes longer for 15 to 20% less. This was measured on the machine described
above only.

## What has been tried, and what has not

**Runs of the real app** (four processes of a Debug build of the head with the save dialog bypassed, a throwaway patch that was never committed: the
six-lane project twice, the largest project once and a project that was playing once; a private
desktop; the app's data in a scratch folder; nothing audible), driven through the window's own automation, and the archives read by Python's
`zipfile` and `wave` modules, which share no code with the app:

- The question named 7 tracks, about 0:34 each and about 43.6 MiB unzipped for a project with six lanes; Cancel wrote nothing and left the
  command on; Continue wrote a ZIP of 1,578,043 bytes and the page said "Saved locally"; a second export replaced the file with identical bytes
  and the bytes were identical across two runs of the app. The reader found the entries in order (`main.wav`, `lane-01.wav` to `lane-06.wav`,
  `manifest.json`), every CRC correct, every entry dated 1 January 1980, 33 checks and none failing. Among them: the click counts of the
  manifest against the files; a lane called `../../outside.wav` is still `lane-02.wav` and nothing was written outside the folder; the
  muted lane, the lane with a chance of 0 and the lane without a sound have no audible click and are silent files of the same length,
  while the lane with no name has its own 64 clicks; each track's first sound comes one frame after its first click; the lane files are
  silent for the whole count-in (96,000 frames); and the seven files added together differ from the mixed WAV of the same project by at
  most 1 step (the bound for 7 files is 4), the mix being nowhere near full scale.
- The largest project (1,024 bars, 8 lanes): the question named 9 tracks of about 34:10 and 3.3 GiB, with the line that saving can take a
  while. While it wrote, the folder held the temporary file next to the destination, the command was off and the page said the tracks were
  being saved. Closing the window then made the process exit 0.6 seconds later and left the folder empty: no archive, no temporary file.
- A silent project that was playing: the question named 3 tracks; the export wrote the ZIP; the transport still read Playing and the position
  moved on afterwards. This says nothing about how it sounded, which nobody heard, or about a slow machine, where the rendering could make the
  audio stutter.

**Six more runs of the real app after Copilot's reviews**, each a pair, one on the head that was reviewed and one on the head that answered it
(Debug builds with the save dialog bypassed, as above, and for the first two a throwaway delay in the compile that was read from an environment
variable; none of it was committed), on the same private desktop, driven the same way:

- *The saving notice.* On the largest project, while the archive was written (the command off, the temporary file in the folder and the page saying
  *Saving the tracks.*), Save to library put *Saved locally* in the one notice bar. On the head before the answer the bar was empty 7.5 seconds
  later, with the export still being written; on the head with the answer *Saving the tracks.* was back 7.5 seconds after Save to library and was
  still there at 9.5. Closing the window then stopped the export, the process ended 0.5 seconds (before) and 0.9 seconds (after) later,
  and the folder was empty.
- *A refused close while the plan is compiled.* The compile was delayed by 16 seconds, the app's settings file was locked by the driver so that a
  change of the export length could not be written (the app showed its access error), the tracks command was pressed and the window was asked to
  close: the close was refused (the process went on and the error stayed). When the compile ended, the head before the answer asked its question,
  23.1 seconds after the close, although the window had been asked to close; the head with the answer asked nothing, and the command was on again
  24.5 seconds after the close. Nothing was written in either run. The lock was then released, and a second close ended the process in 0.4 and
  0.5 seconds.
- *An error that is open when the writing begins* (the second review). The settings file was locked while the export length was changed, so that
  the app showed its access error, and then released; the tracks command was pressed on the largest project and Continue chosen. When the file
  was being written the head that was reviewed showed *Saving the tracks.* in place of the error (which is the finding); the head with the answer
  still showed the error, 3 seconds later as well. The notice bar was then dismissed with its close button: the reviewed head showed nothing, the
  head with the answer showed *Saving the tracks.* again, 2 and 4 seconds later too. Closing the window ended the process in 0.4 and 0.5 seconds
  with the folder empty.

**Not tried:** the Windows save dialog (it does not open on a private desktop, so the run bypassed it; the gates cannot save a file through
it either); in a real window, a close while the question or the save dialog is open (an open question is answered No by the close, which is
reasoned from the code) and a close that is refused after the file is being written (held by the unit tests of the run guard only); opening an
archive in any music software or sampler; listening to any track; archives that need ZIP64 (a track longer than
4 GiB is refused, so only a project with a program of nearly 6 hours, or compressed data above 4 GiB, could need it; the tests' strict reader
accepts no ZIP64 structure, so such an archive would be refused by it, and none was made); projects longer than 1,024 bars; a slow machine;
Narrator; Windows text sizes above 100%; the Contrast themes; a musician's reading of the Dutch, German, French and Spanish texts
(docs/design/glossary.md lists the questions).