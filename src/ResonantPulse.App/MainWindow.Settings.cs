using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Audio;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private UIElement BuildSettings()
    {
        var language = SelectChoices("Language", "Language", _preferences.Language,
        [
            new("", Text.Get("System")), new("en", "English"), new("nl", "Nederlands"),
            new("de", "Deutsch"), new("fr", "Français"), new("es", "Español")
        ]);
        language.SelectionChanged += async (_, _) => await GuardAsync(async () =>
        {
            _preferences.Language = Selected(language);
            Text.Select(_preferences.Language);
            _lastTimingReport = "";
            RebuildNavigation();
            Navigation.SelectedItem = Navigation.SettingsItem;
            LocalizeTransport();
            BuildPage();
            await _workspace.SavePreferencesAsync(_preferences);
        });
        var theme = SelectChoices("Theme", "Theme", _preferences.Theme,
        [
            new("Default", Text.Get("System")), new("Dark", Text.Get("Dark")), new("Light", Text.Get("Light"))
        ]);
        theme.SelectionChanged += async (_, _) => await GuardAsync(async () =>
        {
            _preferences.Theme = Selected(theme);
            ApplyTheme();
            await _workspace.SavePreferencesAsync(_preferences);
        });
        var reduced = Check("ReducedMotion", "ReducedMotion", _preferences.IsReducedMotion);
        async void SaveMotion(object sender, RoutedEventArgs args) => await GuardAsync(async () =>
        {
            _preferences.IsReducedMotion = reduced.IsChecked == true;
            await _workspace.SavePreferencesAsync(_preferences);
        });
        reduced.Checked += SaveMotion;
        reduced.Unchecked += SaveMotion;
        var outputs = new ComboBox { Header = Text.Get("Output"), HorizontalAlignment = HorizontalAlignment.Stretch };
        Identify(outputs, "AudioOutputPicker", Text.Get("Output"));
        var loadingDevices = false;
        async Task RefreshOutputsAsync()
        {
            loadingDevices = true;
            try
            {
                var devices = await AudioTransport.EnumerateOutputsAsync();
                outputs.ItemsSource = devices;
                outputs.SelectedItem = devices.FirstOrDefault(device => device.Id == _preferences.OutputId);
            }
            finally { loadingDevices = false; }
        }
        outputs.SelectionChanged += async (_, _) =>
        {
            if (loadingDevices || outputs.SelectedItem is not OutputDevice device) return;
            await GuardAsync(async () =>
            {
                InvalidateTiming();
                _transport.SuspendForEndpointChange();
                _preferences.OutputId = device.Id;
                _preferences.OutputName = device.Name;
                _isOutputMissing = false;
                await _workspace.SavePreferencesAsync(_preferences);
                LocalizeTransport();
                RefreshSnapshot();
            });
        };
        _ = GuardAsync(RefreshOutputsAsync);
        var bars = Number("ExportBars", "ExportBars", _preferences.ExportBars, 1, 1024);
        var panel = Column(Heading(Text.Get("Settings")), Flow(language, theme), reduced,
            outputs, Copy("SelectOutput"),
            ActionButton("RefreshDevices", "RefreshAudioOutputs", RefreshOutputsAsync),
            bars,
            ActionButton("Apply", "SaveExportBars", async () =>
            {
                _preferences.ExportBars = Integer(bars);
                await _workspace.SavePreferencesAsync(_preferences);
                ShowNotice("Saved");
            }),
            Heading(Text.Get("Capabilities")), Copy("IntegrationGates"), Copy("RemainingMusic"), Copy("WorkflowGaps"), Copy("RemainingMedia"),
            Copy("ValidationGate"), Copy("Privacy"), Copy("KeyboardHelp"));
        return Scroll(panel);
    }
}
