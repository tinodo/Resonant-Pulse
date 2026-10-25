# Resonant Pulse

Native Windows rhythm workstation: C#/.NET 10 and WinUI 3.

## Operating constraints

- The developer machine is shared. Do not compile, build, test, bundle, install
  SDKs, launch audio, or run broad recursive filesystem scans locally.
- All build/type/test/package validation runs in GitHub Actions. Inspect exact
  files locally. Never treat an unexecuted test or an uploaded artifact as a pass.
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
