using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

internal static class TempoGuide
{
    private static readonly (string Key, double Minimum, double Maximum)[] Bands =
    [
        ("TempoLarghissimo", 1, 25),
        ("TempoGrave", 25, 45),
        ("TempoLargo", 40, 60),
        ("TempoLarghetto", 60, 66),
        ("TempoAdagio", 66, 76),
        ("TempoAndante", 76, 108),
        ("TempoModerato", 108, 120),
        ("TempoAllegro", 120, 156),
        ("TempoVivace", 156, 176),
        ("TempoPresto", 168, 200),
        ("TempoPrestissimo", 200, 1000)
    ];

    public static IReadOnlyList<string> MarkingsAt(double bpm)
    {
        if (!IsValid(bpm)) throw new ArgumentOutOfRangeException(nameof(bpm));
        return Bands.Where(band => bpm >= band.Minimum && bpm <= band.Maximum)
            .Select(band => band.Key).ToArray();
    }

    public static string Format(double bpm) => IsValid(bpm)
        ? Text.Format("TempoGuideValue", string.Join(" / ", MarkingsAt(bpm).Select(Text.Get)))
        : Text.Get("TempoGuideInput");

    private static bool IsValid(double bpm) => double.IsFinite(bpm) && bpm is >= 1 and <= 1000;
}
