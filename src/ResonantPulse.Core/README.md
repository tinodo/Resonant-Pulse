# Core integration

Platform-independent .NET 10 production domain code; no package dependencies.
This directory has not been compiled or tested locally. CI must establish
correctness, allocation behavior, performance and interoperability before release.
The budgets below are explicit defensive limits, **not achieved benchmarks**.

## Fixed entry points

- `ScheduleCompiler.Compile(document, sampleRate, bars, includeCountIn)` validates,
  resolves finite sections and probability, and precomputes every audio sample
  and mixing voice. A program overrides the `bars` duration.
- `PcmRenderer.Fill(buffer, plan, startFrame, channels, masterGain, mute, loop)`
  is stateless and intended to allocate nothing for valid calls. No I/O, locks,
  lazy initialization, synthesis, randomness or document access occurs there.
  Supply the audio adapter's absolute render cursor, not a UI timer.
- `WaveExporter.ExportAsync` writes finite stereo PCM16 WAV and
  `<path>.manifest.json`. It uses `plan.DefaultMasterGain`, never loops, and
  truncates click tails at `TotalFrames`. Both outputs are staged and flushed
  before either target changes. Cancellation is honored before commit; commit
  and rollback are noncancellable. A failed commit restores prior contents or
  absence; rollback failure explicitly warns of a potentially mismatched pair
  and retains original backups for recovery. Cleanup failure distinguishes a
  completed pair from a failed export. Calls through this API queue one at a
  time, including rendering, commit, rollback and cleanup; cancellation while
  queued does not acquire the gate. This bounds export concurrency even for
  different destinations. This is not crash-atomic across two files and does
  not coordinate external processes modifying the destinations.
  `WaveExportException` distinguishes failed rollback (possibly mismatched
  outputs), successful commit with failed cleanup, and failed export cleanup.
  Its typed outcome and candidate recovery paths let callers localize the
  correct recovery action without parsing an English exception message.
- `MidiExporter.Preflight` reports PPQ, timing-error bound, errors and notices.
  `ExportAsync` writes SMF type 0 or throws an actionable `ArgumentException`.
- `DocumentStore.SaveAsync/LoadAsync` implement local schema-one JSON.
- `TapTempoEstimator.Tap/Reset` estimate selected-unit taps/minute.
- `TimingAnalyzer.Analyze` performs one-to-one chronological matching.

`PulseDocument`, settings, lanes, events, program steps and songs are mutable
editing/JSON models. Do not mutate them concurrently with compilation or saving;
the app owns revision synchronization. Compiled plans do not reference mutable
editing collections. Stage a whole new plan at a safe audio boundary.

`PulseSettings.PracticeSeconds` is a live-transport limit, not an export or
compiler duration override. Zero disables it; valid nonzero durations are
1..86400 seconds after count-in. The native adapter uses processed audio frames,
so pauses do not consume practice time. A finite program can finish earlier.
The adapter renders no audio at or after the exclusive deadline and fades the
last five milliseconds to avoid an abrupt waveform cut. This is not a guarantee
about downstream device buffers or calibrated acoustic presentation.
Disabled timers are omitted from JSON so ordinary projects retain the previous
shape. A nonzero timer requires a timer-capable reader; older strict readers
reject the new property rather than silently dropping the stop condition.

Live beat/bar auto-stop uses optional `PracticeBeats` (0..6400000) and
`PracticeBars` (0..100000). Count-in is excluded. Beats are the written meter's
denominator units, independent of the BPM note unit and audible subdivision.
Meter changes, ramps and repeats use the compiler's exact rational coordinates;
long loop counts reuse the prepared body rather than compiling a giant plan.
The first enabled seconds/beats/bars/program boundary wins. Silence still counts.
These settings do not truncate exported files. Zero-valued fields are omitted
for backwards compatibility; older strict readers reject enabled new fields.

## Musical coordinates and projection

`Rational` is a reduced arbitrary-precision fraction; default is zero. Arithmetic,
comparison, floor and ceiling are exact. `FromDouble` uses the invariant decimal
round-trip spelling rather than quantizing to a rhythm grid. JSON fractions are
strings such as `"1/3"`. Parsing is bounded; direct arithmetic is not artificially
quantized. `ToDouble` is an approximate conversion for physical-time integration.

Canonical position is quarter notes `q`. Quarter BPM = displayed BPM times
`BeatUnitQuarters`; it is **not** determined by the denominator. Dotted-quarter
= 80 in 6/8 means two primary clicks and 1.5 seconds/bar; eighth = 80 means six
clicks and 4.5 seconds/bar.

Main pulses retain selected-unit spacing across bars, including when a pulse
crosses a bar boundary. Authored program section/repeat boundaries restart the
main pulse. Main accents repeat by primary-pulse index within the master bar.
An empty accent array means normal. Additive `Settings.Groups` accent matching
main-pulse group starts; they do not create additional attacks. Groups require
matching meters throughout a program, otherwise validation rejects them.
Count-in is a separate constant-tempo section using the first resolved section's
meter/unit/initial tempo. Its own strong/normal scheme remains audible when the
body's main accents are silent; it has no lanes/subdivisions/gaps.

`Subdivision` divides the primary unit. Swing moves odd positions within pairs
in musical time before tempo integration. Nonstraight odd subdivisions are
rejected. Lanes/explicit offsets and authored tuplets are never implicitly swung.
`double` swing and offsets preserve their decimal value; a decimal approximation
to 2/3 may exceed MIDI PPQ limits. For exact ratios use rational `RhythmEvent`
positions in a lane.

Lanes have independent cycles and signed offsets. Negative-time opportunities
are clipped, not accumulated into a startup burst. Even pulses implement N:M
polyrhythm; different cycle lengths implement polymeter. `Pattern` repeats over
pulse indices within each cycle. With nonempty `Events`, authored rational
positions replace even pulses; `Pattern` must then be empty. Events must have
distinct positions inside the cycle. Silent accents are rests. `Tie` suppresses
a new attack; these percussive voices do **not** synthesize sustained tied tones.

**Current phase policy:** lanes preserve absolute body-quarter phase through
program steps/repeats, with q=0 after count-in. The contract has no authored lane
phase-policy control or section references. Explicit new-section restart and
normalized-phase edits from the broader design are not implemented. The app
must not label this as configurable section restart behavior.

`RenderPlan` optional integrations:

- `CountInFrames`, `BeatUnitQuarters`, `DefaultMasterGain`, `Seed`,
  `ProbabilityAlgorithm`.
- `SectionBoundaries`: frame and exact quarter boundaries, meter, bars,
  step/repeat index, ramp name. Count-in has step index -1.
- `FrameAtQuarter(q)`: exact coordinate to nearest absolute frame.
- `PositionAtFrame(frame)`: actual ramp inversion, including silent intervals.
  Pass 0..TotalFrames; modulo a looping cursor yourself. Returns
  `PlaybackPosition` with quarter, zero-based bar/beat, within-bar beat and pulse
  phases, selected-unit tempo, section identity, count-in and gap state.
  Count-in bar indices are negative. Beat index/phase refer to denominator units;
  pulse phase refers to the selected BPM unit from the section start, including
  when a pulse crosses a bar boundary.
- `Targets`: non-rest attacks, retained through gaps, probability, lane mute and
  solo, excluding count-in. Select an assessment lane explicitly. `Audible`
  describes the cue mask, not whether any hardware actually presented sound.

At a finite endpoint, projection returns the final section's endpoint. There
is no implied next bar. `TempoSegments` tempos use the selected note unit.
`LinearTime` and `LinearBeat` are integrated differently, as named; frames are
evaluated from absolute section time and rounded nearest, ties away from zero.
An event that rounds onto an exclusive section end is rejected rather than lost.

## Gaps, probability and loops

Fixed gaps suppress every audible layer, including tails from preceding clicks.
Master bars define their periods;
the audible/silent cycle restarts for each step repeat. Both gap counts must
be positive or both zero. Program gap settings explicitly override document
settings; zero/zero disables gaps for that step. Silent ranges are half-open
and adjacent ranges merge. `BlindGaps` is persisted; hiding every visual cue,
counter and conductor phase is the app's responsibility using `IsSilent`.

Lane mute/solo applies to body cues, not count-in. An active solo lane suppresses
main/subdivision clicks and non-solo lanes. Targets remain separate.
Per-event probability multiplies lane probability; it is Bernoulli, not a promise
of a fixed attack count.

`event-hash-v1` uses SHA-256 over:

1. ASCII `RP-EVENT-1` followed by a zero byte (11 bytes total).
2. Four signed little-endian int32 values: seed, zero-based program step index,
   zero-based repeat index, zero-based lane index.
3. UTF-8 canonical reduced absolute body coordinate `numerator/denominator`.

The first little-endian uint64 is shifted right 11 and divided by 2^53.
Draws do not depend on traversal order or other lane omissions. The same
document/seed produces the same schedule; reordered/inserted steps or lanes can
change identity and realization. There are no persisted stable section/lane IDs
or same-realization-repeat control yet. CI must pin published golden vectors
before calling cross-version reproducibility verified.

**Loop semantics:** `Fill(loop:true)` repeats the entire plan, including any
count-in, and mixes prior-cycle tails at seams. It does not invent pre-start
tails on the first cycle. For one initial count-in then repeated body, the
adapter must transition to a second plan compiled with `includeCountIn:false`.
`loop:false` produces zeros after the finite end. Mute clears the output without
altering time. Pause, commit/presentation boundaries and device loss are
adapter-owned; rendering never claims to retract committed hardware buffers.

## Resource/audio limits

- 1..1000 displayed BPM; explicit unit 1/64..16 quarters; meter numerator 1..64,
  power-of-two denominator 1..64; subdivisions 1..64; swing 0.5..0.9.
- Eight lanes; 1..256 pulses or at most 256 distinct authored events/lane.
  Authored event rationals are limited to 256-bit numerator/denominator.
- At most 1024 program steps, 4096 expanded repeats, 100000 bars, one million
  onset opportunities, 24 hours, and at least 0.1 seconds total.
- At most 4096 target/click attacks per sliding second and at most 256 audible
  attacks per 50 ms, including circular-loop seams. Short-loop limits are
  conservatively bounded. Silent/rest generation still counts against the total
  opportunity budget.
- Sample rate 8000..192000. Mono/stereo only. Maximum `Fill` call is 32768 frames;
  split larger buffers. Gain 0..1 and pan -1..1 must be finite.
- Original synthesized `Wood`, `Glass`, `Click`, `Kick`, `HiHat`, with separate
  accent levels and pitch. 50 ms finite, tapered, cached samples; equal-power
  stereo pan; mono ignores pan; explicit master gain; final hard clamp [-1,1].
  There is no normalization that silently changes the document's gain, and
  no claim of acoustic hearing safety.

## Persistence and export boundaries

Persistence uses fully-qualified local filesystem paths, bounded reads (4 MiB),
strict property names/schema, duplicate/unknown property rejection, finite
numeric/model validation, bounded setlist depth/count and reference-cycle
rejection. There is no implicit migration from absent/future schemas. Errors
propagate; the app should present localized explanations and retain edits.

Writes use a unique same-directory temporary file, flush-to-disk, and atomic
replace/rename. Existing files remain intact if writing or pre-commit
cancellation fails. Directories are not silently created. No remote content,
asset path or executable material is loaded from documents. This is not an
exchange-package, recovery-snapshot or multi-process conflict-resolution system.

MIDI positions are exact at the chosen PPQ (LCM starting at 24, <=32767). Tempo
events convert to **microseconds per quarter**, including dotted units. Meter
denominators are encoded as powers of two. Nonconstant continuous ramps,
unrepresentable 24-bit tempos, simultaneous same-lane MIDI attacks, total tick
range >28 bits, and >1 ms cumulative tempo-field rounding bound are explicitly
rejected. Per-onset PPQ quantization error is zero for accepted files; the report
bounds accumulated tempo-field error against ideal musical time, not rounded
audio frames or hardware timing. Global meter cannot express lane polymeter or
additive grouping; original source JSON remains in MIDI text metadata.
Per-lane channel pan/velocity and General MIDI timbres are portable approximations
disclosed by `Preflight.Notices`, not a claim to recreate the PCM sound.

## Timing and libraries

The tap estimator ignores bounce, uses a 12-interval median, rejects isolated
outliers/missed taps, and reacquires after three consistent changed intervals.
Nonmonotonic taps are rejected without shifting the timestamp reference.
`IdleResetSeconds` defaults to 4; raise it explicitly for very slow initial
tapping. Long idle starts a new estimate. Check `LastTapAccepted` for tap-in
start behavior; rejected input must not trigger playback. A tap-in that starts
playback sets `RejectEarlyOutliers`, which applies the outlier gate after the
first interval instead of after three, so a single odd interval is distrusted at
once and a tap that is not steady never starts anything.

Timing alignment maximizes chronological matches, then minimizes absolute
offset with deterministic tie handling. Sorted copies retain original indices.
Each attack participates once. Spread is scaled median absolute deviation;
drift is the difference of first-/last-half median offsets divided by median
time separation. `AmbiguousExpected` counts targets with multiple candidate
observations, not calibrated confidence. Zero-match median/spread/drift and
empty-target coverage are zero placeholders; check `Matched` before display.
Matching is limited to four million DP cells; analyze longer runs in explicit
sections. No microphone detection, input calibration, authoritative physical
early/late claim, or adaptive mastery scoring is implied.

`BuiltInLibrary.Presets/PracticePrograms` return fresh documents and stable
localization IDs. `RhythmGenerators.Euclidean/Rudiment/RudimentLanes` provide
original algorithmic studies. `TempoPrograms.Ladder` produces bounded exact
decimal steps, including the requested endpoint. These English display names
are fallback content; translation and musician review remain app/product work.
Ladders also enforce the validator's shared 100000-bar aggregate limit, including
an appended off-grid endpoint, rather than returning a document-invalid program.

## Broader design not represented by this contract

No time-authored/nested/mastery programs, per-section count-ins, random bounded
gap schedules, tempo-dependent swing curves, random tempo ladders, free-running
no-bar mode, pickup bars, metric-modulation authoring, sustained pitches/drone,
per-step millisecond microtiming, sample import, spoken-count assets, stems,
MIDI/tempo-map import, live MIDI/Link/device adapters, backing media/time stretch,
charts/notation, calibration/transient detection, journals/history/recovery,
package exchange or undo/redo engine is claimed here. Some belong to the app or
other integrations; the remainder need explicit additional domain APIs.
Unknown JSON fields, sounds and ramp types fail rather than being ignored.
Do not advertise the entire approved product as complete based on this Core.

## Recommended CI test matrix (no local execution)

1. Rational reduction/sign/default, huge arithmetic, exact comparisons,
   floor/ceiling negatives, JSON and decimal/exponent parsing, malformed and
   oversized input, double conversion including large numerator/denominator.
2. Golden schedules at 44.1/48/96 kHz: default 4/4, dotted 6/8 versus eighth
   6/8, 7/8 groups, selected-unit/bar crossing, subdivisions 2/3/5/7,
   paired swing and odd-swing rejection, exact authored tuplets/rests/ties.
3. N:M and three-way ratios, 3/4+4/4 12-quarter coincidence, signed offsets,
   section-boundary lane phase, solo/mute, event/lane probabilities, seeded
   reproducibility and published hash vectors including negative seed values.
4. Constant/step and both ramp maps (60->120 over six quarters: time-linear
   4 seconds, beat-linear 6*ln(2) seconds); reverse/tiny ramps, mixed meters,
   repeats, count-in, gaps, target preservation, absolute long-run rounding,
   frame/quarter projection round trips and silent-region visualization.
5. Callback allocation measurements after compilation, chunk equivalence,
   random access, seams/tails, mute, mono/stereo pan, gain zero, peak clamps,
   distinct waveforms, buffer/start-frame validation, simultaneous accents.
6. Every resource limit just below/at/above, including seam density, short
   loops, rational-size bounds, nested/cyclic setlists and unknown sounds.
7. WAV headers/frame length/sample payload/finite tails, manifest contents,
   RIFF-size rejection, destination failure, cancellation, manifest-commit
   failure preservation and WAV-commit rollback. MIDI byte-level parse, PPQ exactness, dotted-quarter tempo conversion,
   meter changes, seeded realization, note pairing, metadata, 1 BPM rejection,
   continuous-ramp/PPQ/tick/rounding preflight errors.
8. JSON round trips, missing/wrong/future schema, duplicate/unknown fields,
   malformed/null/nonfinite data, oversize/growing files, network/device/ADS
   paths, permission/missing-directory failures, replace/cancellation safety.
9. Tap median/outliers/bounce/missed/duplicate/nonmonotonic timestamps, idle,
   slow-tap configuration, intentional tempo reacquisition and reset.
10. Timing exact/early/late/jitter/drift, ambiguous extra attacks, one-to-one
    coverage, greedy-counterexample cases, unsorted input/duplicate timestamps,
    zero/empty data, tolerance boundaries and bounded alignment rejection.
11. Validate/compile every built-in preset and practice program. Check Euclidean
    counts/rotation/spacing at all small steps and rudiment hand/grace positions.
12. Independent CI build/API review and external MIDI/WAV interoperability;
    actual-device timing/soak/power/latency gates remain separate from Core tests.
