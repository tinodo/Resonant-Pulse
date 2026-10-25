# Delivery status

The reviewed full product design is the requirements baseline. This file records
implemented and independently verified behavior rather than treating a scaffold
or a successful compile as the complete workstation.

| Phase | State | Evidence |
|---|---|---|
| Requirements/competitive research | Complete design baseline | design/metronome-design.md and design/metronome-review.md |
| Architecture/contracts | Implementing | IMPLEMENTATION-CONTRACT.md |
| Musical engine | Hosted regression suite and Windows rollback tests passed; typed recovery-outcome cases added | Run 40223399215 at e5df1aa: Linux 185 executed/passed of 189 cases; the Windows file-integrity job passed 17 overlapping cases; latest changes require fresh gates |
| App semantic regressions | Twenty-nine actual-source cases passed; editor publication/identity, recovery notice and System-language corrections in progress | Run 40223399215: 29/29 passed; additional corrections must pass before merge |
| Native application | x64/ARM64 compilation and real x64 native startup passed | Run 40218751867 at a46ba31: dark first launch, complete native frame within a 1024x720 work area, workspace initialized stopped without playback/capture |
| CI/download/MSIX | Hosted CI, native startup and real development-package install/activation/identity/uninstall passed; public download not yet published | Run 40223399215 at e5df1aa; release requires fresh gates on protected main |
| Dependabot/Copilot | Dependency review and CodeQL passed; native automatic review and exact-head completion gate exercised; five actual reviews completed | Completion monitor 40223399203 passed and is now required; latest editor/recovery/localization corrections require fresh gates |
| Physical-device/accessibility validation | Not established | Requires actual installed app/device results |
| Store identity | Supplied and configured | packaging/StoreIdentity.json; owner-provided Partner Center screenshot, 25 October 2026 |
| Store first publication | Not submitted; Partner Center certification and authorization still required | RELEASING.md |

The coordinator updates this table with actual CI links, review outcomes and
capability gaps before a release is described as complete.

Verified native/build/test evidence:
https://github.com/tinodo/ResonantPulse/actions/runs/40223399215

Exact-source CodeQL evidence:
https://github.com/tinodo/ResonantPulse/actions/runs/40223399066

The native smoke gate verifies activation and a responsive stopped window, not
speaker output, timing precision or musical interaction. Its capture must contain
the complete native window within the display; it is not concept artwork.
Windows rollback tests overlap some Linux export coverage; job case counts must
not be summed and represented as a unique-test count.

See CAPABILITIES.md for the implemented-source scope and full-product gaps.

Main is protected by the actual engine, Windows x64/ARM64, Windows file-integrity,
dependency, CodeQL and exact-head Copilot completion checks, plus required resolution of review conversations.
These gates are not an approval from Copilot; its review is a comment review.
