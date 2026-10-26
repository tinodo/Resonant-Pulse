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

    /// <summary>True when a strip in the top-right corner stays clear of the dial's ring, with a small margin.</summary>
    internal static bool CornerIsClear(double width, double height, double stripWidth, double stripHeight = StripHeight)
    {
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
    /// True when the notice (an InfoBar: title with its action button on a second row, about 107 tall, or about 130 when the
    /// title wraps on a page narrower than 480) can sit above the tempo controls and still leave the dial at least
    /// <see cref="MinDialWithNotice"/> tall. The heights of the other rows were measured on the real page: about 205 when
    /// the settings row sits beside the beat strip, and about 290 or 330 once it stacks.
    /// </summary>
    internal static bool NoticeFits(double pageWidth, double pageHeight)
    {
        var noticeHeight = pageWidth >= 480 ? 107d : 130d;
        var otherRows = pageWidth >= 620 ? 205d : pageWidth >= 480 ? 290d : 330d;
        return pageHeight - otherRows - noticeHeight >= MinDialWithNotice;
    }
}
