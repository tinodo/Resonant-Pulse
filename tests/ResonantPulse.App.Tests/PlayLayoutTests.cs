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
    public void ACornerPlacementNeverCoversTheRingAtAnyWindowSizeOrLabelWidth()
    {
        // Independent of the closed-form distance: walk the ring's outermost tick circle and test every point.
        foreach (var labelled in new[] { 220d, Labelled, 330d })
        foreach (var width in Enumerable.Range(10, 80).Select(step => step * 20d))
        foreach (var height in Enumerable.Range(10, 50).Select(step => step * 20d))
        {
            var plan = PlayLayout.Choose(width, height, labelled, Icons);
            if (plan.Placement != RailPlacement.Corner) continue;
            var stripWidth = plan.ShowLabels ? labelled : Icons;
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
    [DataRow(978d, 600d, true)]   // the default window
    [DataRow(938d, 554d, true)]   // 1000 x 720
    [DataRow(738d, 434d, false)]  // 800 x 600: the dial would drop below 200
    [DataRow(738d, 511d, false)]  // wide: one point under the 512 needed
    [DataRow(738d, 512d, true)]
    [DataRow(540d, 596d, false)]  // mid width: the settings row stacks, so more room is needed
    [DataRow(540d, 597d, true)]
    [DataRow(458d, 475d, false)]  // 520 x 720: no room
    [DataRow(358d, 850d, true)]   // tall and narrow: the notice wraps but the dial keeps its room
    [DataRow(358d, 475d, false)]
    public void TheNoticeOnlyAppearsWhenTheDialKeepsAUsableSize(double pageWidth, double pageHeight, bool fits)
    {
        Assert.AreEqual(fits, PlayLayout.NoticeFits(pageWidth, pageHeight));
    }

    [TestMethod]
    public void ARoomierPageNeverHidesANoticeThatFitsInASmallerOneOfTheSameWidth()
    {
        foreach (var width in new[] { 358d, 458d, 738d, 978d })
        {
            var shown = false;
            for (var height = 200d; height <= 1200; height += 10)
            {
                var fits = PlayLayout.NoticeFits(width, height);
                Assert.IsFalse(shown && !fits, $"{width} wide: shown at a shorter page but hidden at {height}");
                shown |= fits;
            }
            Assert.IsTrue(shown, $"{width} wide never shows the notice");
        }
    }
}
