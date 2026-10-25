# Review instructions

Review correctness, not generated-code plausibility. Pay particular attention to:

- Dotted-unit BPM and quarter-note/MIDI conversion.
- Sample-frame onset placement, render-thread allocations, pause/resume,
  queued revisions, device disposal and state transitions.
- Accent/subdivision/gap boundaries, deterministic randomness, tempo-ramp
  integration and polymetric lane phase.
- Invalid or oversized documents, imports, cyclic setlists, atomic writes and
  recoverable user-visible failure paths.
- Complete localization, keyboard equivalence, Contrast themes and layout.
- Secret-free, least-privilege Actions. Never execute PR code under
  pull_request_target. A requested Copilot review is not a completed review.
- Release artifacts must originate from the tested SHA. Store identity and first
  publication are external gates, never defaults to fabricate.

Do not approve unsupported timing, hardware, Store availability or feature claims
just because the document describes intended behavior.
