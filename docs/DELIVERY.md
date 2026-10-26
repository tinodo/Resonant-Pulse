# Delivery status

The reviewed full product design is the requirements baseline. This file records
implemented and independently verified behavior rather than treating a scaffold
or a successful compile as the complete workstation.

| Phase | State | Evidence |
|---|---|---|
| Requirements/competitive research | Complete design baseline | design/metronome-design.md and design/metronome-review.md |
| Architecture/contracts | Implementing | IMPLEMENTATION-CONTRACT.md |
| Musical engine | Hosted regression suite, typed recovery outcomes and Windows rollback tests passed | Release run 40254973052: Linux 224 executed/passed of 228 cases, four platform skips; Windows export integrity also passed |
| App semantic regressions | Ninety-eight actual-source cases passed, including output selection, editor publication, preferences, mixed-meter/ramp/repeat stop deadlines, fades and timing-report limits | Release run 40254973052: 98/98 passed; overlapping workspace cases also passed on Windows; per-build TRX reports are retained by CI |
| Native application | x64/ARM64 compilation, real x64 startup, visible first-use controls and development-package activation passed | Protected-main run 40254604426 and release run 40254973052 passed; hosted playback/count scenarios explicitly reported not-run-no-output |
| CI/download/MSIX | Preview 0.1.0-preview.3 published with x64/ARM64 ZIPs and checksums after fresh CI and exact-source security checks | Release 40254973052 at protected main 165785f; public x64 ZIP SHA-256 5134222cd0e1e55cde359825becbca260736a3ded0297075940fb0c2d23757c3 and embedded provenance checked locally |
| Dependabot/Copilot | Dependency review and CodeQL passed; automatic review and exact-head completion gate exercised and required | Actual review 5646429350 at 0edb5de reported no open issues; all eight required checks passed and the Apply synchronization conversation was resolved before PR #7 merged normally |
| Practice timer and counted stops | Live 0..86400-second, written-beat and complete-bar limits; earliest enabled boundary/program end wins; count-in excluded and pauses preserved; exports unchanged | Actual public preview.3 passed the exact verifier on the saved output: four beats beat competing limits, 17 bars crossed the real 16-bar loop seam, and a paused 16-beat run resumed to completion. Apply and final reset wait for explicit successful publication/recovery acknowledgment |
| Physical-device/accessibility validation | Basic local first-use, released-preview playback and counted stops established; acoustic timing, sound quality and comprehensive accessibility acceptance remain unestablished | Public preview.3 was exercised, diagnostic state removed, preferences restored byte-for-byte and original recovery absence restored. The updated app was left responsive/open/stopped with limits off, and its Start menu shortcut updated |
| Store identity | Supplied and configured | packaging/StoreIdentity.json; owner-provided Partner Center screenshot, 25 October 2026 |
| Store candidate | Actual reserved-identity MSIX generated and installed activation verified | Run 40245073254 succeeded with submit_update=false; candidate artifact is not a signed public installer or Store publication |
| Store first publication | Not submitted; Partner Center certification and authorization still required | RELEASING.md |

The coordinator updates this table with actual CI links, review outcomes and
capability gaps before a release is described as complete.

Verified native/build/test evidence:
https://github.com/tinodo/Resonant-Pulse/actions/runs/40254604426

Exact-source CodeQL evidence:
https://github.com/tinodo/Resonant-Pulse/actions/runs/40254604425

Published preview:
https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.3

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
