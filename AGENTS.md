# Resonant Pulse

Native Windows rhythm workstation: C#/.NET 10 and WinUI 3.

## Operating constraints

- The developer machine is shared, and the owner has explicitly authorised local
  build, test and visual verification for this repository. Keep it light: one
  build at a time, below-normal priority, no watchers or lingering build
  servers, no SDK installs, no broad recursive filesystem scans. Never play
  audible sound or press Start in the owner's session (use `-SilentAudio`,
  below); run test instances against an isolated data folder
  (`RESONANT_PULSE_HOME`) and never capture another application's window.
- Drive the native UI on a private desktop, not the owner's: run
  `packaging\Verify-Local.ps1` (it uses `packaging\Invoke-OnPrivateDesktop.ps1`).
  Nothing appears on screen, nobody can click into the test window, and no
  keystroke or screen copy can reach another window. Keyboard injection and
  screen copies do not work there, so the verifier skips Ctrl+M and screenshots,
  and `PrintWindow` returns a black frame because the window is never composed.
  For an image of your own build, compile with `-p:UiTest=true` and set
  `RESONANT_PULSE_UITEST_DIR`: the app then renders its own root to PNG files
  (`MainWindow.UiTest.cs`, excluded from every other build) when a driver process
  types a name into the hidden `UiTestCaptureName` box and invokes
  `UiTestCapture`; pair each image with UI Automation rectangles to measure
  alignment. A second hidden button, `UiTestGrowToolLabels`, enlarges the
  page-tools labels at run time to stand in for a Windows text-size change,
  which a test must not alter on the owner's machine. Screenshots,
  keystrokes and audio are otherwise opt-in (`-Screenshots`, `-Keyboard`,
  `-RunAudio`) and only automatic on GitHub Actions. `-SilentAudio` runs the
  real audio scenarios (Start, live edits, counted stops, pause/resume) on the
  Windows default output at an inaudible master gain (0.0001, about -80 dBFS)
  seeded into the scratch data folder: it validates the engine and timing path,
  not how anything sounds.
- Never launch a build that ignores `RESONANT_PULSE_HOME` (0.1.0-preview.5 and
  older do): it would open the owner's real projects and settings and could play
  them audibly. `Verify-Local.ps1` refuses such a build by looking for that
  setting's name inside `ResonantPulse.dll`; any other launcher must do the same.
- GitHub Actions remains the authoritative gate (hosted build, native, package,
  CodeQL, review). Local runs give fast feedback only; never treat an
  unexecuted test or an uploaded artifact as a pass.
- Read a Copilot review in full, not its headline. The native review can say
  "No open issues" and still list "previously missed" findings in its body;
  those are not inline threads, so neither the exact-head completion check nor
  required conversation resolution sees them (preview.6 shipped three that way).
  Confirm each against the code, fix it with a regression test that fails
  without the fix, and record the disposition in `docs/DELIVERY.md`.
- Preserve work belonging to other sessions. This repository is separate from
  the Resonant tuner.
- Use precise musical units. BPM has an explicit note unit; canonical musical
  position is quarter notes. Do not schedule audio with UI timers.
- Report errors. Do not silently ignore failed imports, device loss, Store
  identity, reviews or unsupported musical/export configurations.
- No secrets, private transcripts, local account profiles or proprietary media
  belong in this public repository.
- Tests use MSTest and deterministic fixtures. Production-only integrations
  require explicit capability checks.
- Store first publication requires Partner Center. Never manufacture identity
  strings or claim certification/publication without the provider's result.

## Phases and evidence

Requirements and reviewed visual design precede architecture and contracts,
implementation, deterministic tests, Windows build/package tests, independent
review, release preparation and operational documentation. Track actual
capability status in `docs/DELIVERY.md`; do not silently reduce the full design
to a scaffold or call a preview a complete product.
