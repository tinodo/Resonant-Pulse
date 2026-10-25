using Microsoft.UI.Xaml;
using ResonantPulse.App.Audio;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private IReadOnlyList<OutputDevice> _outputDevices = [];
    private bool _isRefreshingOutputs;
    private int _outputRefreshVersion;

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
        LocalizeTransport();
        RefreshSnapshot();
        if (changed) await _workspace.SavePreferencesAsync(_preferences);
    }

    private async Task SelectOutputAsync(OutputDevice device)
    {
        if (_preferences.OutputId == device.Id && !_isOutputMissing) return;
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
