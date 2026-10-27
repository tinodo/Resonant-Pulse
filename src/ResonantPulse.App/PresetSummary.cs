using System.Globalization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

/// <summary>The meter and tempo line under a ready-made rhythm: "6/8 · 80 BPM", or "4/4 · 80–112 BPM" for a program.</summary>
internal static class PresetSummary
{
    internal static string Meta(PulseDocument document, CultureInfo culture)
    {
        var settings = document.Settings;
        var first = document.Program.FirstOrDefault();
        var beats = first?.BeatsPerBar ?? settings.BeatsPerBar;
        var denominator = first?.BeatDenominator ?? settings.BeatDenominator;
        string tempo;
        if (document.Program.Count == 0) tempo = TempoFormat.Format(settings.Tempo, culture);
        else
        {
            var low = document.Program.Min(step => Math.Min(step.StartTempo, step.EndTempo));
            var high = document.Program.Max(step => Math.Max(step.StartTempo, step.EndTempo));
            tempo = low == high ? TempoFormat.Format(low, culture)
                : $"{TempoFormat.Format(low, culture)}–{TempoFormat.Format(high, culture)}";
        }
        return string.Create(culture, $"{beats}/{denominator} · {tempo} BPM");
    }
}
