# Shared implementation contract

This is the integration boundary for independent core and native-UI work.
Namespaces below are fixed. Core has no Windows dependency.

## `ResonantPulse.Core`

Use public records/classes with public init/set properties that can round-trip
through System.Text.Json. Exact Rational arithmetic and compilation internals
can be added without changing these application-facing types.

- `enum Accent { Silent = 0, Soft = 1, Normal = 2, Strong = 3 }`
- `PulseSettings`: `double Tempo = 120`, `int BeatsPerBar = 4`,
  `int BeatDenominator = 4`, `double BeatUnitQuarters = 1`,
  `int Subdivision = 1`, `double Swing = 0.5`, `int CountInBars = 1`,
  `Accent[] Accents` default strong/normal/normal/normal,
  `int AudibleBars = 0`, `int SilentBars = 0`, `bool BlindGaps = true`,
  `float MasterGain = 0.5f`, `string Sound = "Wood"`.
- `RhythmLane`: `string Name`, `int Pulses = 4`,
  `double CycleQuarters = 4`, `double OffsetQuarters = 0`,
  `float Volume = 0.7f`, `float Pan = 0`, `bool Muted = false`,
  `bool Solo = false`, `double Probability = 1`, `Accent[] Pattern = []`,
  `string Sound = "Glass"`.
- `ProgramStep`: `string Name`, `double StartTempo = 80`,
  `double EndTempo = 80`, `int Bars = 8`, `int Repeats = 1`,
  `string Ramp = "Constant"` (Constant, LinearTime, LinearBeat),
  `int AudibleBars = 0`, `int SilentBars = 0`,
  `int BeatsPerBar = 4`, `int BeatDenominator = 4`.
- `SongEntry`: `string Name`, `PulseDocument Document`, `string Notes`.
- `PulseDocument`: `int SchemaVersion = 1`, `string Name = "Open practice"`,
  `PulseSettings Settings = new()`, `List<RhythmLane> Lanes = []`,
  `List<ProgramStep> Program = []`, `List<SongEntry> Setlist = []`,
  `int Seed = 1`.
- `ScheduledOnset`: `long Frame`, `int LaneIndex` (-1 main click,
  -2 subdivision, >=0 authored lane), `Accent Accent`, `float Gain`,
  `float Pan`, `string Sound`.
- `TempoSegment`: `long StartFrame`, `long EndFrame`,
  `double StartTempo`, `double EndTempo`.
- `RenderPlan`: `int SampleRate`, `long TotalFrames`,
  `IReadOnlyList<ScheduledOnset> Onsets`,
  `IReadOnlyList<TempoSegment> TempoSegments`,
  `IReadOnlyList<(long StartFrame, long EndFrame)> SilentRanges`.
  A parameterless/default constructor is not required.
- `ScheduleCompiler.Compile(PulseDocument document, int sampleRate,
  int bars = 16, bool includeCountIn = true)` returns RenderPlan.
  Validate unsupported state with descriptive ArgumentException, never silently
  drop invalid notes. Program, if present, determines finite duration.
- `PcmRenderer.Fill(Span<float> interleaved, RenderPlan plan,
  long startFrame, int channels = 2, float masterGain = 0.5f,
  bool mute = false, bool loop = true)` mixes scheduled events sample-aligned.
  This static API must be bounded/allocation-free per call. It may use precomputed
  plan caches created outside the render callback.
- `WaveExporter.ExportAsync(string path, RenderPlan plan,
  CancellationToken cancellationToken = default)` writes a finite WAV.
- `DocumentStore.SaveAsync(string path, PulseDocument document,
  CancellationToken cancellationToken = default)` and
  `DocumentStore.LoadAsync(string path, CancellationToken cancellationToken = default)`.
- `MidiExporter.ExportAsync(string path, PulseDocument document,
  int bars = 16, CancellationToken cancellationToken = default)` writes a
  representable Standard MIDI File or explicitly rejects with an explanation.
- `TimingAnalyzer.Analyze(IReadOnlyList<double> expectedSeconds,
  IReadOnlyList<double> observedSeconds, double toleranceSeconds = 0.15)`
  returns a `TimingReport` with `int Matched`, `int Missed`, `int Extra`,
  `double MedianOffsetMilliseconds`, `double SpreadMilliseconds`,
  `double Coverage`.
- `TapTempoEstimator.Tap(double timestampSeconds)` returns `double?` BPM;
  `Reset()` clears history.

Prefer optional additions over breaking these APIs. Inform the coordinator about
any essential contract correction before changing it.

## App ownership

The app owns UI, localizable strings/assets, WinRT audio adapter, file-pickers,
keyboard handling, native main window and view models. It references Core.
The coordinator owns root build props, solution, pipelines, packaging scripts
and public delivery documentation. Do not overwrite these from another scope.

## Required behavior

One transport survives tab changes. Startup is stopped. AudioGraph callbacks
fill exact sample offsets, independent of UI refresh. Pause retains position;
Stop resets without discarding edits; Mute continues musical time. Show actual
errors/device failure. No sound or recording begins as part of development.
Persist documents, settings and library. EN/NL/DE/FR/ES must cover visible UI and
errors, with culture fallback. Export actions cannot be dead controls.
Controls for unsupported integrations must say unavailable and why, not pretend
to work. Do not advertise unimplemented features as complete.
