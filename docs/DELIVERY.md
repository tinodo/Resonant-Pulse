# Delivery status

The reviewed full product design is the requirements baseline. This file records
implemented and independently verified behavior rather than treating a scaffold
or a successful compile as the complete workstation.

| Phase | State | Evidence |
|---|---|---|
| Requirements/competitive research | Complete design baseline | design/metronome-design.md and design/metronome-review.md |
| Architecture/contracts | Implementing | IMPLEMENTATION-CONTRACT.md |
| Musical engine | Hosted regression suite, typed recovery outcomes and Windows rollback tests passed | Release run 40372330283: Linux 236 cases (232 passed, four platform skips); Windows export integrity also passed |
| App semantic regressions | Two hundred sixty actual-source cases passed, including output selection, editor publication, preferences, meter presets, mixed-meter/ramp/repeat stop deadlines, fades, timing-report limits and the live tempo/meter splice engine | Release run 40372330283: 260/260 passed; overlapping workspace cases also passed on Windows; per-build TRX reports are retained by CI |
| Native application | x64/ARM64 compilation, real x64 startup, visible first-use controls (every primary control visible at 800x600 and 1000x720) and development-package activation passed | Protected-main run 40371460154 and release run 40372330283 passed; hosted playback, live-edit and counted-stop scenarios explicitly reported not-run-no-output |
| CI/download/MSIX | Preview 0.1.0-preview.6 published with x64/ARM64 ZIPs and checksums after fresh CI and exact-source security checks | Release 40372330283 at protected main 44a60d6; public x64 ZIP SHA-256 1ee905c3627281c3227f60ee2f857ec62d18313eb157f11a1d8455d9707abe04 matched its checksum file and the GitHub digest, and the embedded provenance (commit, version, architecture, run) was checked locally |
| Dependabot/Copilot | Dependency review and CodeQL passed; automatic review and exact-head completion gate exercised and required | PR #16: review 5656056175 at 5b8aa67 raised three findings, each reproduced, fixed with regression tests and resolved; re-review 5656462400 at 7dc2144 reported no open issues; all eight required checks passed before the guarded squash merge. Earlier example: review 5646429350 at 0edb5de on PR #7 |
| Practice timer and counted stops | Live 0..86400-second, written-beat and complete-bar limits; earliest enabled boundary/program end wins; count-in excluded and pauses preserved; exports unchanged | Actual public preview.3 passed the exact verifier on the saved output: four beats beat competing limits, 17 bars crossed the real 16-bar loop seam, and a paused 16-beat run resumed to completion. Apply and final reset wait for explicit successful publication/recovery acknowledgment |
| Play redesign and live edits | Implemented: large central instrument with the tempo inside, sheet-music meter picker with a Custom builder where every written beat is a click, beat-strength bars, live -/TAP/+, Cancel/Done panels, custom title bar and silent session restore. Tempo, meter, feel and sound edits are spliced into running playback at the next pulse; a program document keeps tempo and meter under its own control | PR #16 (squash 44a60d6): deterministic splice, seam, rewind, deadline and consumer tests; native first-use verifier on x64; Copilot re-review reported no open issues. Hardware listening and latency acceptance of live splices remain unestablished |
| Physical-device/accessibility validation | Basic local first-use, released-preview playback and counted stops established (preview.3); acoustic timing, sound quality, live-splice latency and comprehensive accessibility acceptance remain unestablished | Public preview.3 was exercised, diagnostic state removed, preferences restored byte-for-byte and original recovery absence restored. Public preview.6 was installed on the owner's workstation after a dry run on copies: it started responsive and stopped, the saved preferences and recovery draft were byte-identical after startup, and the Start menu shortcut was updated; no audio was played |
| Store identity | Supplied and configured | packaging/StoreIdentity.json; owner-provided Partner Center screenshot, 25 October 2026 |
| Store candidate | Actual reserved-identity MSIX generated and installed activation verified | Run 40245073254 succeeded with submit_update=false; candidate artifact is not a signed public installer or Store publication |
| Store first publication | Not submitted; Partner Center certification and authorization still required | RELEASING.md |

The coordinator updates this table with actual CI links, review outcomes and
capability gaps before a release is described as complete.

Verified native/build/test evidence (protected main, 44a60d6):
https://github.com/tinodo/Resonant-Pulse/actions/runs/40371460154

Exact-source CodeQL evidence:
https://github.com/tinodo/Resonant-Pulse/actions/runs/40371460083

Release run:
https://github.com/tinodo/Resonant-Pulse/actions/runs/40372330283

Published preview:
https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.6

The native smoke gate verifies activation, a responsive window and visible
first-use controls. It exercises Start/Stop only when an output exists and records
`not-run-no-output` otherwise; this is not acoustic timing or speaker acceptance.
Its capture must contain the complete native window within the display; it is not concept artwork.
Windows rollback tests overlap some Linux export coverage; job case counts must
not be summed and represented as a unique-test count.

See CAPABILITIES.md for the implemented-source scope and full-product gaps.

Main is protected by the actual engine, Windows x64/ARM64, Windows file-integrity,
dependency, CodeQL and exact-head Copilot completion checks, plus required resolution of review conversations.
These gates are not an approval from Copilot; its review is a comment review.
