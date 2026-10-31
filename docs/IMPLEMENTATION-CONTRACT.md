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
  `int PracticeSeconds = 0` (0 disables; live duration after count-in, up to 86400),
  `int PracticeBeats = 0` (written meter beats, up to 6400000),
  `int PracticeBars = 0` (complete authored bars, up to 100000);
  zero disables each live limit, count-in is excluded, the earliest limit/program end wins, and exports retain their authored duration;
  `Accent[] Accents` default strong/normal/normal/normal,
  `Accent[]? SubdivisionAccents` (omitted from the file while null: the strength of each click that splits a beat, in bar order,
  `Subdivision - 1` per pulse, repeating like `Accents`; null or empty leaves every split click Soft, up to 1024 entries),
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
  `IReadOnlyList<(long StartFrame, long EndFrame)> SilentRanges`,
  `int LaneCount` (the authored lanes of the project, also those that have no audible click: a lane
  that is muted, left out by solo or has a chance of 0 is still a lane).
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
- `WaveStemExporter.ExportAsync(string path, RenderPlan plan,
  IReadOnlyList<string> laneNames, CancellationToken cancellationToken = default)` writes one ZIP
  holding `main.wav`, `lane-01.wav` to `lane-NN.wav` (one for each authored lane, named by number
  and never by the lane's name) and `manifest.json`, through `AtomicFile.WriteAsync` (a temporary
  file next to the destination, then a move: a locked, read-only or full destination keeps its old
  content, and a cancelled export removes its temporary file; when it cannot, the exception is a `TemporaryFileCleanupException` (an
  `IOException`) with the path of the file that stays as `TemporaryPath` and both causes in its inner `AggregateException`). `laneNames` has the name of every
  authored lane in lane order (at most 256 well-formed UTF-16 characters each); the names are data
  in the manifest. `WriteAsync(Stream, ...)` writes the same bytes without the atomic move, and
  `GetUncompressedAudioBytes(plan)` is the audio of all the tracks before compression (4 bytes a
  frame for each of the `LaneCount + 1` tracks, without headers and the manifest). Every track
  starts at frame 0 and ends at `TotalFrames`, so they line up. A track longer than a RIFF file
  (4 GiB) is refused with an `ArgumentException` before anything is written. docs/WAV-STEMS.md is
  the specification.
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
  `double Coverage`, `int AmbiguousExpected`, `double DriftMillisecondsPerSecond`,
  `bool HasDriftEstimate` and the `Matches` with their original indices. Drift is a
  measurement only when `HasDriftEstimate` is true (at least four matches, the first and
  the last half of them centred on different moments, and a result that fits in a
  number); otherwise `DriftMillisecondsPerSecond` is 0 and means nothing, which is not a
  measured drift of 0. More than `TimingAnalyzer.MaximumTimestamps` (100,000) expected or
  observed timestamps, or more than `MaximumAlignmentCells` (4,000,000) comparison cells,
  are refused with an explanation.
- `TimingSession` is the immutable snapshot of one completed keyboard timing run:
  `new TimingSession(TimingSessionContext context, IReadOnlyList<double> expectedSeconds,
  IReadOnlyList<double> observedSeconds, double toleranceSeconds, DateTimeOffset?
  completedAtUtc = null)`. The context holds the project name, the target's name
  (`TimingSession.MainBeatName`, "Main beat", for the main beat), the lane index (-1 for
  the main beat), the sample rate, the first and last frame of the run and whether
  clicks that are not played were followed. It copies its inputs, validates every one of
  them (names of at most 256 well-formed UTF-16 characters, a lane of -1 to 7, a sample
  rate of 8,000 to 192,000 Hz, ordered frames, at most 600 seconds, finite timestamps
  from 0 up to the end of the run, the analyzer's budgets) and keeps the `TimingReport`
  that `TimingAnalyzer.Analyze` made of them. Nothing in it can change afterwards.
- `TimingCsvExporter.ExportAsync(string path, TimingSession session,
  CancellationToken cancellationToken = default)` writes the session as a CSV file
  through `AtomicFile.WriteAsync` (a temporary file next to the destination, then a
  move: a locked, read-only or full destination keeps its old content); `WriteAsync(Stream,
  ...)` writes the same bytes without the atomic move. The format is schema version 1,
  fixed and invariant: UTF-8 with a byte order mark, CRLF, every field quoted, eight
  columns, numbers with a point as the decimal separator (where they have a fraction) and
  every digit needed to read them back, a value
  that is not available as an empty field, and the project's and the target's names as
  JSON strings. docs/TIMING-CSV.md is the specification.
- `TapTempoEstimator.Tap(double timestampSeconds)` returns `double?` BPM;
  `Reset()` clears history. The optional `RejectEarlyOutliers` property (init-only,
  default `false`) applies the same interval gate after one interval instead of
  three; regular tap tempo is unchanged.

Tap to start (App) is an explicit, one-shot intent: never persisted or restored,
and not part of the project. The number of taps, 2 to 16, is an app preference
(`Preferences.TapInCount`, 4 by default; a stored value outside the range is
clamped where it is used). Consecutive accepted taps count towards it: a rejected
tap (bounce, outlier, non-increasing time) breaks the run and a gap of more than four
seconds between two taps (a fixed limit, not the estimator's three beats, which lets
slow taps wait longer) starts a new one. The tempo is the estimate to the nearest 0.1 BPM in the project's selected
note unit, which is never changed. It is saved as one edit of the project and only
then does the normal start run (the project's count-in applies). For a program,
every section's start and end tempo is multiplied by `tapped / firstSection.StartTempo`
as an exact fraction; no meter, ramp domain, repeat, count-in or setlist entry is
rewritten. A tempo outside 1 to 1000 BPM, a failed validation or recovery save and
a locked project never start playback, and each ends the intent: the notice for a tempo
outside the limits says to switch Tap to start on again and tap a different tempo (a
further tap would not count, and with a program TAP is off until the switch is on). A tempo change that is still being committed
is settled first, so the tapped tempo is the last word, and an intent that is
cancelled while its own change is being checked drops that change before it is
published (no Undo step, nothing saved). What the start waits for (that tempo change,
the edit queue) is cancelled with the intent, so Stop gives Start and the switch back
at once. Stop, a start or pause by hand, a tempo
chosen by hand (the intent ends before that change is committed, so a commit that
fails after publishing cannot leave it listening), a rebuilt or left page, a change of
output, a power transition and
closing invalidate the intent before it publishes or plays. While it is armed a
WinRT `DeviceWatcher` for audio render endpoints cancels it when the selected
endpoint is removed or updated, without waiting for a refresh; reconnecting never
arms it again, and when Windows cannot watch the outputs it stays off with a reason
while a manual Start still works. The watcher lives only as long as the intent.
This is not a phase-aligned or acoustically calibrated start.

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
