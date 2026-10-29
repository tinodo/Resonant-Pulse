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
    // Measured at 100% text size: tempo buttons 52 + settings row about 119 + three 10 DIP gaps = 201 beside the dial. The
    // notice on two rows (a narrow window or enlarged text) is about 98 plus its 6 DIP gap = 104; preview.8 always had two
    // rows, 109 + 6 = 115, which these rows keep as a tall notice. The strip is 273 wide with labels, 140 with icons only.
    [DataRow(978d, 600d, 201d, 115d, true)]   // the default window
    [DataRow(938d, 554d, 201d, 115d, true)]   // 1000 x 720
    [DataRow(738d, 434d, 201d, 115d, false)]  // 800 x 600: the dial would drop below 200
    [DataRow(738d, 516d, 201d, 115d, true)]   // exactly 200 left for the dial
    [DataRow(738d, 515.9d, 201d, 115d, false)]
    [DataRow(978d, 600d, 260d, 150d, false)]  // enlarged Windows text: taller rows and notice, so it steps aside
    [DataRow(978d, 700d, 260d, 150d, true)]   // and returns when the window has room again
    [DataRow(180d, 900d, 201d, 115d, false)]  // the page is narrower than the dial's minimum
    [DataRow(458d, 700d, 290d, 136d, true)]   // narrow and stacked: the strip takes a row, and 230 is still left
    [DataRow(280d, 665d, 290d, 136d, false)]  // the notice pushes the strip into its own row: 239 budgeted, only 195 left
    [DataRow(280d, 669d, 290d, 136d, false)]  // 243 budgeted, 199 left
    [DataRow(280d, 670d, 290d, 136d, true)]   // 244 budgeted, exactly 200 left once the strip has its row
    // The notice on one row: about 50 DIP high plus its 6 DIP gap = 56.
    [DataRow(978d, 600d, 201d, 56d, true)]    // the default window: the dial keeps 343
    [DataRow(938d, 554d, 201d, 56d, true)]    // 1000 x 720: 297, where the two-row notice left 238
    [DataRow(738d, 434d, 201d, 56d, false)]   // 800 x 600: the dial would drop below 200
    [DataRow(738d, 457d, 201d, 56d, true)]    // exactly 200 left for the dial
    [DataRow(738d, 456.9d, 201d, 56d, false)]
    public void TheNoticeOnlyAppearsWhenTheDialKeepsAUsableSize(double pageWidth, double pageHeight, double otherRows,
        double noticeRow, bool fits)
    {
        Assert.AreEqual(fits, PlayLayout.NoticeFits(pageWidth, pageHeight, otherRows, noticeRow, Labelled, Icons));
    }

    [TestMethod]
    public void ShowingTheNoticeCanMoveTheStripIntoARowAndThatRowIsPartOfTheBudget()
    {
        // Without the notice the strip sits in the corner; with it the dial has less room and the strip needs its own row.
        const double width = 280, pageHeight = 665, otherRows = 290, noticeRow = 136;
        Assert.AreEqual(RailPlacement.Corner, PlayLayout.Choose(width, pageHeight - otherRows, Labelled, Icons).Placement);
        Assert.AreEqual(RailPlacement.Row, PlayLayout.Choose(width, pageHeight - otherRows - noticeRow, Labelled, Icons).Placement);
        Assert.IsFalse(PlayLayout.NoticeFits(width, pageHeight, otherRows, noticeRow, Labelled, Icons));
    }

    [TestMethod]
    public void WheneverTheNoticeIsShownTheDialKeepsItsMinimumAfterTheStripIsPlaced()
    {
        // Soundness across many sizes and row heights (including enlarged text): replay the layout the way the page does.
        foreach (var width in new[] { 180d, 280d, 358d, 458d, 540d, 738d, 978d })
        foreach (var otherRows in new[] { 150d, 201d, 260d, 290d, 330d })
        foreach (var noticeRow in new[] { 60d, 115d, 136d, 180d })
        foreach (var height in Enumerable.Range(40, 260).Select(step => step * 5d))
        {
            if (!PlayLayout.NoticeFits(width, height, otherRows, noticeRow, Labelled, Icons)) continue;
            var room = height - otherRows - noticeRow;
            var stripRow = PlayLayout.Choose(width, room, Labelled, Icons).Placement == RailPlacement.Row ? PlayLayout.StripHeight : 0;
            Assert.IsTrue(PlayLayout.DialSize(width, room - stripRow) >= PlayLayout.MinDialWithNotice,
                $"{width}x{height}, rows {otherRows}/{noticeRow}: the dial would drop below its minimum");
        }
    }

    [TestMethod]
    public void ThereIsRoomForTheNoticeWheneverThePageIsTallEnough()
    {
        foreach (var width in new[] { 358d, 458d, 738d, 978d })
            Assert.IsTrue(PlayLayout.NoticeFits(width, 5000, 330, 180, Labelled, Icons), $"{width} wide, very tall page");
        Assert.IsFalse(PlayLayout.NoticeFits(978, 100, 201, 115, Labelled, Icons), "no room at all");
    }

    // The beat strip. Its row is at most 720 wide: less the meter chip (96), the sound box (152) and two 20 wide gaps leaves
    // 432, and 8 of that is kept free, so the strip has 424 to itself beside the other controls.
    private const double StripRoom = 424;

    [TestMethod]
    public void TwelveEightFitsBesideTheMeterAndSoundControlsWithoutAScrollBar()
    {
        const int beats = 12, gaps = 3; // 3 + 3 + 3 + 3
        Assert.IsFalse(PlayLayout.StripNeedsScroll(StripRoom, beats, gaps));
        var cell = PlayLayout.StripCellWidth(StripRoom, beats, gaps);
        Assert.IsTrue(cell * beats + gaps * PlayLayout.StripGroupGap <= StripRoom + 1e-9, $"{cell} wide cells overflow");

        // What preview.8 did: it sized the cells as if there were no gaps, so the row overflowed by exactly the gaps and
        // a scroll bar appeared although every beat fits.
        var before = Math.Clamp(StripRoom / beats, PlayLayout.StripMinCell, PlayLayout.StripMaxCell);
        Assert.AreEqual(gaps * PlayLayout.StripGroupGap, before * beats + gaps * PlayLayout.StripGroupGap - StripRoom, 1e-9);
    }

    [TestMethod]
    [DataRow(424d, 12, 3, false)]    // 12/8
    [DataRow(414d, 12, 3, false)]    // exactly the narrowest cells and the gaps
    [DataRow(413.9d, 12, 3, true)]
    [DataRow(424d, 13, 4, true)]     // 13 * 32 + 4 * 10 = 456
    [DataRow(424d, 16, 0, true)]     // 16 * 32 = 512
    [DataRow(512d, 16, 0, false)]
    [DataRow(424d, 8, 0, false)]     // 8/8 and smaller always fit
    [DataRow(700d, 64, 0, true)]
    public void TheStripScrollsOnlyWhenTheNarrowestCellsAndTheGapsDoNotFit(double available, int beats, int gaps, bool scrolls)
    {
        Assert.AreEqual(scrolls, PlayLayout.StripNeedsScroll(available, beats, gaps));
    }

    // The tempo row: slower, TAP and faster stay centred under the dial, and "Tap to start" sits to their right.
    // Measured at 100% text size in English: the three controls are 264 wide, the labelled switch about 170, the icon-only one 44.
    private const double Cluster = 264;
    private const double SwitchLabelled = 170;
    private const double SwitchIcon = 44;

    [TestMethod]
    [DataRow(760d, "Labelled")]   // the page of an 800 wide window
    [DataRow(978d, "Labelled")]   // the page of a 1000 wide window
    [DataRow(660d, "Labelled")]
    [DataRow(600d, "Compact")]
    [DataRow(440d, "Compact")]
    [DataRow(399d, "Appended")]
    [DataRow(360d, "Appended")]   // a 400 wide window
    [DataRow(322d, "Appended")]
    [DataRow(321d, "OwnRow")]
    [DataRow(280d, "OwnRow")]     // a 320 wide window
    public void TheSwitchStaysBesideTheTempoControlsAndOnlyInAVeryNarrowWindowTakesARowOfItsOwn(double rowWidth, string expected)
    {
        Assert.AreEqual(expected, PlayLayout.ChooseTapStart(rowWidth, Cluster, SwitchLabelled, SwitchIcon).ToString());
    }

    [TestMethod]
    public void TheSwitchChoosesFromTheRoomLeftOfTheCentredControlsOnEachSide()
    {
        // Each side has (row - controls) / 2, less the gap between the controls and the switch.
        var side = (double width) => (width - Cluster) / 2 - PlayLayout.TapStartGap;
        var labelled = 2 * (SwitchLabelled + PlayLayout.TapStartGap) + Cluster;
        Assert.AreEqual(SwitchLabelled, side(labelled), 1e-9);
        Assert.AreEqual(TapStartPlacement.Labelled, PlayLayout.ChooseTapStart(labelled, Cluster, SwitchLabelled, SwitchIcon));
        Assert.AreEqual(TapStartPlacement.Compact, PlayLayout.ChooseTapStart(Math.BitDecrement(labelled), Cluster, SwitchLabelled, SwitchIcon));
        var compact = 2 * (SwitchIcon + PlayLayout.TapStartGap) + Cluster;
        Assert.AreEqual(TapStartPlacement.Compact, PlayLayout.ChooseTapStart(compact, Cluster, SwitchLabelled, SwitchIcon));
        Assert.AreEqual(TapStartPlacement.Appended, PlayLayout.ChooseTapStart(Math.BitDecrement(compact), Cluster, SwitchLabelled, SwitchIcon));
        var appended = Cluster + PlayLayout.TapStartSpacing + SwitchIcon;
        Assert.AreEqual(TapStartPlacement.Appended, PlayLayout.ChooseTapStart(appended, Cluster, SwitchLabelled, SwitchIcon));
        Assert.AreEqual(TapStartPlacement.OwnRow, PlayLayout.ChooseTapStart(Math.BitDecrement(appended), Cluster, SwitchLabelled, SwitchIcon));
    }

    [TestMethod]
    public void EveryRowWidthGetsAPlacementThatFitsAndAWiderRowNeverGetsASmallerOne()
    {
        // Independent of the closed forms: for each width the chosen placement must fit its row, and as the row grows the
        // placement only ever goes from the row of its own, to the appended icon, to the icon beside, to the words beside.
        var order = new[] { TapStartPlacement.OwnRow, TapStartPlacement.Appended, TapStartPlacement.Compact, TapStartPlacement.Labelled };
        var previous = 0;
        for (var width = 200d; width <= 1400; width += 1)
        {
            var placement = PlayLayout.ChooseTapStart(width, Cluster, SwitchLabelled, SwitchIcon);
            var rank = Array.IndexOf(order, placement);
            Assert.IsTrue(rank >= previous, $"{width}: {placement} after a wider-placement");
            previous = rank;
            var needed = placement switch
            {
                TapStartPlacement.Labelled => Cluster + 2 * (PlayLayout.TapStartGap + SwitchLabelled),
                TapStartPlacement.Compact => Cluster + 2 * (PlayLayout.TapStartGap + SwitchIcon),
                TapStartPlacement.Appended => Cluster + PlayLayout.TapStartSpacing + SwitchIcon,
                _ => Cluster
            };
            Assert.IsTrue(needed <= width + 1e-9 || placement == TapStartPlacement.OwnRow, $"{width}: {placement} needs {needed}");
        }
    }

    [TestMethod]
    public void AWiderSwitchNeedsAWiderRowAndAnUnmeasuredRowKeepsTheDefault()
    {
        // Dutch and German labels, or a larger Windows text size, make the labelled switch wider: it gives way to the icon sooner.
        Assert.AreEqual(TapStartPlacement.Labelled, PlayLayout.ChooseTapStart(760, Cluster, 170, SwitchIcon));
        Assert.AreEqual(TapStartPlacement.Compact, PlayLayout.ChooseTapStart(760, Cluster, 260, SwitchIcon));
        Assert.AreEqual(TapStartPlacement.Labelled, PlayLayout.ChooseTapStart(0, Cluster, SwitchLabelled, SwitchIcon));
        Assert.AreEqual(TapStartPlacement.Labelled, PlayLayout.ChooseTapStart(760, 0, SwitchLabelled, SwitchIcon));
    }

    [TestMethod]
    public void TheStripOverflowsItsRoomExactlyWhenItNeedsToScroll()
    {
        // Independent of the formulas: lay the cells out the way the strip does and compare with the room.
        for (var available = 0d; available <= 1200; available += 7)
        for (var beats = 1; beats <= 64; beats++)
        for (var gaps = 0; gaps <= Math.Min(beats - 1, 8); gaps++)
        {
            var cell = PlayLayout.StripCellWidth(available, beats, gaps);
            Assert.IsTrue(cell >= PlayLayout.StripMinCell && cell <= PlayLayout.StripMaxCell, $"{beats} beats in {available}: cell {cell}");
            var total = beats * cell + gaps * PlayLayout.StripGroupGap;
            if (PlayLayout.StripNeedsScroll(available, beats, gaps))
                Assert.IsTrue(total > available, $"{beats} beats, {gaps} gaps, {available}: asked to scroll but {total} fits");
            else
                Assert.IsTrue(total <= available + 1e-9, $"{beats} beats, {gaps} gaps, {available}: {total} overflows without scrolling");
        }
    }
}
