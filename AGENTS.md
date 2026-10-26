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
  screen copies do not work there, so the verifier skips Ctrl+M and screenshots;
  for an image of your own window use `PrintWindow` from a driver process that is
  separate from the process that started the application. Screenshots,
  keystrokes and audio are otherwise opt-in (`-Screenshots`, `-Keyboard`,
  `-RunAudio`) and only automatic on GitHub Actions. `-SilentAudio` runs the
  real audio scenarios (Start, live edits, counted stops, pause/resume) on the
  Windows default output at an inaudible master gain (0.0001, about -80 dBFS)
  seeded into the scratch data folder: it validates the engine and timing path,
  not how anything sounds.
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
