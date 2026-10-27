namespace ResonantPulse.App;

/// <summary>
/// Sizing and breakpoint rules for the page kit. A field is as wide as the longest thing it can hold, never as wide as its
/// window, so a box for "100" stays short on a 4K monitor. Everything here is pure so it can be tested without a UI.
/// </summary>
internal static class FormMetrics
{
    /// <summary>Pages never grow wider than this; on a large monitor the content is centred.</summary>
    internal const double PageMaxWidth = 960;
    /// <summary>From this page width up, two cards sit side by side.</summary>
    internal const double TwoColumnBreakpoint = 900;
    /// <summary>Below this card width a setting's control moves under its text.</summary>
    internal const double StackedRowBreakpoint = 560;
    /// <summary>Text boxes (names, search) never grow wider than this.</summary>
    internal const double TextMaxWidth = 420;
    internal const double SelectMinWidth = 120;
    internal const double SelectMaxWidth = 320;
    /// <summary>Space a drop-down box needs besides its text: the padding and the chevron.</summary>
    internal const double SelectChrome = 64;

    // A NumberBox with inline steppers: the text area plus the clear button that appears on focus, two 36 DIP stepper buttons
    // and the box's own padding. Without room for the clear button the digits vanish while the box has focus.
    private const double NumberChrome = 124;
    private const double DigitWidth = 9;
    private const double MinimumDigits = 3;

    /// <summary>Digits needed to show every value between the limits, including a minus sign.</summary>
    internal static int Digits(double minimum, double maximum)
    {
        static int Count(double value) => (int)Math.Floor(Math.Log10(Math.Max(1, Math.Abs(Math.Truncate(value))))) + 1;
        var digits = Math.Max(Count(minimum), Count(maximum));
        return minimum < 0 ? digits + 1 : digits;
    }

    /// <summary>Width of a number box with steppers that fits its largest value; at least three digits wide.</summary>
    internal static double NumberWidth(double minimum, double maximum, int fractionDigits = 0, double textScale = 1)
    {
        var characters = Math.Max(MinimumDigits, Digits(minimum, maximum)) + (fractionDigits > 0 ? fractionDigits + 1 : 0);
        return Math.Ceiling(NumberChrome + characters * DigitWidth * Math.Max(1, textScale));
    }

    /// <summary>Width of a drop-down for options whose longest text measures <paramref name="longestText"/> DIP.</summary>
    internal static double SelectWidth(double longestText) =>
        Math.Clamp(Math.Ceiling(longestText + SelectChrome), SelectMinWidth, SelectMaxWidth);

    /// <summary>
    /// The width a field gets: its own preferred width, but never more than the room it was offered, so a field cannot be
    /// clipped by a narrow window or a large text size. Unbounded room (a measuring pass) gives the preferred width.
    /// </summary>
    internal static double Fit(double preferred, double available) =>
        double.IsFinite(available) ? Math.Min(preferred, Math.Max(0, available)) : preferred;

    /// <summary>
    /// True when a number box got the room its inline steppers need. Below that it uses compact steppers, which appear over
    /// the box only while it has focus, so the digits keep their room.
    /// </summary>
    internal static bool SteppersInline(double given, double preferred) => given >= preferred - 0.5;

    /// <summary>
    /// The width of each of <paramref name="count"/> equal footer buttons: <paramref name="preferred"/> when they fit in
    /// <paramref name="panelWidth"/>, otherwise as wide as the panel allows, so a narrow panel never clips the pair.
    /// </summary>
    internal static double FooterCell(double panelWidth, double spacing, int count, double preferred) =>
        Math.Min(preferred, Math.Max(0, Math.Floor((panelWidth - spacing * Math.Max(0, count - 1)) / Math.Max(1, count))));

    /// <summary>The number of columns a two-pane page uses: one below the breakpoint, two from it.</summary>
    internal static int PageColumns(double width) => width >= TwoColumnBreakpoint ? 2 : 1;

    /// <summary>True when a setting's control sits beside its text, false when it moves under it.</summary>
    internal static bool SettingRowSideBySide(double width) => width >= StackedRowBreakpoint;
}

/// <summary>Where a left-to-right wrapping layout starts each new row.</summary>
internal static class WrapRows
{
    /// <summary>
    /// The index at which each row starts. A child that does not fit begins a new row; one wider than the row sits alone.
    /// An infinite or unknown width never wraps.
    /// </summary>
    internal static IReadOnlyList<int> Starts(IReadOnlyList<double> widths, double available, double spacing)
    {
        var starts = new List<int>();
        var wraps = double.IsFinite(available) && available > 0;
        var used = 0d;
        for (var index = 0; index < widths.Count; index++)
        {
            if (starts.Count == 0)
            {
                starts.Add(index);
                used = widths[index];
                continue;
            }
            var needed = used + spacing + widths[index];
            if (wraps && needed > available)
            {
                starts.Add(index);
                used = widths[index];
            }
            else used = needed;
        }
        return starts;
    }
}
