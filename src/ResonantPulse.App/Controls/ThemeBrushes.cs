using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace ResonantPulse.App.Controls;

internal static class ThemeBrushes
{
    private static readonly AccessibilitySettings Accessibility = new();

    /// <summary>True while a Windows Contrast theme is active.</summary>
    internal static bool IsHighContrast => Accessibility.HighContrast;

    internal static Brush Resolve(FrameworkElement owner, string key)
    {
        var theme = IsHighContrast ? "HighContrast" :
            owner.ActualTheme == ElementTheme.Light ? "Light" : "Dark";
        foreach (var merged in Application.Current.Resources.MergedDictionaries)
            if (merged.ThemeDictionaries.TryGetValue(theme, out var themed) &&
                themed is ResourceDictionary dictionary && dictionary.TryGetValue(key, out var value))
                return (Brush)value;
        return (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }
}
