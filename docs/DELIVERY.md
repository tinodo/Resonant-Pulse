# Delivery status

The reviewed full product design is the requirements baseline. This file records
implemented and independently verified behavior rather than treating a scaffold
or a successful compile as the complete workstation.

| Phase | State | Evidence |
|---|---|---|
| Requirements/competitive research | Complete design baseline | design/metronome-design.md and design/metronome-review.md |
| Architecture/contracts | Implementing | IMPLEMENTATION-CONTRACT.md |
| Musical engine | Hosted regression suite, typed recovery outcomes and Windows rollback tests passed | Release run 40381406205: Linux 238 cases (234 passed, four platform skips); Windows export integrity also passed |
| App semantic regressions | Two hundred seventy-one actual-source cases passed, including output selection, editor publication, preferences, meter presets, mixed-meter/ramp/repeat stop deadlines, fades, timing-report limits, the live tempo/meter splice engine and gap masking of ringing click tails | Release run 40381406205: 271/271 passed; overlapping workspace cases also passed on Windows; per-build TRX reports are retained by CI |
| Native application | x64/ARM64 compilation, real x64 startup, visible first-use controls (every primary control visible at 800x600 and 1000x720) and development-package activation passed | Protected-main run 40380680897 and release run 40381406205 passed; hosted playback, live-edit and counted-stop scenarios explicitly reported not-run-no-output |
| CI/download/MSIX | Preview 0.1.0-preview.7 published with x64/ARM64 ZIPs and checksums after fresh CI and exact-source security checks | Release 40381406205 at protected main 0050cdc; public x64 ZIP SHA-256 0c797b085db00421a0e2363e882825708f5a267217799bf58831ec684c7a6d47 and the ARM64 ZIP matched their checksum files and the GitHub digests, and the x64 embedded provenance (commit, version, architecture, run) was checked locally |
| Dependabot/Copilot | Dependency review and CodeQL passed; automatic review and exact-head completion gate exercised and required | PR #16 (preview.6): review 5656056175 at 5b8aa67 raised three findings, each reproduced, fixed with regression tests and resolved. Re-review 5656462400 at 7dc2144 reported no open inline issues but listed three previously missed medium findings (snapshot pairing, gap-mask tails, Contrast caption colors) under a "needs a closer look" verdict; they were not inline threads, so no required gate flagged them and they shipped in preview.6. PR #18 fixed all three with regression tests that fail without the fixes; its review 5657179021 at bc54c67 reported no open issues, with the caution that real-time rendering and native Contrast appearance still need human validation. All eight required checks passed before each guarded squash merge. Earlier example: review 5646429350 at 0edb5de on PR #7 |
| Practice timer and counted stops | Live 0..86400-second, written-beat and complete-bar limits; earliest enabled boundary/program end wins; count-in excluded and pauses preserved; exports unchanged | Actual public preview.3 passed the exact verifier on the saved output: four beats beat competing limits, 17 bars crossed the real 16-bar loop seam, and a paused 16-beat run resumed to completion. Apply and final reset wait for explicit successful publication/recovery acknowledgment |
| Play redesign and live edits | Implemented: large central instrument with the tempo inside, sheet-music meter picker with a Custom builder where every written beat is a click, beat-strength bars, live -/TAP/+, Cancel/Done panels, custom title bar and silent session restore. Sound and beat-strength edits switch at the next audio quantum; tempo, meter, tempo unit, subdivision, swing, group and gap-bar edits are spliced into running playback at the next primary pulse; a program document keeps tempo and meter under its own control | PRs #16 and #18 (preview.7 at 0050cdc): deterministic splice, seam, rewind, deadline, gap-tail and consumer tests; native first-use verifier on x64. The shipped preview.7 x64 binary passed three consecutive silent real-device runs (inaudible master gain 0.0001, isolated data folder, Windows default output): playback advanced, live sound, tempo and meter edits published without stopping, and the counted stops passed. Listening, latency and Contrast-theme appearance remain unestablished |
| Physical-device/accessibility validation | Basic local first-use, released-preview playback and counted stops established (preview.3); acoustic timing, sound quality, live-splice latency and comprehensive accessibility acceptance, including Contrast-theme appearance, remain unestablished | Public preview.3 was exercised, diagnostic state removed, preferences restored byte-for-byte and original recovery absence restored. Public preview.7 was installed on the owner's workstation after a dry run on copies: it started responsive and stopped, the saved preferences and recovery draft were byte-identical after startup and exit, and the Start menu shortcut was updated; the instance installed for the owner played no audio |
| Store identity | Supplied and configured | packaging/StoreIdentity.json; owner-provided Partner Center screenshot, 25 October 2026 |
| Store candidate | Actual reserved-identity MSIX generated and installed activation verified | Run 40245073254 succeeded with submit_update=false; candidate artifact is not a signed public installer or Store publication |
| Store first publication | Not submitted; Partner Center certification and authorization still required | RELEASING.md |

The coordinator updates this table with actual CI links, review outcomes and
capability gaps before a release is described as complete.

Verified native/build/test evidence (protected main, 0050cdc):
https://github.com/tinodo/Resonant-Pulse/actions/runs/40380680897

Exact-source CodeQL evidence:
https://github.com/tinodo/Resonant-Pulse/actions/runs/40380680942

Release run:
https://github.com/tinodo/Resonant-Pulse/actions/runs/40381406205

Published preview:
https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.7

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
