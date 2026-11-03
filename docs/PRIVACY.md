# Privacy

Applies to Resonant Pulse 1.0.0 for Windows. Effective date: 3 November 2026. Publisher and maintainer: Tino Donderwinkel.

## In short

Resonant Pulse works on your own computer. It does not use the microphone, does not connect to the internet, has no accounts, shows
no advertising and collects no analytics or telemetry. The app sends nothing about you, or anything you make with it, anywhere.

## What the app keeps on your computer

- **Your current project** (a recovery file), so that the app opens where you left it. A project holds what you typed: its name, the
  names of its lanes and steps, and the notes of a setlist.
- **Your preferences**: language, theme, Reduce motion, the audio output you chose, and the export length.
- **The projects you save to the library**, including the copy of the project that is saved there before another project replaces it.
- **A log** of what went wrong (for example a file that could not be written). It can name a file you saved or exported. It stays on
  your computer.

These files are in the app's data folder in your Windows user profile (`%LOCALAPPDATA%\ResonantPulse`; the Microsoft Store version
keeps the same files in its own private storage under your profile).

Files you save, export or open (project files, WAV, MIDI, a ZIP of tracks, the CSV of a timing check) are written to and read from
the places you choose. The app does not look at any other file.

## Sound and keyboard

Sound is made by the app and played on the audio output you choose. The app does not record or analyse any sound input. The timing
check on Train compares when you press a key with when each click was due; it reads key presses in the app's own window and keeps the
result only until you start another check or close the app, unless you export it as a CSV file yourself.

## Removing your data

Delete the files in the app's data folder, or delete a project from the library folder inside it. Uninstalling the Microsoft Store
version removes its private storage. A file that you exported, or a copy that you made, stays where you put it until you delete it.

## Other parties

The app does not share data with other parties, because it does not collect any. Windows, GitHub and the Microsoft Store have their
own privacy practices for downloading, installing and updating software and for reporting crashes; those are separate from the app.
The components the app is built from are listed in the [third-party notices](../THIRD-PARTY-NOTICES.md).

## Changes and contact

If this policy changes, the new version is published on the website at
<https://tinodo.github.io/Resonant-Pulse/privacy.html>, and in this file, with a new effective date. Questions about privacy can be raised
as an issue at <https://github.com/tinodo/Resonant-Pulse/issues>. Issues are public: do not attach private projects, credentials or
personal information to them, and ask there for a private way to get in touch before sharing anything sensitive.
