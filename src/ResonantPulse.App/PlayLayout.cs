namespace ResonantPulse.App;

/// <summary>Where the Practice, Project and Help strip sits on the Play page.</summary>
internal enum RailPlacement
{
    /// <summary>In the free top-right corner beside the dial, over the hero area, costing no height.</summary>
    Corner,
    /// <summary>In a row of its own above the dial, for windows too narrow for the corner.</summary>
    Row
}

internal readonly record struct RailPlan(RailPlacement Placement, bool ShowLabels);

/// <summary>
/// Geometry rules for the Play page that depend on the dial. The dial is a square whose ring is a circle, so the corner
/// beside it is free at most window sizes; the page-tools strip uses that corner, and a notice only appears when it would
/// not shrink the dial below a usable size.
/// </summary>
internal static class PlayLayout
{
    /// <summary>Height of every button in the strip.</summary>
    internal const double StripHeight = 44;
    internal const double MaxDialSize = 560;
    /// <summary>The dial is never allowed to drop below this to make room for a notice.</summary>
    internal const double MinDialWithNotice = 200;

    // PulseInstrument draws in a 440 x 440 box. The ring and its ticks are a circle centred 200 down from the top, and the
    // outermost tick ends 191 from that centre.
    private const double DialBox = 440;
    private const double RingCenterY = 200;
    private const double RingOuterRadius = 191;
    private const double Clearance = 8;

    internal static double DialSize(double width, double height) => Math.Clamp(Math.Min(width, height), 0, MaxDialSize);

    /// <summary>
    /// Prefers labels in the corner, then icons in the corner, then a row of its own (with labels when they fit).
    /// <paramref name="width"/> and <paramref name="height"/> are the space for the dial when the strip takes none of it.
    /// The answer depends only on that space, so it cannot flip back and forth when taking a row shrinks the dial.
    /// </summary>
    internal static RailPlan Choose(double width, double height, double labelledWidth, double iconsWidth,
        double stripHeight = StripHeight)
    {
        if (width <= 0 || height <= 0) return new RailPlan(RailPlacement.Corner, true);
        if (CornerIsClear(width, height, labelledWidth, stripHeight)) return new RailPlan(RailPlacement.Corner, true);
        if (CornerIsClear(width, height, iconsWidth, stripHeight)) return new RailPlan(RailPlacement.Corner, false);
        return new RailPlan(RailPlacement.Row, labelledWidth <= width);
    }

    /// <summary>
    /// True when a strip in the top-right corner fits the page's width and stays clear of the dial's ring, with a small
    /// margin. A strip wider than the page would run off its left edge however far away the ring is.
    /// </summary>
    internal static bool CornerIsClear(double width, double height, double stripWidth, double stripHeight = StripHeight)
    {
        if (stripWidth > width) return false;
        var size = DialSize(width, height);
        var scale = size / DialBox;
        var centerX = width / 2;
        var centerY = (height - size) / 2 + RingCenterY * scale;
        var radius = RingOuterRadius * scale + Clearance;
        // Distance from the ring's centre to the nearest point of the strip's rectangle.
        var dx = Math.Max(width - stripWidth - centerX, 0);
        var dy = Math.Max(centerY - stripHeight, 0);
        return dx * dx + dy * dy >= radius * radius;
    }

    /// <summary>
    /// True when a cached measurement of the strip no longer matches its real width. That happens when Windows text size
    /// changes: the labels resize but the page does not, so no page event announces it. A difference within one
    /// device-independent pixel is layout rounding, not a change.
    /// </summary>
    internal static bool StripWidthIsStale(double cachedWidth, double actualWidth) =>
        cachedWidth > 0 && Math.Abs(actualWidth - cachedWidth) > 1;

    /// <summary>
    /// True when the notice can sit above the tempo controls and still leave the dial at least
    /// <see cref="MinDialWithNotice"/> across. Every input is measured on the real page, so enlarged Windows text (taller
    /// rows, a taller notice) is accounted for: <paramref name="otherRows"/> is everything on the page except the dial and
    /// the notice (strip row, tempo buttons, settings row and the gaps between rows), and <paramref name="noticeRow"/> is
    /// the notice plus its gap. None of them depends on whether the notice is shown, so the answer cannot oscillate.
    /// </summary>
    internal static bool NoticeFits(double pageWidth, double pageHeight, double otherRows, double noticeRow) =>
        DialSize(pageWidth, pageHeight - otherRows - noticeRow) >= MinDialWithNotice;
}
