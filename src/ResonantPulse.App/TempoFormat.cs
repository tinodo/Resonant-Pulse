using System.Globalization;

namespace ResonantPulse.App;

internal static class TempoFormat
{
    /// <summary>Whole tempos read as "120"; only a genuinely fractional tempo (tap tempo) shows a decimal.</summary>
    internal static string Format(double tempo, CultureInfo culture) =>
        Math.Abs(tempo - Math.Round(tempo)) < 0.05
            ? Math.Round(tempo).ToString("0", culture)
            : tempo.ToString("0.0", culture);
}
