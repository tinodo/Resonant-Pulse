# Resonant Pulse

Native Windows rhythm workstation: C#/.NET 10 and WinUI 3.

## Operating constraints

- The developer machine is shared, and the owner has explicitly authorised local
  build, test and visual verification for this repository. Keep it light: one
  build at a time, below-normal priority, no watchers or lingering build
  servers, no SDK installs, no broad recursive filesystem scans. Never launch
  audio or press Start on a developer machine; run test instances against an
  isolated data folder (`RESONANT_PULSE_HOME`) and never capture another
  application's window.
- GitHub Actions remains the authoritative gate (hosted build, native, package,
  CodeQL, review). Local runs give fast feedback only; never treat an
  unexecuted test or an uploaded artifact as a pass.
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
