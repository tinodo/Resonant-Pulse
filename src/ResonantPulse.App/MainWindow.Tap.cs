using System.Diagnostics;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Audio;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private readonly TapInSession _tapIn = new();
    private TextBlock? _tapStatus;
    private Button? _tapInArm;
    private Button? _tapInCancel;
    private NumberBox? _tapInCount;
    private NumberBox? _playTempo;
    private bool _isSynchronizingTempo;
    private long _tempoInputVersion;
    private int _tapInRequiredTaps = 4;

    private void ArmTapIn(int requiredTaps)
    {
        if (_isBusy || _isLocked || _transport.State is not (TransportState.Stopped or TransportState.Completed))
        {
            ShowNotice(_isLocked ? "Locked" : "TapInStopFirst");
            return;
        }
        if (!HasSelectedOutput) { PromptForOutput(); return; }
        if (!_isOutputMonitoring) { ShowNotice("TapInMonitoringUnavailable"); return; }
        _tapIn.Arm(requiredTaps, _preferences.OutputId);
        _tapInRequiredTaps = requiredTaps;
        _transportIntent++;
        _transport.Stop();
        _hasPendingEdits = false;
        _tap.Reset();
        RefreshSnapshot();
    }

    private void CancelTapIn()
    {
        if (!_tapIn.IsArmed && !_tapIn.IsPending) return;
        if (_tapIn.IsPending)
        {
            _transportIntent++;
            _transport.Stop();
        }
        _tapIn.Cancel();
        RefreshSnapshot();
    }

    private async Task TapTempoAsync()
    {
        if (_isBusy || _isClosed) return;
        var timestamp = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        if (_tapIn.IsArmed)
        {
            if (_isLocked || !HasSelectedOutput || _transport.State != TransportState.Stopped)
            {
                CancelTapIn();
                ShowNotice(_isLocked ? "Locked" : "TapInCancelled");
                return;
            }
            if (_tapIn.Tap(timestamp) && _tapIn.Bpm is { } bpm) await StartTapInAsync(bpm);
            else RefreshSnapshot();
            return;
        }
        var estimate = _tap.Tap(timestamp);
        if (_playTempo is { } input && TempoEdits.TryApplyTap(input.Value, estimate, out var proposed))
            input.Value = proposed;
        RefreshSnapshot();
    }

    private async Task StartTapInAsync(double tempo)
    {
        var generation = _tapIn.Generation;
        var intent = _transportIntent;
        var revision = _documentRevision;
        var inputVersion = _tempoInputVersion;
        var output = _preferences.OutputId;
        PulseDocument? committed = null;
        _isBusy = true;
        RefreshSnapshot();
        try
        {
            await CommitAsync(document =>
            {
                if (!_tapIn.IsCurrent(generation) || intent != _transportIntent || _isClosed)
                    throw new OperationCanceledException();
                if (_documentRevision != revision) throw new EditorRevisionChangedException();
                TempoEdits.ApplyTapIn(document, tempo);
                committed = document;
            }, editorChange: _document.Program.Count == 0 ? EditorChangeKind.GlobalTempoOnly : EditorChangeKind.Document);
            if (committed is null || !_tapIn.IsCurrent(generation) || intent != _transportIntent || _isClosed)
                return;
            if (!ReferenceEquals(_document, committed))
                throw new EditorRevisionChangedException();
            if (_transport.State != TransportState.Stopped || !HasSelectedOutput || output != _preferences.OutputId)
            {
                ShowNotice("TapInCancelled");
                return;
            }
            InvalidateTiming();
            var startRevision = _documentRevision;
            await _transport.StartAsync(committed, output);
            _hasPendingEdits = startRevision != _documentRevision;
        }
        finally
        {
            _tapIn.Finish(generation);
            _isBusy = false;
            if (TempoEdits.ShouldSynchronizeTapIn(inputVersion, _tempoInputVersion))
                SynchronizePlayTempo();
            RefreshSnapshot();
        }
    }

    private void SynchronizePlayTempo()
    {
        if (_playTempo is not { } input) return;
        _isSynchronizingTempo = true;
        try { input.Value = _document.Settings.Tempo; }
        finally { _isSynchronizingTempo = false; }
    }

    private void RefreshTapControls(TransportState state)
    {
        if (_tapInArm is not null)
            _tapInArm.IsEnabled = !_isBusy && !_isLocked && HasSelectedOutput && _isOutputMonitoring &&
                state is TransportState.Stopped or TransportState.Completed;
        if (_tapInCancel is not null) _tapInCancel.IsEnabled = _tapIn.IsArmed || _tapIn.IsPending;
        if (_tapInCount is not null) _tapInCount.IsEnabled = !_isBusy;
        if (_tapStatus is null) return;
        var estimate = _tapIn.Bpm?.ToString("0.0", Text.Culture) ?? Text.Get("TapEstimateUnknown");
        _tapStatus.Text = _tapIn.IsPending ? Text.Get("TapInPreparing") :
            _tapIn.IsArmed ? Text.Format(_tapIn.LastTapRejected ? "TapInRejected" : "TapInProgress",
                _tapIn.ValidTaps, _tapIn.RequiredTaps, estimate) :
            HasSelectedOutput && !_isOutputMonitoring ? Text.Get("TapInMonitoringUnavailable") :
            _tap.CurrentBpm is { } bpm ? Text.Format("TapEstimate", bpm, _tap.AcceptedIntervals, _tap.RejectedTaps) :
            Text.Get("TapReady");
        AutomationProperties.SetName(_tapStatus, _tapStatus.Text);
        AutomationProperties.SetItemStatus(_tapStatus, _tapIn.IsPending ? "preparing" :
            _tapIn.IsArmed ? $"armed:{_tapIn.ValidTaps}:{_tapIn.RequiredTaps}:{_tapIn.LastTapRejected}" : "idle");
    }
}
