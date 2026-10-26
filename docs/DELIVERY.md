# Delivery status

The reviewed full product design is the requirements baseline. This file records
implemented and independently verified behavior rather than treating a scaffold
or a successful compile as the complete workstation.

| Phase | State | Evidence |
|---|---|---|
| Requirements/competitive research | Complete design baseline | design/metronome-design.md and design/metronome-review.md |
| Architecture/contracts | Implementing | IMPLEMENTATION-CONTRACT.md |
| Musical engine | Hosted regression suite, typed recovery outcomes and Windows rollback tests passed | Release run 40249141008: Linux 214 executed/passed of 218 cases, four platform skips; Windows export integrity passed 21 overlapping cases |
| App semantic regressions | Seventy-one actual-source cases passed, including output selection, chart boundaries, editor publication, preferences, timer deadlines/fades and timing-report limits | Release run 40249141008: 71/71 passed; eight overlapping workspace cases also passed on Windows; per-build TRX reports are retained by CI |
| Native application | x64/ARM64 compilation, real x64 startup, visible first-use controls and development-package activation passed | Protected-main run 40249136627 and release run 40249141008 passed; hosted playback is explicitly not run without an audio endpoint |
| CI/download/MSIX | Preview 0.1.0-preview.2 published with x64/ARM64 ZIPs and checksums after fresh CI and exact-source security checks | Release 40249141008 at protected main 73b9266; public x64 ZIP SHA-256 336d918427df1c63b306bd9f08c207134a35bb434ff3e5193da9c63f6fad3dc3 and embedded provenance checked locally |
| Dependabot/Copilot | Dependency review and CodeQL passed; native automatic review and exact-head completion gate exercised and required | Actual review 5646144476 at 02eda18 reported no open issues; all eight required checks passed and conversations were resolved before PR #5 merged normally |
| Practice timer | Live 0..86400-second limit, count-in exclusion, pause preservation and earlier finite-program end; exports unchanged | Candidate 0a821dc passed real-output auto-stop, pause/resume and frozen completion checks. Published preview.2 also preserved three seconds through count-in and completed at zero on the saved Realtek output |
| Physical-device/accessibility validation | Basic local first-use, released-preview playback and timer completion established; acoustic timing, sound quality and comprehensive accessibility acceptance remain unestablished | Published 0.1.0-preview.2 entered Playing/count-in, completed at 00:05 including count-in, then stopped. Test state was removed, preferences restored byte-for-byte, and the updated app left open/stopped with its timer disabled |
| Store identity | Supplied and configured | packaging/StoreIdentity.json; owner-provided Partner Center screenshot, 25 October 2026 |
| Store candidate | Actual reserved-identity MSIX generated and installed activation verified | Run 40245073254 succeeded with submit_update=false; candidate artifact is not a signed public installer or Store publication |
| Store first publication | Not submitted; Partner Center certification and authorization still required | RELEASING.md |

The coordinator updates this table with actual CI links, review outcomes and
capability gaps before a release is described as complete.

Verified native/build/test evidence:
https://github.com/tinodo/Resonant-Pulse/actions/runs/40249136627

Exact-source CodeQL evidence:
https://github.com/tinodo/Resonant-Pulse/actions/runs/40249136761

Published preview:
https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.2

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
