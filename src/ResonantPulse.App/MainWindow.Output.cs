using Microsoft.UI.Xaml;
using ResonantPulse.App.Audio;
using ResonantPulse.App.Localization;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private IReadOnlyList<OutputDevice> _outputDevices = [];
    private bool _isRefreshingOutputs;
    private int _outputRefreshVersion;
    private DeviceWatcher? _outputWatcher;
    private bool _isOutputMonitoring;

    private void EnsureOutputMonitoring()
    {
        if (_outputWatcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) return;
        StopOutputMonitoring();
        var watcher = DeviceInformation.CreateWatcher(MediaDevice.GetAudioRenderSelector());
        _outputWatcher = watcher;
        watcher.Added += OutputAdded;
        watcher.Removed += OutputRemovedOrUpdated;
        watcher.Updated += OutputRemovedOrUpdated;
        watcher.Stopped += OutputMonitoringStopped;
        watcher.Start();
        _isOutputMonitoring = true;
    }

    private void StopOutputMonitoring()
    {
        var watcher = _outputWatcher;
        _outputWatcher = null;
        _isOutputMonitoring = false;
        if (watcher is null) return;
        watcher.Added -= OutputAdded;
        watcher.Removed -= OutputRemovedOrUpdated;
        watcher.Updated -= OutputRemovedOrUpdated;
        watcher.Stopped -= OutputMonitoringStopped;
        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            watcher.Stop();
    }

    private void OutputAdded(DeviceWatcher sender, DeviceInformation device) =>
        QueueOutputChange(sender, device.Id, false);

    private void OutputRemovedOrUpdated(DeviceWatcher sender, DeviceInformationUpdate device) =>
        QueueOutputChange(sender, device.Id, true);

    private void QueueOutputChange(DeviceWatcher sender, string id, bool invalidatesIntent) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed || !ReferenceEquals(sender, _outputWatcher)) return;
            if (invalidatesIntent && _tapIn.IsAffectedByOutput(id))
            {
                CancelTapIn();
                ShowNotice("TapInOutputChanged");
            }
            _ = GuardAsync(RefreshOutputsAsync);
        });

    private void OutputMonitoringStopped(DeviceWatcher sender, object args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed || !ReferenceEquals(sender, _outputWatcher)) return;
            _isOutputMonitoring = false;
            CancelTapIn();
            ShowError(new InvalidOperationException(Text.Get("TapInMonitoringUnavailable")), "AudioError");
            RefreshSnapshot();
        });

    private async Task RefreshOutputsAsync()
    {
        var request = ++_outputRefreshVersion;
        var devices = await AudioTransport.EnumerateOutputsAsync();
        if (_isClosed || request != _outputRefreshVersion) return;
        var defaultId = devices.Count > 0 && string.IsNullOrWhiteSpace(_preferences.OutputId)
            ? AudioTransport.DefaultOutputId() : "";
        var selected = AudioOutputSelection.Resolve(_preferences.OutputId, defaultId, devices);
        var changed = selected is not null &&
            (_preferences.OutputId != selected.Id || _preferences.OutputName != selected.Name);
        _outputDevices = devices;
        _isOutputMissing = !string.IsNullOrWhiteSpace(_preferences.OutputId) && selected is null;
        if (selected is null) CancelTapIn();
        if (selected is not null)
        {
            _preferences.OutputId = selected.Id;
            _preferences.OutputName = selected.Name;
        }
        _isRefreshingOutputs = true;
        try
        {
            OutputPicker.ItemsSource = devices;
            OutputPicker.SelectedItem = selected;
        }
        finally { _isRefreshingOutputs = false; }
        EnsureOutputMonitoring();
        LocalizeTransport();
        RefreshSnapshot();
        if (changed) await _workspace.SavePreferencesAsync(_preferences);
    }

    private async Task SelectOutputAsync(OutputDevice device)
    {
        if (_preferences.OutputId == device.Id && !_isOutputMissing) return;
        CancelTapIn();
        InvalidateTiming();
        _transportIntent++;
        _transport.SuspendForEndpointChange();
        _preferences.OutputId = device.Id;
        _preferences.OutputName = device.Name;
        _isOutputMissing = false;
        _isRefreshingOutputs = true;
        try { OutputPicker.SelectedItem = _outputDevices.FirstOrDefault(item => item.Id == device.Id); }
        finally { _isRefreshingOutputs = false; }
        LocalizeTransport();
        RefreshSnapshot();
        await _workspace.SavePreferencesAsync(_preferences);
    }

    private bool HasSelectedOutput =>
        !_isOutputMissing && _outputDevices.Any(device =>
            string.Equals(device.Id, _preferences.OutputId, StringComparison.OrdinalIgnoreCase));

    private void PromptForOutput()
    {
        ShowNotice("ChooseOutput");
        OutputPicker.Focus(FocusState.Programmatic);
        OutputPicker.IsDropDownOpen = true;
    }
}
