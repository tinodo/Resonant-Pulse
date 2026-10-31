using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private UIElement BuildSettings()
    {
        var language = Dropdown("Language", "Language", _preferences.Language,
        [
            new("", Text.Get("System")), new("en", "English"), new("nl", "Nederlands"),
            new("de", "Deutsch"), new("fr", "Français"), new("es", "Español")
        ]);
        language.SelectionChanged += async (_, _) => await GuardAsync(async () =>
        {
            _preferences.Language = Selected(language);
            Text.Select(_preferences.Language);
            // The result is shown again in the new language: a finished check is not lost because the words around it changed.
            _lastTimingReport = _lastTimingSession is { } finished ? TimingExport.ReportText(finished.Report) : "";
            RebuildNavigation();
            Navigation.SelectedItem = Navigation.SettingsItem;
            LocalizeTransport();
            BuildPage();
            await _workspace.SavePreferencesAsync(_preferences);
        });
        var theme = Dropdown("Theme", "Theme", _preferences.Theme,
        [
            new("Default", Text.Get("System")), new("Dark", Text.Get("Dark")), new("Light", Text.Get("Light"))
        ]);
        theme.SelectionChanged += async (_, _) => await GuardAsync(async () =>
        {
            _preferences.Theme = Selected(theme);
            ApplyTheme();
            await _workspace.SavePreferencesAsync(_preferences);
        });
        // The presentation loop reads the preference on every frame, so the change is seen at once.
        var motion = Toggle("ReducedMotion", "ReducedMotion", _preferences.IsReducedMotion);
        motion.Toggled += async (_, _) => await GuardAsync(async () =>
        {
            _preferences.IsReducedMotion = motion.IsOn;
            await _workspace.SavePreferencesAsync(_preferences);
        });

        var appearance = Card("Appearance", SettingRows(
            SettingRow("Language", null, language),
            SettingRow("Theme", null, theme),
            SettingRow("ReducedMotion", "ReducedMotionHelp", motion)), id: "AppearanceCard");

        var version = new TextBlock
        {
            Text = Text.Format("VersionLine", AppVersion()), Style = ResonantStyle("BodyStyle"), FontSize = 14,
            IsTextSelectionEnabled = true
        };
        AutomationProperties.SetAutomationId(version, "AppVersion");
        var about = Card("AboutApp", new StackPanel { Spacing = 8, Children = { version, Copy("Privacy"), Copy("TimingHonesty") } },
            id: "AboutCard");
        return PageScaffold("Settings", null, null, appearance, about);
    }

    /// <summary>The release number the build was stamped with, without any source-revision suffix.</summary>
    private static string AppVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "";
        var revision = version.IndexOf('+');
        return revision >= 0 ? version[..revision] : version;
    }
}
