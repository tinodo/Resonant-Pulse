using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace ResonantPulse.App.Controls;

internal static class ThemeBrushes
{
    private static readonly AccessibilitySettings Accessibility = new();

    internal static Brush Resolve(FrameworkElement owner, string key)
    {
        var theme = Accessibility.HighContrast ? "HighContrast" :
            owner.ActualTheme == ElementTheme.Light ? "Light" : "Dark";
        foreach (var merged in Application.Current.Resources.MergedDictionaries)
            if (merged.ThemeDictionaries.TryGetValue(theme, out var themed) &&
                themed is ResourceDictionary dictionary && dictionary.TryGetValue(key, out var value))
                return (Brush)value;
        return (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    internal static void Track(FrameworkElement owner, Action refresh)
    {
        var isLoaded = false;
        void ContrastChanged(AccessibilitySettings sender, object args) =>
            owner.DispatcherQueue.TryEnqueue(() =>
            {
                if (isLoaded) refresh();
            });
        owner.Loaded += (_, _) =>
        {
            if (isLoaded) return;
            isLoaded = true;
            Accessibility.HighContrastChanged += ContrastChanged;
            refresh();
        };
        owner.Unloaded += (_, _) =>
        {
            if (!isLoaded) return;
            isLoaded = false;
            Accessibility.HighContrastChanged -= ContrastChanged;
        };
        owner.ActualThemeChanged += (_, _) =>
        {
            if (isLoaded) refresh();
        };
    }
}
