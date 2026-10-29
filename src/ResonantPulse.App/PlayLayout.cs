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

/// <summary>Where the "Tap to start" switch sits relative to slower, TAP and faster.</summary>
internal enum TapStartPlacement
{
    /// <summary>To their right, with its words, while they stay centred under the dial.</summary>
    Labelled,
    /// <summary>To their right as an icon alone, when only that fits there; they stay centred under the dial.</summary>
    Compact,
    /// <summary>Right against them as an icon alone, the whole group centred under the dial: for a window too narrow to keep them centred.</summary>
    Appended,
    /// <summary>On a row of its own below them, in a window too narrow for even the icon beside them.</summary>
    OwnRow
}

/// <summary>
/// Geometry rules for the Play page that depend on the dial. The dial is a square whose ring is a circle, so the corner
/// beside it is free at most window sizes; the page-tools strip uses that corner, and a notice only appears when it would
/// not shrink the dial below a usable size. The beat strip's cell sizing lives here too, so it can be tested.
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
    /// rows, a taller notice) is accounted for: <paramref name="otherRows"/> is everything on the page except the dial, the
    /// notice and the page-tools strip's own row (tempo buttons, settings row and the gaps between rows), and
    /// <paramref name="noticeRow"/> is the notice plus its gap. The strip is placed from the room the dial has, and the
    /// notice takes some of that room, so showing the notice can push the strip into a row of its own; that row is
    /// subtracted here, from the placement the strip will actually get. None of the inputs depends on whether the notice is
    /// shown, so the answer cannot oscillate.
    /// </summary>
    internal static bool NoticeFits(double pageWidth, double pageHeight, double otherRows, double noticeRow,
        double labelledWidth, double iconsWidth, double stripHeight = StripHeight)
    {
        var room = pageHeight - otherRows - noticeRow;
        var stripRow = Choose(pageWidth, room, labelledWidth, iconsWidth, stripHeight).Placement == RailPlacement.Row
            ? stripHeight : 0;
        return DialSize(pageWidth, room - stripRow) >= MinDialWithNotice;
    }

    /// <summary>
    /// Height of the notice's action button: the framework's own button height, so the notice is one row. An InfoBar's
    /// panel (microsoft-ui-xaml, InfoBarPanel.cpp) puts the title and the action on one row only while the tallest child,
    /// with its horizontal margin, stays within the bar's minimum height. The bar is 48 DIP high and the framework leaves
    /// 8 DIP above an action, so a button taller than 40 DIP goes to a second row. The 44 DIP every other button here has
    /// did that in preview.8, and the notice was twice as tall as it needed to be.
    /// </summary>
    internal const double NoticeButtonHeight = 32;

    /// <summary>Space before each additive group of the beat strip, after the first.</summary>
    internal const double StripGroupGap = 10;

    /// <summary>Space between the tempo controls and the "Tap to start" switch beside them.</summary>
    internal const double TapStartGap = 24;

    /// <summary>Space between faster and an icon-only switch that is appended to them: the spacing the three controls have among themselves.</summary>
    internal const double TapStartSpacing = 14;

    /// <summary>
    /// Where the "Tap to start" switch goes. Slower, TAP and faster stay centred under the dial, so the switch may only use the
    /// room each side of them has: the words when they fit there, the icon alone when only that fits. In a window too narrow
    /// for that the icon is appended to them and the group is centred (they move a little to the left, which keeps the dial's
    /// height), and only when even that does not fit does the switch take a row of its own, below them, which costs the dial
    /// some height. A row or a cluster that has not been measured yet gives the default, which is corrected as soon as it is.
    /// </summary>
    internal static TapStartPlacement ChooseTapStart(double rowWidth, double clusterWidth, double labelledWidth, double compactWidth)
    {
        if (rowWidth <= 0 || clusterWidth <= 0) return TapStartPlacement.Labelled;
        var room = (rowWidth - clusterWidth) / 2 - TapStartGap;
        if (labelledWidth <= room) return TapStartPlacement.Labelled;
        if (compactWidth <= room) return TapStartPlacement.Compact;
        return clusterWidth + TapStartSpacing + compactWidth <= rowWidth ? TapStartPlacement.Appended : TapStartPlacement.OwnRow;
    }

    /// <summary>The narrowest and widest a beat's cell gets. Narrower than this stops being a comfortable target.</summary>
    internal const double StripMinCell = 32;
    internal const double StripMaxCell = 56;

    /// <summary>
    /// Room kept under a strip that scrolls. A ScrollViewer draws its horizontal scroll bar over the bottom of its content,
    /// which is where the beat numbers are; the bar is 12 DIP thick while the pointer is over it, so this room is where it
    /// sits instead of on the numbers.
    /// </summary>
    internal const double StripScrollBarRoom = 14;

    /// <summary>
    /// Width of one beat's cell. The group gaps come out of the room first: they are space the cells cannot use. Counting
    /// them as cell width made 12/8 (three gaps) overflow its room by about 20 DIP and show a scroll bar although every
    /// beat fits at the narrowest cell.
    /// </summary>
    internal static double StripCellWidth(double available, int beats, int groupGaps) =>
        Math.Clamp((available - groupGaps * StripGroupGap) / Math.Max(1, beats), StripMinCell, StripMaxCell);

    /// <summary>True when even the narrowest cells and the group gaps are wider than the room, so the strip must scroll.</summary>
    internal static bool StripNeedsScroll(double available, int beats, int groupGaps) =>
        beats * StripMinCell + groupGaps * StripGroupGap > available;
}
