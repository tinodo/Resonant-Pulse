using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class PlayLayoutTests
{
    // Measured widths of the Timer, Project and Help strip in English at 100% text scale (labelled, icons only).
    private const double Labelled = 273;
    private const double Icons = 140;

    [TestMethod]
    public void AWideWindowKeepsTheLabelsInTheCornerBesideTheDial()
    {
        Assert.AreEqual(new RailPlan(RailPlacement.Corner, true), PlayLayout.Choose(984, 470, Labelled, Icons));
        Assert.AreEqual(new RailPlan(RailPlacement.Corner, true), PlayLayout.Choose(2000, 1000, Labelled, Icons));
    }

    [TestMethod]
    public void WhenLabelsWouldTouchTheDialTheCornerKeepsOnlyIcons()
    {
        Assert.AreEqual(new RailPlan(RailPlacement.Corner, false), PlayLayout.Choose(560, 330, Labelled, Icons));
    }

    [TestMethod]
    public void WhenEvenIconsWouldTouchTheDialTheStripTakesARowOfItsOwn()
    {
        Assert.AreEqual(new RailPlan(RailPlacement.Row, true), PlayLayout.Choose(300, 330, Labelled, Icons));
        Assert.AreEqual(new RailPlan(RailPlacement.Row, false), PlayLayout.Choose(240, 330, Labelled, Icons));
    }

    [TestMethod]
    public void AnUnmeasuredWindowGivesTheDefaultPlan()
    {
        Assert.AreEqual(new RailPlan(RailPlacement.Corner, true), PlayLayout.Choose(0, 0, Labelled, Icons));
    }

    [TestMethod]
    public void TheDialIsTheSmallerSideCappedAtItsMaximum()
    {
        Assert.AreEqual(330, PlayLayout.DialSize(560, 330));
        Assert.AreEqual(PlayLayout.MaxDialSize, PlayLayout.DialSize(2000, 1000));
        Assert.AreEqual(0, PlayLayout.DialSize(-5, 100));
    }

    [TestMethod]
    public void AStripWiderThanThePageNeverGoesInTheCornerEvenWhenTheRingIsFarAway()
    {
        // Tall and narrow: the ring sits far below the corner, but a 330 wide strip cannot fit a 258 wide page.
        Assert.AreEqual(new RailPlan(RailPlacement.Corner, false), PlayLayout.Choose(258, 700, 330, Icons));
        Assert.IsFalse(PlayLayout.CornerIsClear(258, 700, 330));
        // Not even the icons fit: a row of its own, without labels.
        Assert.AreEqual(new RailPlan(RailPlacement.Row, false), PlayLayout.Choose(120, 700, 330, Icons));
    }

    [TestMethod]
    public void ACornerPlacementNeverCoversTheRingAtAnyWindowSizeOrLabelWidth()
    {
        // Independent of the closed-form distance: walk the ring's outermost tick circle and test every point.
        foreach (var labelled in new[] { 220d, Labelled, 330d, 420d })
        foreach (var width in Enumerable.Range(10, 80).Select(step => step * 20d))
        foreach (var height in Enumerable.Range(10, 50).Select(step => step * 20d))
        {
            var plan = PlayLayout.Choose(width, height, labelled, Icons);
            if (plan.Placement != RailPlacement.Corner) continue;
            var stripWidth = plan.ShowLabels ? labelled : Icons;
            Assert.IsTrue(stripWidth <= width, $"{width}x{height}: a {stripWidth} wide strip does not fit the page");
            var size = PlayLayout.DialSize(width, height);
            var scale = size / 440;
            var centerX = width / 2;
            var centerY = (height - size) / 2 + 200 * scale;
            for (var degrees = 0; degrees < 360; degrees++)
            {
                var angle = degrees * Math.PI / 180;
                var x = centerX + Math.Cos(angle) * 191 * scale;
                var y = centerY + Math.Sin(angle) * 191 * scale;
                var inside = x >= width - stripWidth && x <= width && y >= 0 && y <= PlayLayout.StripHeight;
                Assert.IsFalse(inside, $"{width}x{height}, strip {stripWidth}, ring point at {degrees} degrees");
            }
        }
    }

    [TestMethod]
    [DataRow(0d, 273d, false)]     // nothing measured yet
    [DataRow(273d, 273d, false)]
    [DataRow(273d, 273.6d, false)] // layout rounding
    [DataRow(273d, 272.1d, false)]
    [DataRow(273d, 341d, true)]    // Windows text size went up: the labels grew
    [DataRow(273d, 230d, true)]    // and down
    public void AStripWidthIsStaleOnlyWhenItReallyChanged(double cached, double actual, bool stale)
    {
        Assert.AreEqual(stale, PlayLayout.StripWidthIsStale(cached, actual));
    }

    [TestMethod]
    // Measured at 100% text size: tempo buttons 52 + settings row about 119 + three 10 DIP gaps = 201 beside the dial, and
    // the notice (two rows) about 109 plus its 6 DIP gap = 115.
    [DataRow(978d, 600d, 201d, 115d, true)]   // the default window
    [DataRow(938d, 554d, 201d, 115d, true)]   // 1000 x 720
    [DataRow(738d, 434d, 201d, 115d, false)]  // 800 x 600: the dial would drop below 200
    [DataRow(738d, 516d, 201d, 115d, true)]   // exactly 200 left for the dial
    [DataRow(738d, 515.9d, 201d, 115d, false)]
    [DataRow(978d, 600d, 260d, 150d, false)]  // enlarged Windows text: taller rows and notice, so it steps aside
    [DataRow(978d, 700d, 260d, 150d, true)]   // and returns when the window has room again
    [DataRow(180d, 900d, 201d, 115d, false)]  // the page is narrower than the dial's minimum
    [DataRow(458d, 700d, 290d, 136d, true)]   // narrow and stacked, but tall enough
    public void TheNoticeOnlyAppearsWhenTheDialKeepsAUsableSize(double pageWidth, double pageHeight, double otherRows,
        double noticeRow, bool fits)
    {
        Assert.AreEqual(fits, PlayLayout.NoticeFits(pageWidth, pageHeight, otherRows, noticeRow));
    }

    [TestMethod]
    public void TheNoticeNeverFlipsBackAndForthAsRoomOrHeightsChange()
    {
        // Monotonic in every input: more room can only show it, and taller rows or a taller notice can only hide it.
        foreach (var width in new[] { 358d, 458d, 738d, 978d })
        foreach (var otherRows in new[] { 150d, 201d, 290d, 330d })
        foreach (var noticeRow in new[] { 60d, 115d, 136d, 180d })
        {
            var shown = false;
            for (var height = 100d; height <= 1400; height += 5)
            {
                var fits = PlayLayout.NoticeFits(width, height, otherRows, noticeRow);
                Assert.IsFalse(shown && !fits, $"{width} wide, rows {otherRows}/{noticeRow}: shown lower but hidden at {height}");
                shown |= fits;
                if (!fits) continue;
                Assert.IsTrue(PlayLayout.NoticeFits(width, height, otherRows - 10, noticeRow), "shorter rows must still fit");
                Assert.IsTrue(PlayLayout.NoticeFits(width, height, otherRows, noticeRow - 10), "a shorter notice must still fit");
                Assert.IsFalse(PlayLayout.NoticeFits(width, height - 5000, otherRows, noticeRow), "no room can never fit");
            }
        }
    }
}
