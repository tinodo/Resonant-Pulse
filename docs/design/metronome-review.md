# Resonant Pulse - independent model critique and design response

Date: 25 October 2026. Scope: ideation/design only.

## Review method and evidence

Two independent `rubber-duck` tasks used **GPT-6.1 Sol (`gpt-6.1-sol`)** and **Claude Sonnet 5.5 (`claude-sonnet-5.5`)**. Each received the same complete product specification and the Play, Studio and Train SVG source artboards. Each was asked to challenge the whole design, not merely one assigned specialty. Neither edited or implemented the proposal. Their reviews were completed before the revisions below.

GPT verdict: strong and comprehensive, but pause/presentation boundaries, sync-unit conversion and measurement-clock contracts were design blockers.

Claude verdict: strong concept and unusually honest measurement discipline; document/transport ownership, MIDI representation, random reproducibility, stage workflow and artboard inconsistencies needed correction.

The artboards were inspectable as SVG markup; the reviewer results are not evidence of rendered UI testing. The coordinator separately opened the static artwork in the browser canvas. No app code, build, audio benchmark, instrument test, deployment or Store submission was performed.

## Consolidated decisions

| Finding | Raised by | Incorporated correction | Remaining implementation evidence |
|---|---|---|---|
| Render cursor is not audible position; buffered audio cannot be retracted | Both | Separate render/committed/presentation positions; Pause pending and effective boundary; drain/flush semantics; no instantaneous-mute promise. | Adapter response/latency and pause/resume no-loss tests. |
| Stop could silently discard document edits | Claude | Edits are immediately undoable document revisions; only playback swaps are deferred/cancelled. Next Start compiles latest document. | Document recovery and staged-edit transport tests. |
| Program and document had competing tempo ownership | Claude | Program references sections with explicit override precedence; one compiler/schedule; Studio and Train are projections of it. | Golden compiled arrangements and manual-override tests. |
| Compound tempo, MIDI clock and Song Position Pointer lacked mapping | Both | Canonical quarter-note coordinate, visible unit conversion, 24 clocks/quarter and six-clock position units; mode-specific transport semantics. | Normative spec and interoperability cases. |
| Link is a peer session, not simply a MIDI follower | Both | Dedicated authority matrix, shared phase/tempo/quantum semantics; default disables competing program ramps. | License decision and Link test plan. |
| Corrected early/late timing could absorb musician bias or device drift | Both | Input provenance, clock mapping, independent calibration, uncertainty, input/output profile invalidation; arrival-only keyboard measurements when needed. | Ground-truth audio/MIDI/input experiments. |
| Blind gaps could leak time through counters, charts or other windows | Both | Cue-mask matrix across all surfaces; separate audible events/performer targets; Internal Time defaults to blind mode. | Multiwindow visual masking and onset-matching tests. |
| Tempo ramps were named but not formally specified | GPT | Time-linear and beat-linear formulas, numeric example, endpoint ownership and Hold/Repeat/Skip semantics. | High-precision golden schedules. |
| Backing audio lacked anchors or a clear clock | Both | Fixed-media versus tempo-following modes, explicit source anchors, loop coordinate and compatibility restrictions. | Stretch/license and synchronized-loop tests. |
| Arbitrary rational patterns cannot all export exactly to SMF | Both | Export preflight for PPQ/tempo/meter/ramp errors; 1 BPM tempo-field limitation; seeded realization; exact WAV/project alternatives. | Normative SMF encoding and roundtrip error tests. |
| Seed alone does not guarantee stable randomness | Claude | Versioned event-keyed algorithm, repeat policy and published-vector prerequisite; record seed/version/realization. | Schema vectors and repeat/skip/resume reproducibility. |
| Lane phase across edits/sections was undefined | Both | Preserve absolute/normalized phase or restart; named commit boundary and preview. | Polymeter transitions and long-supercycle tests. |
| Stage required too many gestures; media/global control missing | Claude | One-action Next + count-in, authored medleys, opt-in global/media controls, pedal gesture mapping, mute scope and explicit stop-protection option. | Live musician/pedal usability and shortcut collision tests. |
| Minimized, power and managed-callback behavior lacked gates | Claude | MMCSS/adaptor guidance without machine tuning; battery/occlusion/display-off/format-change/GC load matrix. | Hardware soak and underrun results. |
| Artboard Mute/volume and control affordances were inconsistent | Both | Shared explicit transport buttons, Train Mute/volume, compact More/Mute targets, separate Stage/large-text/contrast artboard. | Actual WinUI hit targets/Narrator and localized text-size fit. |
| Essential contrast, beat shapes and horizon semantics were weak | Claude | Stronger essential stroke token; shape-redundant beat marks; discrete onset horizon; clarify strong-accent color. | Rendered contrast/visual-motion/accessibility testing. |
| Train graph scale/playhead contradicted its readout | Claude | Linear BPM axis and correctly positioned 92 BPM playhead, labelled master-bar axis. | Real schedule/chart projection tests. |
| Non-Western cycles, glossary and practice-log interpretation needed attention | Claude | Tala/clave/aksak examples with expert cultural review; musician-reviewed localization glossary; activity versus confirmed-practice distinction. | Expert review, localization review and non-invasive logging usability. |

## Judgement calls, not blindly accepted suggestions

- **No claim that every low-contrast decorative border violates WCAG.** Essential component/state/graphical information needs adequate contrast; purely decorative separators need not carry the interaction. The stronger stroke is used for meaningful markers and controls, consistent with the primary WCAG guidance.
- **Do not make device-loss auto-rerouting the stage default.** Preserve position and allow an explicit continue-silently policy, but never unexpectedly send a private headphone click to speakers.
- **Do not promise arbitrary media tempo following.** It requires source anchors, validated DSP and a defined authority; otherwise the combination is explicitly unavailable.
- **Do not raise the internal tempo floor merely to fit MIDI files.** Keep the musical range; export has declared representational limits and exact audio/project alternatives.
- **Do not turn every suggested integration into a launch promise.** Full-product intent includes the workflows; hardware/SDK/license-dependent paths stay clearly conditional rather than masquerading as completed capability.
- **Do not claim final approval from either reviewer.** Both reviewed the initial proposal; the coordinator incorporated changes. There was no second independent sign-off.

## What must precede "best on the market" language

Use a predeclared comparison method and representative musician tasks: a beginner starts 6/8 without a unit mistake; a drummer programs 3:4 and blind gaps; a classical player follows mixed meters/ramps; a jazz player programs swing/2-and-4; a stage player advances a medley with a pedal. Measure correctness, completion time, error/recovery rate and perceived workload against selected current competitors.

Independently measure audio onset error, long-run rate drift, MIDI jitter/follow behavior and audio/visual alignment on declared hardware. Include power/occlusion/device-change cases. Obtain real keyboard/Narrator and fluent-musician language reviews. Claims should name specific measured advantages, not an unverifiable universal ranking.
