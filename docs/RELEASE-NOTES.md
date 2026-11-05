# Resonant Pulse 1.0.0

Resonant Pulse is a native Windows metronome and rhythm workstation (WinUI 3, .NET 10) for focused practice. It works on your own
computer: no account, no microphone, no network and no telemetry. It is free and open source (MIT).

## What you can do with it

- **Play** is the metronome. Type an exact tempo, or nudge it with the -, **TAP** and + buttons; switch on **Tap to start** and tap
  the beat to start at your own tempo. Choose a time signature from a sheet-music picker (or build your own), click the beat bars to
  change how strong each click is, add a count-in, a subdivision or swing, pick a sound, and set a practice timer in seconds, beats or
  bars. Most changes are heard while it plays.
- **Studio** is where you build your own rhythm in lanes: layers of clicks, each with its own pattern, sound, volume, pan and chance,
  with a Euclidean rhythm generator. A practice program is a list of steps with tempo ramps, repeats, silent bars and a time signature
  of their own, and it can repeat until you press Stop. A strip at the top shows the metronome while you edit. A change made in Studio
  is saved at once and heard after Stop, then Start.
- **Train** follows a program (status, progress and a chart of the planned tempo) and runs a keyboard timing check, whose result you
  can export as a CSV file.
- **Stage** is a setlist for the stand: now playing, up next, Next with a count-in, notes for each song, Lock editing and Always on top.
- **Library** keeps your projects, starts you from ready-made rhythms, and exports WAV, MIDI, or one WAV per track in a ZIP.
- **Settings** has the language (English, Dutch, German, French and Spanish), the theme and Reduce motion. The audio output is chosen
  with **Audio** in the bar at the bottom.

Everything stays on your computer. Projects are ordinary files that you can save, open and share.

## What's new since preview.12

Five changes since preview.12, the last published preview.

- **Studio shows the metronome.** In preview.12 the page that edits the rhythm showed nothing of the metronome, so you could not see
  the beat while you built a lane or a step. Studio now has a strip pinned above its page, in view while you scroll: the tempo and the
  time signature, a light for each beat of the bar (the beat that sounds is a filled disc; the first beat of the bar, and of each
  additive group, is larger) and, while it plays, *Count-in* or the step that plays ("Step 2 of 4: Faster", on one line). Nothing is
  lit, and no step is named, while nothing plays or in a silent bar of a project that hides its phase cues in gaps (on by default).
  A bar of 13 or more beats gets small lights, so that even 64 beats take only a few rows. It follows the time signature of the step
  that plays and wraps under the tempo in a narrow window. A screen reader reads it on demand ("120 BPM, beat 2 of 4").
- **A program can repeat until you press Stop.** In preview.12 a program ended after its last step. The practice program in Studio now
  has a switch, *Repeat the program*, above its steps. Off (the default, and how every project and file before this release behaves)
  the program stops after its last step. On, it starts again from its first step and plays until you press Stop: the count-in is played
  once, a limit of the Timer panel counts through the passes, and the bars keep counting. Train says so under the steps. The switch is
  saved with the project and Undo takes it back; removing the last step turns it off. An export is still one pass of the program. A
  program cannot stay on its last step: with the switch on it starts over from its first. Tempo and time signature changes are still
  heard from the next Start.
- **Studio says what it is for.** Studio now has two lines under its title: the first says what the page is for, the second how it
  works (your project is kept as you edit it, a copy as a file is **Save project** in the Library, and to hear a change you press
  Stop, then Start). Settings, the one page that had no such line, has one now too. All of it is in the five languages.
- **Repair: Train brings the command that takes over into view.** After *Start check* the keyboard focus moves on to *Hit*, and after
  *Finish and show result* to *Export result (CSV)*; the page is now asked to bring that command into view. In preview.12 the notice
  that *Start check* opens could leave *Hit* below the part of the page that was shown.
- **Repair: when a notice opens, the page follows the control that has the keyboard focus.** A notice such as *Saved* takes room from
  the foot of the page for about six seconds, and a control at the foot that had the focus could end up out of view. The page now keeps
  it in view. Only a notice that opens is handled.

## Install and requirements

- Windows 11 24H2 (build 26100) or later, on x64 or ARM64.
- Download the ZIP for your architecture from this page, extract **all** files, and run `ResonantPulse.exe`. The ZIP includes the .NET
  and WinUI runtime files. A `.sha256` file accompanies each ZIP.
- The ZIPs are not signed, so Windows SmartScreen may warn. Compare the SHA-256 checksum instead of turning protection off.
- Microsoft Store: since 4 November 2026 (the release date in the Store's catalog; added to these notes on 5 November) Resonant Pulse is in
  the [Microsoft Store](https://apps.microsoft.com/detail/9nrfh2c8x2ql): free, one click to install, kept up to date by the Store. A new
  version reaches the Store only after Microsoft has certified it, so the Store can be behind the download on this page.

## Known limits

- **Sound and timing were not measured on other hardware.** No hosted runner has an audio output, and the automatic checks play at an
  inaudible volume. The release has been listened to on the owner's PC; no listening or timing test was made on other hardware, and
  nothing here is a claim of microsecond timing.
- **Not tried:** Narrator, the Contrast themes, Windows text sizes above 100%, ARM64 hardware, and an install of the Store package on a
  clean Windows.
- **Translations:** the Dutch, German, French and Spanish texts have not been read by a native speaker.
- **Files:** the Windows save and open dialogs were exercised by a stand-in in the automatic checks (they cannot open on a build
  runner), and nobody has opened a tracks ZIP in music software. A long export (WAV, MIDI or CSV) shows no notice while it writes.
- **Known and not fixed:** after *Save to library* the list above the button gets a row, which can push the button out of view in a
  narrow window, and the page does not follow it. Studio has no Help panel of its own and no button that stops and starts for you.

The notes of the earlier previews, with their known issues, are in
[docs/RELEASE-NOTES-previews.md](https://github.com/tinodo/Resonant-Pulse/blob/main/docs/RELEASE-NOTES-previews.md). What is built and
what is not is in [docs/CAPABILITIES.md](https://github.com/tinodo/Resonant-Pulse/blob/main/docs/CAPABILITIES.md), the evidence is in
[docs/DELIVERY.md](https://github.com/tinodo/Resonant-Pulse/blob/main/docs/DELIVERY.md), and the privacy policy is
[docs/PRIVACY.md](https://github.com/tinodo/Resonant-Pulse/blob/main/docs/PRIVACY.md).
