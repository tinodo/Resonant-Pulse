using System.Runtime.InteropServices;
using System.Numerics;
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

public sealed record OutputDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

public enum TransportState { Stopped, Playing, Paused, Faulted, Completed }

public sealed record TransportSnapshot(
    TransportState State, long RenderFrame, long ProcessedFrame,
    long MusicalFrame, RenderPlan? Plan, PlaybackRevision? Revision, bool IsMuted);

/// <summary>Immutable, prepared off the real-time callback. No UI document is
/// shared with the renderer. Initial count-in is never repeated by the loop.</summary>
public sealed class PlaybackRevision
{
    public PulseDocument Document { get; }
    public RenderPlan Initial { get; }
    public RenderPlan Body { get; }
    public bool IsFinite => Document.Program.Count > 0;
    public long CountInFrames => Initial.CountInFrames;

    public PlaybackRevision(PulseDocument source, int sampleRate)
    {
        Document = LocalWorkspace.Clone(source);
        var bars = LoopBars(Document);
        Initial = ScheduleCompiler.Compile(Document, sampleRate, bars, true);
        Body = ScheduleCompiler.Compile(Document, sampleRate, bars, false);
        if (Initial.TotalFrames <= 0 || Body.TotalFrames <= 0)
            throw new ArgumentException("The compiled rhythm has no duration.");
        // Warm any lazily initialized renderer caches here, never in QuantumStarted.
        Span<float> warmup = stackalloc float[2];
        PcmRenderer.Fill(warmup, Initial, 0, 2, 0, false, false);
        PcmRenderer.Fill(warmup, Body, 0, 2, 0, false, false);
    }

    private static int LoopBars(PulseDocument document)
    {
        if (document.Program.Count > 0) return 16;
        var bar = new Rational(4 * document.Settings.BeatsPerBar, document.Settings.BeatDenominator);
        BigInteger multiple = 1;
        void Include(Rational cycle)
        {
            var required = (cycle / bar).Numerator;
            multiple = multiple / BigInteger.GreatestCommonDivisor(multiple, required) * required;
            if (multiple > 4096) throw new ArgumentException(Text.Get("CycleLimit"));
        }
        Include(Rational.FromDouble(document.Settings.BeatUnitQuarters));
        foreach (var lane in document.Lanes) Include(Rational.FromDouble(lane.CycleQuarters));
        var gaps = document.Settings.AudibleBars + document.Settings.SilentBars;
        if (gaps > 0) Include(bar * gaps);
        // A complete supercycle prevents a 3-quarter lane being restarted at
        // an arbitrary 16-bar seam. Deterministic realization repeats per loop.
        var count = (int)multiple;
        return count * Math.Max(1, (16 + count - 1) / count);
    }
}

/// <summary>
/// One AudioGraph per window, shared by every destination. AudioGraph owns
/// scheduling; the UI only reads snapshots. ProcessedFrame is graph completion,
/// NOT a calibrated acoustic presentation position. Stop uses graph flush
/// semantics, Pause retains the next-render cursor; downstream audio can drain.
/// </summary>
public sealed class AudioTransport : IDisposable
{
    // IID from the Windows IMemoryBufferByteAccess ABI, not IBufferByteAccess.
    private static readonly Guid MemoryBufferByteAccessId = new("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");
    private readonly SemaphoreSlim _lifecycle = new(1);
    private AudioGraph? _graph;
    private AudioFrameInputNode? _input;
    private AudioDeviceOutputNode? _output;
    private PlaybackRevision? _revision;
    private long _renderFrame;
    private long _processedFrame;
    private int _state;
    private int _mute;
    private float _gain = 0.5f;
    private int _fault;
    private long _lastQuantumTimestamp;
    private int _generation;
    private bool _isDisposed;

    public event EventHandler<Exception>? Failed;
    public TransportState State => (TransportState)Volatile.Read(ref _state);
    public bool IsMuted { get => Volatile.Read(ref _mute) != 0; set => Volatile.Write(ref _mute, value ? 1 : 0); }
    public float Gain { get => Volatile.Read(ref _gain); set => Volatile.Write(ref _gain, Math.Clamp(value, 0, 1)); }

    public static async Task<IReadOnlyList<OutputDevice>> EnumerateOutputsAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
        return devices.Select(device => new OutputDevice(device.Id, device.Name)).ToArray();
    }

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
            var retained = State is TransportState.Paused or TransportState.Faulted ? _revision : null;
            var retainedRender = Interlocked.Read(ref _processedFrame);
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
            var captured = LocalWorkspace.Clone(retained?.Document ?? document);
            _revision = await Task.Run(() => new PlaybackRevision(captured, (int)inputFormat.SampleRate));
            CheckCurrent();
            _input = _graph.CreateFrameInputNode(inputFormat);
            _input.AddOutgoingConnection(_output);
            _input.QuantumStarted += OnQuantumStarted;
            _input.AudioFrameCompleted += OnFrameCompleted;
            _graph.UnrecoverableErrorOccurred += OnUnrecoverableError;
            _renderFrame = retained is null ? 0 : (long)Math.Round((double)retainedRender * inputFormat.SampleRate / retained.Initial.SampleRate);
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
            _revision = null;
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
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _state, (int)TransportState.Stopped);
        ReleaseGraph();
        Interlocked.Exchange(ref _renderFrame, 0);
        Interlocked.Exchange(ref _processedFrame, 0);
        _revision = null;
    }

    public TransportSnapshot Snapshot()
    {
        if (State == TransportState.Playing && Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastQuantumTimestamp)).TotalSeconds > 4)
            ReportFault(new InvalidOperationException(Text.Get("AudioError")));
        var revision = _revision;
        var processed = Interlocked.Read(ref _processedFrame);
        var musical = processed;
        RenderPlan? plan = revision?.Initial;
        if (revision is not null && processed >= revision.Initial.TotalFrames)
        {
            plan = revision.Body;
            musical = revision.IsFinite ? plan.TotalFrames :
                (processed - revision.Initial.TotalFrames) % plan.TotalFrames;
        }
        return new(State, Interlocked.Read(ref _renderFrame), processed, musical, plan, revision, IsMuted);
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
        var revision = _revision;
        if (revision is null) return;
        var byteCount = checked((uint)(frameCount * 2 * sizeof(float)));
        var cursor = Interlocked.Read(ref _renderFrame);
        var frame = new AudioFrame(byteCount)
        {
            RelativeTime = TimeSpan.FromSeconds((double)cursor / revision.Initial.SampleRate),
            Duration = TimeSpan.FromSeconds((double)frameCount / revision.Initial.SampleRate)
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
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(referenceAbi.ThisPtr, ref iid, out var access));
                try
                {
                    byte* bytes = null;
                    uint capacity = 0;
                    var vtable = *(void***)access;
                    var getBuffer = (delegate* unmanaged[Stdcall]<nint, byte**, uint*, int>)vtable[3];
                    Marshal.ThrowExceptionForHR(getBuffer(access, &bytes, &capacity));
                    if (bytes is null || capacity < byteCount)
                        throw new InvalidOperationException(Text.Get("AudioError"));
                    var samples = new Span<float>(bytes, frameCount * 2);
                    samples.Clear();
                    var written = 0;
                    while (written < frameCount)
                    {
                        var absolute = cursor + written;
                        var initial = absolute < revision.Initial.TotalFrames;
                        if (!initial && revision.IsFinite) break;
                        var plan = initial ? revision.Initial : revision.Body;
                        var local = initial ? absolute : (absolute - revision.Initial.TotalFrames) % plan.TotalFrames;
                        var count = (int)Math.Min(Math.Min(frameCount - written, plan.TotalFrames - local), 32768);
                        // Looping body uses renderer's seam-tail mixing. Initial
                        // finite intro is rendered once, never recurring count-in.
                        var renderAt = initial ? local : absolute - revision.Initial.TotalFrames + plan.TotalFrames;
                        PcmRenderer.Fill(samples.Slice(written * 2, count * 2), plan, renderAt, 2, Gain, IsMuted, !initial);
                        written += count;
                    }
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
            var revision = _revision;
            if (revision is not null && ReferenceEquals(sender, _input))
            {
                var end = (long)Math.Round(((args.Frame.RelativeTime ?? TimeSpan.Zero) + (args.Frame.Duration ?? TimeSpan.Zero)).TotalSeconds * revision.Initial.SampleRate);
                Interlocked.Exchange(ref _processedFrame, end);
                if (revision.IsFinite && end >= revision.Initial.TotalFrames && State == TransportState.Playing)
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
