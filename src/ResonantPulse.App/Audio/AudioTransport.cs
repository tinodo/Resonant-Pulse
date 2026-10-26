using System.Runtime.InteropServices;
using System.Diagnostics;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using WinRT;

namespace ResonantPulse.App.Audio;

public enum TransportState { Stopped, Playing, Paused, Faulted, Completed }

/// <summary>
/// A consistent view of the transport. Two timelines are involved (see <see cref="PlaybackRevision"/>):
/// <c>RenderFrame</c> and <c>ProcessedFrame</c> are ABSOLUTE device frames (they never rewind and are unaffected by
/// live edits; elapsed time = ProcessedFrame / sample rate). <c>Revision</c> is the revision that is AUDIBLE at
/// <c>ProcessedFrame</c> (the previous one until a queued splice has actually been heard), and <c>Plan</c>,
/// <c>MusicalFrame</c> and <c>PositionFrame</c> are always expressed in that revision's OWN timeline, so
/// <c>Plan.PositionAtFrame(MusicalFrame)</c> and the Initial/Body loop arithmetic on <c>PositionFrame</c> need no
/// correction. Whenever a revision's own frames meet <c>ProcessedFrame</c> or <c>RenderFrame</c>, add
/// <see cref="Shift"/> to the absolute value (<see cref="OwnProcessedFrame"/>).
/// </summary>
public sealed record TransportSnapshot(
    TransportState State, long RenderFrame, long ProcessedFrame,
    long MusicalFrame, RenderPlan? Plan, PlaybackRevision? Revision, bool IsMuted)
{
    /// <summary>Own frame = absolute frame + Shift for <see cref="Revision"/>; zero without one.</summary>
    public long Shift => Revision?.Shift ?? 0;

    /// <summary><see cref="ProcessedFrame"/> in the own timeline of <see cref="Revision"/>.</summary>
    public long OwnProcessedFrame => ProcessedFrame + Shift;

    /// <summary>The processed position in the own timeline of <see cref="Revision"/>, held at the last rendered frame once Completed.</summary>
    public long PositionFrame => Math.Max(0, PlaybackDeadline.PositionFrame(OwnProcessedFrame, Revision?.EndFrame,
        State == TransportState.Completed));
}

/// <summary>
/// One AudioGraph per window, shared by every destination. AudioGraph owns
/// scheduling; the UI only reads snapshots. ProcessedFrame is graph completion,
/// NOT a calibrated acoustic presentation position. Stop uses graph flush
/// semantics, Pause retains the next-render cursor; downstream audio can drain.
/// Live edits (<see cref="UpdateLiveAsync"/>) are prepared off the callback and applied inside it: sound/accents at
/// the next quantum, everything else spliced in at the next pulse boundary, sample accurately and without a gap.
/// </summary>
public sealed class AudioTransport : IDisposable
{
    // IID from the Windows IMemoryBufferByteAccess ABI, not IBufferByteAccess.
    private static readonly Guid MemoryBufferByteAccessId = new("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");
    private readonly SemaphoreSlim _lifecycle = new(1);
    private AudioGraph? _graph;
    private AudioFrameInputNode? _input;
    private AudioDeviceOutputNode? _output;
    private LiveTimeline? _timeline;
    private readonly LiveEditQueue _live = new();
    // The document the transport converges to (latest accepted live request); guarded by _lifecycle.
    private PulseDocument? _liveTarget;
    private readonly float[] _tailMix = new float[32768 * 2];
    private long _renderFrame;
    private long _processedFrame;
    private int _state;
    private int _mute;
    private float _gain = 0.5f;
    private int _fault;
    private long _lastQuantumTimestamp;
    private int _generation;
    private int _soundSessionGeneration;
    private bool _isDisposed;

    public event EventHandler<Exception>? Failed;
    public TransportState State => (TransportState)Volatile.Read(ref _state);
    public bool IsMuted { get => Volatile.Read(ref _mute) != 0; set => Volatile.Write(ref _mute, value ? 1 : 0); }
    public float Gain { get => Volatile.Read(ref _gain); set => Volatile.Write(ref _gain, Math.Clamp(value, 0, 1)); }

    public static async Task<IReadOnlyList<OutputDevice>> EnumerateOutputsAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
        return devices.Where(device => device.IsEnabled)
            .Select(device => new OutputDevice(device.Id, device.Name)).ToArray();
    }

    /// <summary>Returns the current Windows default playback endpoint identifier.</summary>
    /// <returns>The endpoint identifier, or an empty string when no default exists.</returns>
    public static string DefaultOutputId() =>
        MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);

    /// <summary>
    /// Applies the live-eligible settings (Tempo, meter, BPM unit, subdivision, swing, groups, accents, sound, gap bars,
    /// blind gaps) of <paramref name="requested"/>, which must carry the COMPLETE live settings of the editor, to the
    /// running revision without restarting. Count-in, practice limits, master gain and the document structure
    /// (lanes, program) stay as started. Sound/accent-only changes switch at the next audio quantum on the same
    /// musical clock (also for finite programs, exactly like the former sound-only update); anything
    /// else is spliced in at the next primary pulse (see <c>LiveSplice</c>) with the bar position continued when the bar
    /// structure is unchanged, otherwise restarted at bar 1 beat 1. A finite program cannot follow structural
    /// settings: they return <see cref="LiveEditOutcome.RequiresRestart"/> (the sound part of that request is still applied).
    /// Only the newest unapplied request survives. Pause keeps a queued request; Stop discards it.
    /// Validation/compile errors of the merged document propagate.
    /// </summary>
    /// <exception cref="InvalidOperationException">The session was stopped or replaced while the request was prepared.</exception>
    public async Task<LiveEditOutcome> UpdateLiveAsync(PulseSettings requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        await _lifecycle.WaitAsync();
        try
        {
            var active = Volatile.Read(ref _timeline)?.Active;
            if (active is null || State is TransportState.Stopped or TransportState.Completed or TransportState.Faulted)
                return LiveEditOutcome.NotPlaying;
            var session = Volatile.Read(ref _soundSessionGeneration);
            var decision = LiveSettingsEdits.Decide(active, _liveTarget, requested);
            if (!decision.Publish) return decision.Outcome;
            var merged = decision.Merged;
            var rate = active.SampleRate;
            var prepared = await Task.Run(() => new PlaybackRevision(merged, rate));
            if (session != Volatile.Read(ref _soundSessionGeneration) || _isDisposed)
                throw new InvalidOperationException(Text.Get("SoundSessionChanged"));
            if (LiveSettingsEdits.Classify(active.Document.Settings, merged.Settings) != LiveChange.Structural &&
                !active.HasSameClock(prepared))
                throw new InvalidOperationException("A live sound edit must preserve the musical clock and stop boundary.");
            _live.Post(prepared);
            _liveTarget = merged;
            return decision.Outcome;
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>
    /// Source-compatible entry point for the former sound/accent-only update. It now simply forwards to
    /// <see cref="UpdateLiveAsync"/> (so structural settings carried by <paramref name="requested"/> are applied live
    /// too, except during a finite program, where only the sound part follows exactly as before); the outcome is ignored.
    /// </summary>
    public async Task UpdateSoundAsync(PulseSettings requested) => await UpdateLiveAsync(requested);

    public async Task StartAsync(PulseDocument document, string outputId)
    {
        await _lifecycle.WaitAsync();
        try
        {
            var request = Volatile.Read(ref _generation);
            void CheckCurrent()
            {
                if (_isDisposed || request != Volatile.Read(ref _generation))
                    throw new OperationCanceledException();
            }
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (string.IsNullOrWhiteSpace(outputId)) throw new InvalidOperationException(Text.Get("SelectOutput"));
            if (State == TransportState.Paused && _graph is not null)
            {
                Volatile.Write(ref _state, (int)TransportState.Playing);
                Interlocked.Exchange(ref _lastQuantumTimestamp, Stopwatch.GetTimestamp());
                _graph.Start();
                return;
            }
            var timeline = Volatile.Read(ref _timeline);
            var retained = State is TransportState.Paused or TransportState.Faulted
                ? timeline : null;
            if (retained is null) Interlocked.Increment(ref _soundSessionGeneration);
            var retainedState = retained is null ? null : new RetainedPlayback(retained.Active,
                retained.Previous, _live.Pending, retained.SwitchFrame, Interlocked.Read(ref _processedFrame));
            ReleaseGraph();
            var device = await DeviceInformation.CreateFromIdAsync(outputId);
            CheckCurrent();
            if (device is null || !device.IsEnabled) throw new InvalidOperationException(Text.Get("SelectOutput"));
            var format = AudioEncodingProperties.CreatePcm(48000, 2, 32);
            format.Subtype = MediaEncodingSubtypes.Float;
            var settings = new AudioGraphSettings(AudioRenderCategory.Media)
            {
                PrimaryRenderDevice = device,
                EncodingProperties = format,
                QuantumSizeSelectionMode = QuantumSizeSelectionMode.LowestLatency
            };
            var result = await AudioGraph.CreateAsync(settings);
            if (_isDisposed || request != Volatile.Read(ref _generation))
            {
                result.Graph?.Dispose();
                throw new OperationCanceledException();
            }
            if (result.Status != AudioGraphCreationStatus.Success)
            {
                result.Graph?.Dispose();
                throw new InvalidOperationException($"{Text.Get("AudioError")} ({result.Status})");
            }
            _graph = result.Graph;
            var outputResult = await _graph.CreateDeviceOutputNodeAsync();
            CheckCurrent();
            if (outputResult.Status != AudioDeviceNodeCreationStatus.Success)
                throw new InvalidOperationException($"{Text.Get("AudioError")} ({outputResult.Status})");
            _output = outputResult.DeviceOutputNode;
            var inputFormat = AudioEncodingProperties.CreatePcm(_graph.EncodingProperties.SampleRate, 2, 32);
            inputFormat.Subtype = MediaEncodingSubtypes.Float;
            // Compile/preload off the UI thread and entirely before the graph starts.
            var captured = LocalWorkspace.Clone(retained?.Active.Document ?? document);
            var resumed = await Task.Run(() => retainedState?.Rebuild((int)inputFormat.SampleRate) ??
                new RetainedPlayback(new PlaybackRevision(captured, (int)inputFormat.SampleRate), null, null, 0, 0));
            CheckCurrent();
            _live.Clear();
            if (resumed.Pending is { } pending) _live.Post(pending);
            if (retained is null) _liveTarget = null;
            Volatile.Write(ref _timeline, new LiveTimeline(resumed.Active, resumed.Previous, resumed.SwitchFrame));
            _input = _graph.CreateFrameInputNode(inputFormat);
            _input.AddOutgoingConnection(_output);
            _input.QuantumStarted += OnQuantumStarted;
            _input.AudioFrameCompleted += OnFrameCompleted;
            _graph.UnrecoverableErrorOccurred += OnUnrecoverableError;
            _renderFrame = resumed.ProcessedFrame;
            _processedFrame = _renderFrame;
            _fault = 0;
            _lastQuantumTimestamp = Stopwatch.GetTimestamp();
            Volatile.Write(ref _state, (int)TransportState.Playing);
            _input.Start();
            _output.Start();
            _graph.Start();
        }
        catch (OperationCanceledException)
        {
            ReleaseGraph();
            Volatile.Write(ref _state, (int)TransportState.Stopped);
            Volatile.Write(ref _timeline, null);
            _live.Clear();
            _liveTarget = null;
            Interlocked.Exchange(ref _renderFrame, 0);
            Interlocked.Exchange(ref _processedFrame, 0);
            throw;
        }
        catch
        {
            ReleaseGraph();
            Volatile.Write(ref _state, (int)TransportState.Faulted);
            throw;
        }
        finally { _lifecycle.Release(); }
    }

    public void Pause()
    {
        if (State != TransportState.Playing) return;
        Volatile.Write(ref _state, (int)TransportState.Paused);
        _graph?.Stop();
    }

    public void SuspendForEndpointChange()
    {
        Interlocked.Increment(ref _generation);
        if (State == TransportState.Playing) Pause();
        // Reopening is explicit on the next Start. Keep the retained cursor;
        // never resume the still-open previous endpoint after a picker change.
        ReleaseGraph();
    }

    public void Stop()
    {
        Interlocked.Increment(ref _soundSessionGeneration);
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _state, (int)TransportState.Stopped);
        ReleaseGraph();
        Interlocked.Exchange(ref _renderFrame, 0);
        Interlocked.Exchange(ref _processedFrame, 0);
        Volatile.Write(ref _timeline, null);
        _live.Clear();
        _liveTarget = null;
    }

    public TransportSnapshot Snapshot()
    {
        if (State == TransportState.Playing && Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastQuantumTimestamp)).TotalSeconds > 4)
            ReportFault(new InvalidOperationException(Text.Get("AudioError")));
        // Processed first: a splice is published before the frame that contains it can complete, so the
        // timeline read afterwards is never older than what the processed cursor has already heard.
        var processed = Interlocked.Read(ref _processedFrame);
        var timeline = Volatile.Read(ref _timeline);
        var state = State;
        var render = Interlocked.Read(ref _renderFrame);
        if (timeline is null) return new(state, render, processed, processed, null, null, IsMuted);
        var position = LiveSplice.Present(timeline, processed, state == TransportState.Completed);
        return new(state, render, processed, position.MusicalFrame, position.Plan, position.Revision, IsMuted);
    }

    private void OnQuantumStarted(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
    {
        if (State != TransportState.Playing || !ReferenceEquals(sender, _input) || args.RequiredSamples <= 0) return;
        try
        {
            Interlocked.Exchange(ref _lastQuantumTimestamp, Stopwatch.GetTimestamp());
            FillQuantum(sender, args.RequiredSamples);
        }
        catch (Exception exception) { ReportFault(exception); }
    }

    private unsafe void FillQuantum(AudioFrameInputNode sender, int frameCount)
    {
        if (frameCount > 32768) throw new InvalidOperationException(Text.Get("QuantumLimit"));
        var published = Volatile.Read(ref _timeline);
        if (published is null) return;
        var byteCount = checked((uint)(frameCount * 2 * sizeof(float)));
        var cursor = Interlocked.Read(ref _renderFrame);
        var gain = Gain;
        var muted = IsMuted;
        // Retires old tails, takes the newest live request and splices at the armed pulse boundary. A concurrent
        // Stop() (null timeline) wins over this publication.
        var timeline = _live.Advance(published, cursor, frameCount, Interlocked.Read(ref _processedFrame));
        if (!ReferenceEquals(timeline, published) &&
            !ReferenceEquals(Interlocked.CompareExchange(ref _timeline, timeline, published), published)) return;
        var rate = timeline.Active.SampleRate;
        var frame = new AudioFrame(byteCount)
        {
            RelativeTime = TimeSpan.FromSeconds((double)cursor / rate),
            Duration = TimeSpan.FromSeconds((double)frameCount / rate)
        };
        try
        {
            using (var buffer = frame.LockBuffer(AudioBufferAccessMode.Write))
            using (var reference = buffer.CreateReference())
            // Query the real WinRT ABI. Never CLR-cast a CsWinRT projected
            // IMemoryBufferReference to a hand-declared [ComImport] interface:
            // that is the source of InvalidCastException in the old adapter.
            using (var referenceAbi = MarshalInspectable<IMemoryBufferReference>.CreateMarshaler(reference))
            {
                var iid = MemoryBufferByteAccessId;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(referenceAbi.ThisPtr, in iid, out var access));
                try
                {
                    byte* bytes = null;
                    uint capacity = 0;
                    var vtable = *(void***)access;
                    var getBuffer = (delegate* unmanaged[Stdcall]<nint, byte**, uint*, int>)vtable[3];
                    Marshal.ThrowExceptionForHR(getBuffer(access, &bytes, &capacity));
                    if (bytes is null || capacity < byteCount)
                        throw new InvalidOperationException(Text.Get("AudioError"));
                    LiveSplice.Render(new Span<float>(bytes, frameCount * 2), cursor, frameCount, timeline,
                        gain, muted, _tailMix);
                    buffer.Length = byteCount;
                }
                finally { Marshal.Release(access); }
            }
            sender.AddFrame(frame);
            Interlocked.Exchange(ref _renderFrame, cursor + frameCount);
        }
        catch { frame.Dispose(); throw; }
    }

    private void OnFrameCompleted(AudioFrameInputNode sender, AudioFrameCompletedEventArgs args)
    {
        try
        {
            var timeline = Volatile.Read(ref _timeline);
            if (timeline is not null && ReferenceEquals(sender, _input))
            {
                var end = (long)Math.Round(((args.Frame.RelativeTime ?? TimeSpan.Zero) + (args.Frame.Duration ?? TimeSpan.Zero)).TotalSeconds * timeline.Active.SampleRate);
                var limit = timeline.EndFrameAt(end);
                Interlocked.Exchange(ref _processedFrame, limit is { } bound ? Math.Min(end, bound) : end);
                if (limit is { } deadline && end >= deadline && State == TransportState.Playing)
                {
                    Volatile.Write(ref _state, (int)TransportState.Completed);
                    // The UI observes Completed and stops the graph, not an audio
                    // scheduling timer. Subsequent callbacks submit no new frames.
                }
            }
        }
        finally { args.Frame.Dispose(); }
    }

    public void FinishCompleted()
    {
        if (State == TransportState.Completed) _graph?.Stop();
    }

    private void OnUnrecoverableError(AudioGraph sender, AudioGraphUnrecoverableErrorOccurredEventArgs args) =>
        ReportFault(new InvalidOperationException($"{Text.Get("AudioError")} ({args.Error})"));

    private void ReportFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _fault, 1) != 0) return;
        Volatile.Write(ref _state, (int)TransportState.Faulted);
        // No UI calls, disk I/O, stopping/disposal or blocking locks in callback.
        // Window handles Failed on its dispatcher and stops the failed graph.
        Failed?.Invoke(this, exception);
    }

    public void AcknowledgeFault()
    {
        _graph?.Stop();
        // Keep graph/cursor for diagnosis, never silently create a new endpoint.
    }

    private void ReleaseGraph()
    {
        var graph = _graph;
        var input = _input;
        _graph = null;
        _input = null;
        _output = null;
        if (graph is null) return;
        if (input is not null)
        {
            input.QuantumStarted -= OnQuantumStarted;
            input.AudioFrameCompleted -= OnFrameCompleted;
        }
        graph.UnrecoverableErrorOccurred -= OnUnrecoverableError;
        try { graph.Stop(); }
        finally { graph.Dispose(); }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        try { Stop(); }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            Diagnostics.Write("Audio graph close failed", exception);
        }
        // An in-flight StartAsync still owns the semaphore and will release it
        // after observing the generation change. Do not dispose under that await.
    }
}
