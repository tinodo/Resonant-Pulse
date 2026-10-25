# Delivery status

The reviewed full product design is the requirements baseline. This file records
implemented and independently verified behavior rather than treating a scaffold
or a successful compile as the complete workstation.

| Phase | State | Evidence |
|---|---|---|
| Requirements/competitive research | Complete design baseline | design/metronome-design.md and design/metronome-review.md |
| Architecture/contracts | Implementing | IMPLEMENTATION-CONTRACT.md |
| Musical engine | Hosted regression suite, typed recovery outcomes and Windows rollback tests passed | Run 40241740117 at 49f9d6f: Linux 195 executed/passed of 199 cases; Windows file integrity passed separately with overlapping coverage; subsequent revisions require the same gates |
| App semantic regressions | Fifty-four actual-source cases passed, including output selection, tempo-chart preview, editor publication/identity, recovery guidance, System language and benign editor revisions | Run 40241740117: 54/54 passed; per-build TRX reports are retained by CI |
| Native application | x64/ARM64 compilation, real x64 startup, visible first-use controls and development-package activation passed | Run 40241740117 at 49f9d6f; first-use playback is explicitly not run when a hosted runner has no audio endpoint |
| CI/download/MSIX | Hosted CI, native startup and real development-package install/activation/identity/uninstall passed; download publication is controlled by the release workflow | Run 40241740117 at 49f9d6f; the GitHub Releases page is authoritative for published downloads; every release requires fresh gates on protected main |
| Dependabot/Copilot | Dependency review and CodeQL passed; native automatic review and exact-head completion gate exercised and required | Monitor 40241740092 passed at 49f9d6f; PR #1 contains actual findings and responses. Completion alone is not approval; findings must be addressed before merge |
| Physical-device/accessibility validation | Basic local first-use playback established; acoustic timing, sound quality and comprehensive accessibility acceptance remain unestablished | Delivered f198cc0 selected the Windows-default Realtek output; actual Start advanced through count-in to bar 18 at 00:37. Diagnostic playback then stopped with the app left open |
| Store identity | Supplied and configured | packaging/StoreIdentity.json; owner-provided Partner Center screenshot, 25 October 2026 |
| Store first publication | Not submitted; Partner Center certification and authorization still required | RELEASING.md |

The coordinator updates this table with actual CI links, review outcomes and
capability gaps before a release is described as complete.

Verified native/build/test evidence:
https://github.com/tinodo/Resonant-Pulse/actions/runs/40241740117

Exact-source CodeQL evidence:
https://github.com/tinodo/Resonant-Pulse/actions/runs/40241740171

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
