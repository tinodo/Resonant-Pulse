# Delivery status

The reviewed full product design is the requirements baseline. This file records
implemented and independently verified behavior rather than treating a scaffold
or a successful compile as the complete workstation.

| Phase | State | Evidence |
|---|---|---|
| Requirements/competitive research | Complete design baseline | design/metronome-design.md and design/metronome-review.md |
| Architecture/contracts | Implementing | IMPLEMENTATION-CONTRACT.md |
| Musical engine | Hosted regression suite, typed recovery outcomes and Windows rollback tests passed | Reviewed source 3e2c2ca, run 40244472762: Linux 208 executed/passed of 212 cases, four platform skips; Windows export integrity passed 21 overlapping cases |
| App semantic regressions | Fifty-seven actual-source cases passed, including output selection, tempo-chart boundaries, editor publication/identity, recovery guidance and concurrent preferences | Run 40244472762: 57/57 passed; eight overlapping workspace cases also passed on Windows; per-build TRX reports are retained by CI |
| Native application | x64/ARM64 compilation, real x64 startup, visible first-use controls and development-package activation passed | Reviewed source run 40244472762; protected-main run 40245030920 passed again after merge; hosted playback is explicitly not run without an audio endpoint |
| CI/download/MSIX | Preview 0.1.0-preview.1 published with x64/ARM64 ZIPs and checksums after fresh CI and exact-source security checks | Release 40245073032 at protected main bee4205; reviewed source 3e2c2ca has the identical tree; public x64 ZIP checksum and embedded provenance checked locally |
| Dependabot/Copilot | Dependency review and CodeQL passed; native automatic review and exact-head completion gate exercised and required | Actual review 5645874986 at 3e2c2ca reported no open issues; all eight required checks passed and conversations were resolved before PR #1 merged normally |
| Physical-device/accessibility validation | Basic local first-use and released-preview playback established; acoustic timing, sound quality and comprehensive accessibility acceptance remain unestablished | Published 0.1.0-preview.1 advanced from count-in to bar 1, beat 4, at 00:03 on the saved Realtek output; stopped afterward and left open |
| Store identity | Supplied and configured | packaging/StoreIdentity.json; owner-provided Partner Center screenshot, 25 October 2026 |
| Store candidate | Actual reserved-identity MSIX generated and installed activation verified | Run 40245073254 succeeded with submit_update=false; candidate artifact is not a signed public installer or Store publication |
| Store first publication | Not submitted; Partner Center certification and authorization still required | RELEASING.md |

The coordinator updates this table with actual CI links, review outcomes and
capability gaps before a release is described as complete.

Verified native/build/test evidence:
https://github.com/tinodo/Resonant-Pulse/actions/runs/40245030920

Exact-source CodeQL evidence:
https://github.com/tinodo/Resonant-Pulse/actions/runs/40245030985

Published preview:
https://github.com/tinodo/Resonant-Pulse/releases/tag/v0.1.0-preview.1

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
