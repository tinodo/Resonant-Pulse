using System.Globalization;

namespace ResonantPulse.App;

/// <summary>How the saved practice limits read in the Timer panel: "off" or a short value, never a unit that needs a plural.</summary>
internal static class PracticeFormat
{
    /// <summary>"45 s", "5:00", "1:30:00", or <paramref name="off"/> for no limit.</summary>
    internal static string Time(int seconds, string off, CultureInfo culture)
    {
        if (seconds <= 0) return off;
        if (seconds < 60) return string.Create(culture, $"{seconds} s");
        if (seconds < 3600) return string.Create(culture, $"{seconds / 60}:{seconds % 60:00}");
        return string.Create(culture, $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}");
    }

    /// <summary>A whole number with the language's digit grouping, or <paramref name="off"/> for no limit.</summary>
    internal static string Count(int value, string off, CultureInfo culture) =>
        value <= 0 ? off : value.ToString("N0", culture);
}
