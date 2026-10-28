using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class StudioRulesTests
{
    private static readonly DateTime Start = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void ALongProjectNameIsCutWithAnEllipsisAndNeverInsideACharacter()
    {
        Assert.AreEqual("Open practice", TextFit.Shorten("Open practice", 40));
        Assert.AreEqual("Open prac…", TextFit.Shorten("Open practice", 10));
        Assert.AreEqual(10, TextFit.Shorten("Open practice", 10).Length);
        // A pair of UTF-16 characters (here U+1D11E, the G clef) is one character: it is dropped, not split.
        var cut = TextFit.Shorten("abc\U0001D11Edef", 5);
        Assert.AreEqual("abc…", cut);
        Assert.IsFalse(cut.Any(char.IsSurrogate));
    }

    [TestMethod]
    public void RepeatedEditsToOneFieldShareAnUndoStep()
    {
        Assert.IsTrue(UndoCoalescing.ShouldMerge("Lane0.Volume", "Lane0.Volume", Start, Start.AddMilliseconds(400)));
        Assert.IsTrue(UndoCoalescing.ShouldMerge("Lane0.Volume", "Lane0.Volume", Start, Start + UndoCoalescing.Window));
    }

    [TestMethod]
    public void AnotherFieldALongPauseOrNoKeyStartsANewUndoStep()
    {
        Assert.IsFalse(UndoCoalescing.ShouldMerge("Lane0.Volume", "Lane0.Pan", Start, Start.AddMilliseconds(100)));
        Assert.IsFalse(UndoCoalescing.ShouldMerge("Lane0.Volume", "Lane1.Volume", Start, Start.AddMilliseconds(100)));
        Assert.IsFalse(UndoCoalescing.ShouldMerge("Lane0.Volume", "Lane0.Volume", Start, Start + UndoCoalescing.Window + TimeSpan.FromMilliseconds(1)));
        Assert.IsFalse(UndoCoalescing.ShouldMerge(null, "Lane0.Volume", Start, Start.AddMilliseconds(100)));
        Assert.IsFalse(UndoCoalescing.ShouldMerge("Lane0.Volume", null, Start, Start.AddMilliseconds(100)));
        Assert.IsFalse(UndoCoalescing.ShouldMerge(null, null, Start, Start.AddMilliseconds(100)));
        // A clock that went backwards never merges.
        Assert.IsFalse(UndoCoalescing.ShouldMerge("a", "a", Start, Start.AddSeconds(-1)));
    }

    [TestMethod]
    public void LevelsRoundToWholePercentAndStayInRange()
    {
        Assert.AreEqual(70, LevelFormat.ToPercent(0.7f));
        Assert.AreEqual(0, LevelFormat.ToPercent(-0.5));
        Assert.AreEqual(100, LevelFormat.ToPercent(3));
        Assert.AreEqual(0.35, LevelFormat.FromPercent(35), 1e-9);
        Assert.AreEqual(0.3, LevelFormat.FromPercent(29.6), 1e-9);
        Assert.AreEqual(1.0, LevelFormat.FromPercent(250), 1e-9);
        Assert.AreEqual(0.0, LevelFormat.FromPercent(-5), 1e-9);
    }

    [TestMethod]
    public void PanRoundsToWholePercentLeftNegativeRightPositive()
    {
        Assert.AreEqual(-30, LevelFormat.PanToPercent(-0.3f));
        Assert.AreEqual(25, LevelFormat.PanToPercent(0.25f));
        Assert.AreEqual(-100, LevelFormat.PanToPercent(-7));
        Assert.AreEqual(-0.3, LevelFormat.PanFromPercent(-30), 1e-9);
        Assert.AreEqual(1.0, LevelFormat.PanFromPercent(400), 1e-9);
        Assert.AreEqual(0.0, LevelFormat.PanFromPercent(0.2), 1e-9);
    }

    [TestMethod]
    public void ADragIsOneChangeCommittedWhenThePointerIsReleased()
    {
        var gesture = new GestureCommit();
        gesture.PointerDown();
        for (var tick = 0; tick < 6; tick++) Assert.IsFalse(gesture.Changed(), "no commit during a drag");
        Assert.IsTrue(gesture.PointerUp());
        Assert.IsFalse(gesture.PointerUp(), "released twice commits once");
    }

    [TestMethod]
    public void AHeldArrowKeyIsOneChangeCommittedWhenTheKeyIsReleased()
    {
        var gesture = new GestureCommit();
        gesture.KeyDown();
        Assert.IsFalse(gesture.Changed());
        Assert.IsFalse(gesture.Changed());
        Assert.IsTrue(gesture.KeyUp());
    }

    [TestMethod]
    public void AChangeWithNoGestureIsCommittedAtOnce()
    {
        var gesture = new GestureCommit();
        Assert.IsTrue(gesture.Changed(), "a screen reader or automation setting the value");
        Assert.IsFalse(gesture.PointerUp(), "nothing changed since");
    }

    [TestMethod]
    public void AGestureWithNoChangeCommitsNothing()
    {
        var gesture = new GestureCommit();
        gesture.PointerDown();
        Assert.IsFalse(gesture.PointerUp());
        gesture.KeyDown();
        Assert.IsFalse(gesture.KeyUp());
    }

    [TestMethod]
    public void ThePointerAndTheKeyboardOverlapWithoutCommittingEarly()
    {
        var gesture = new GestureCommit();
        gesture.PointerDown();
        gesture.KeyDown();
        Assert.IsFalse(gesture.Changed());
        Assert.IsFalse(gesture.PointerUp(), "the key is still held");
        Assert.IsTrue(gesture.KeyUp());
    }

    [TestMethod]
    public void TheEuclideanPreviewShowsEveryStepAndExactlyTheRequestedHits()
    {
        var lane = RhythmGenerators.Euclidean(5, 8, new Rational(4, 1));
        var hits = lane.Events.Select(item => item.Accent != Accent.Silent).ToArray();
        var preview = EuclideanPreview.Format(hits);
        Assert.AreEqual(8, preview.Split(' ').Length);
        Assert.AreEqual(5, preview.Count(symbol => symbol == '●'));
        Assert.AreEqual(3, preview.Count(symbol => symbol == '○'));
        Assert.AreEqual("● ○ ● ○ ● ● ○ ●", preview);
    }

    [TestMethod]
    public void RotatingTheEuclideanRhythmMovesTheHitsAndKeepsTheirNumber()
    {
        var plain = EuclideanPreview.Format(RhythmGenerators.Euclidean(3, 8, new Rational(4, 1)).Events.Select(item => item.Accent != Accent.Silent));
        var rotated = EuclideanPreview.Format(RhythmGenerators.Euclidean(3, 8, new Rational(4, 1), 1).Events.Select(item => item.Accent != Accent.Silent));
        Assert.AreNotEqual(plain, rotated);
        Assert.AreEqual(plain.Count(symbol => symbol == '●'), rotated.Count(symbol => symbol == '●'));
    }

    private static RhythmLane Lane(int pulses, params Accent[] pattern) =>
        new() { Name = "Lane", Pulses = pulses, CycleQuarters = 4, Pattern = pattern };

    private static void AssertAccepted(RhythmLane lane)
    {
        var document = new PulseDocument();
        document.Lanes.Add(lane);
        DocumentValidator.Validate(document);
    }

    [TestMethod]
    public void FewerClicksCutThePatternSoTheProjectStillAcceptsIt()
    {
        var lane = Lane(8, Accent.Strong, Accent.Soft, Accent.Normal, Accent.Silent, Accent.Normal, Accent.Normal, Accent.Soft, Accent.Strong);
        LaneEdits.SetPulses(lane, 5);
        Assert.AreEqual(5, lane.Pulses);
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Soft, Accent.Normal, Accent.Silent, Accent.Normal }, lane.Pattern);
        AssertAccepted(lane);
    }

    [TestMethod]
    public void MoreClicksKeepTheShorterPatternRepeatingAsItPlays()
    {
        var lane = Lane(4, Accent.Strong, Accent.Normal);
        LaneEdits.SetPulses(lane, 6);
        Assert.AreEqual(6, lane.Pulses);
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Normal }, lane.Pattern);
        CollectionAssert.AreEqual(
            new[] { Accent.Strong, Accent.Normal, Accent.Strong, Accent.Normal, Accent.Strong, Accent.Normal },
            AccentPatterns.ExpandLane(lane.Pattern, lane.Pulses));
        AssertAccepted(lane);
    }

    [TestMethod]
    public void ATapOnABeatWrapsAroundAndWritesTheWholePattern()
    {
        var lane = Lane(4);
        // No pattern yet: the metronome plays a strong first beat and normal ones.
        LaneEdits.ChangeAccent(lane, 2, 1, wrap: true);
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Normal, Accent.Strong, Accent.Normal }, lane.Pattern);
        LaneEdits.ChangeAccent(lane, 2, 1, wrap: true);
        Assert.AreEqual(Accent.Silent, lane.Pattern[2], "strong wraps to silent");
        LaneEdits.ChangeAccent(lane, 2, 1, wrap: true);
        Assert.AreEqual(Accent.Soft, lane.Pattern[2]);
        AssertAccepted(lane);
    }

    [TestMethod]
    public void TheArrowKeysStopAtTheStrongestAndTheQuietest()
    {
        var lane = Lane(4, Accent.Strong, Accent.Normal, Accent.Normal, Accent.Silent);
        Assert.IsFalse(LaneEdits.ChangeAccent(lane, 0, 1, wrap: false), "up on the strongest beat changes nothing");
        Assert.AreEqual(Accent.Strong, lane.Pattern[0]);
        Assert.IsFalse(LaneEdits.ChangeAccent(lane, 3, -1, wrap: false), "down on the quietest beat changes nothing");
        Assert.AreEqual(Accent.Silent, lane.Pattern[3]);
        Assert.IsTrue(LaneEdits.ChangeAccent(lane, 1, -1, wrap: false));
        Assert.AreEqual(Accent.Soft, lane.Pattern[1]);
    }

    [TestMethod]
    public void AnArrowKeyAtTheEndOfTheScaleLeavesThePatternExactlyAsItWas()
    {
        // No edit is made for it, so nothing is saved, Undo gets no empty step and Redo is not cleared.
        var lane = Lane(3, Accent.Strong, Accent.Soft, Accent.Silent);
        var before = lane.Pattern.ToArray();
        Assert.IsFalse(LaneEdits.ChangeAccent(lane, 0, 1, wrap: false));
        Assert.IsFalse(LaneEdits.ChangeAccent(lane, 2, -1, wrap: false));
        Assert.IsFalse(LaneEdits.ChangeAccent(lane, 0, 5, wrap: false));
        CollectionAssert.AreEqual(before, lane.Pattern);
        Assert.IsTrue(LaneEdits.ChangeAccent(lane, 0, 5, wrap: true), "a tap always goes round, so it always changes something");
    }

    [TestMethod]
    public void ATapOnABeatThatIsGoneChangesNothingAndSaysSo()
    {
        // The number of clicks was lowered to four and a tap on the seventh beat arrives before the strip has caught up.
        var lane = Lane(4, Accent.Strong, Accent.Normal, Accent.Normal, Accent.Normal);
        Assert.IsFalse(LaneEdits.ChangeAccent(lane, 6, 1, wrap: true));
        Assert.IsFalse(LaneEdits.ChangeAccent(lane, 4, -1, wrap: false));
        Assert.IsFalse(LaneEdits.ChangeAccent(lane, -1, 1, wrap: true));
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Normal, Accent.Normal, Accent.Normal }, lane.Pattern);
        Assert.IsTrue(LaneEdits.ChangeAccent(lane, 3, 1, wrap: true), "the last beat that exists can still be changed");
    }

    [TestMethod]
    public void ChangingABeatOfAShortPatternOnALongerLaneKeepsEveryOtherBeatAsItPlayed()
    {
        var lane = Lane(6, Accent.Strong, Accent.Normal);
        LaneEdits.ChangeAccent(lane, 5, -1, wrap: false);
        CollectionAssert.AreEqual(
            new[] { Accent.Strong, Accent.Normal, Accent.Strong, Accent.Normal, Accent.Strong, Accent.Soft }, lane.Pattern);
        AssertAccepted(lane);
    }

    [TestMethod]
    public void RotatingAPatternMovesEveryBeatOneStepLater()
    {
        var lane = Lane(4, Accent.Strong, Accent.Normal, Accent.Soft, Accent.Silent);
        LaneEdits.Rotate(lane);
        CollectionAssert.AreEqual(new[] { Accent.Silent, Accent.Strong, Accent.Normal, Accent.Soft }, lane.Pattern);
    }

    [TestMethod]
    public void RotatingExactPositionsGoesTheSameWayAsRotatingAPattern()
    {
        var lane = new RhythmLane { Pulses = 4, CycleQuarters = 4 };
        foreach (var quarter in new[] { 0, 1, 3 }) lane.Events.Add(new RhythmEvent { PositionQuarters = quarter, Accent = Accent.Normal });
        LaneEdits.Rotate(lane);
        CollectionAssert.AreEqual(new Rational[] { 0, 1, 2 }, lane.Events.Select(item => item.PositionQuarters).ToArray(),
            "positions 0, 1 and 3 move to 1, 2 and 0 (the last comes round), kept in order");
        AssertAccepted(lane);
    }

    [TestMethod]
    public void InvertingSwapsClicksAndRestsInAPatternAndInExactPositions()
    {
        var pattern = Lane(4, Accent.Strong, Accent.Silent, Accent.Soft, Accent.Silent);
        LaneEdits.Invert(pattern);
        CollectionAssert.AreEqual(new[] { Accent.Silent, Accent.Normal, Accent.Silent, Accent.Normal }, pattern.Pattern);

        var exact = new RhythmLane { Pulses = 2, CycleQuarters = 4 };
        exact.Events.Add(new RhythmEvent { PositionQuarters = 0, Accent = Accent.Strong });
        exact.Events.Add(new RhythmEvent { PositionQuarters = 2, Accent = Accent.Silent });
        LaneEdits.Invert(exact);
        CollectionAssert.AreEqual(new[] { Accent.Silent, Accent.Normal }, exact.Events.Select(item => item.Accent).ToArray());
    }

    [TestMethod]
    public void ANewLaneNeverTakesANameAnotherLaneHas()
    {
        Assert.AreEqual("Lane 1", LaneEdits.NextName([], "Lane"));
        Assert.AreEqual("Lane 3", LaneEdits.NextName(["Lane 1", "Lane 2", "Kick"], "Lane"));
        Assert.AreEqual("Lane 2", LaneEdits.NextName(["Lane 1", "Lane 3"], "Lane"), "fills the gap left by a removed lane");
    }

    [TestMethod]
    public void ACopyKeepsItsNameWithASuffixAndStaysWithinTheNameLimit()
    {
        Assert.AreEqual("Kick (copy)", LaneEdits.CopyName("Kick", " (copy)"));
        var longest = new string('x', LaneEdits.MaxNameLength);
        var copy = LaneEdits.CopyName(longest, " (copy)");
        Assert.AreEqual(LaneEdits.MaxNameLength, copy.Length);
        Assert.IsTrue(copy.EndsWith(" (copy)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ACopyNameIsNeverCutInsideACharacterThatTakesTwoCodeUnits()
    {
        const string suffix = " (copy)";
        var keep = LaneEdits.MaxNameLength - suffix.Length;
        // The cut would fall between the two halves of the emoji: the whole character goes, not half of it.
        var split = new string('x', keep - 1) + "😀" + new string('y', 20);
        var cutBefore = LaneEdits.CopyName(split, suffix);
        Assert.IsFalse(cutBefore.Any(char.IsSurrogate), "no half of a character is left");
        Assert.AreEqual(new string('x', keep - 1) + suffix, cutBefore);
        Assert.IsTrue(cutBefore.Length <= LaneEdits.MaxNameLength);

        // The cut falls just after the emoji: it stays whole.
        var whole = new string('x', keep - 2) + "😀" + new string('y', 20);
        var cutAfter = LaneEdits.CopyName(whole, suffix);
        Assert.AreEqual(new string('x', keep - 2) + "😀" + suffix, cutAfter);
        Assert.AreEqual(LaneEdits.MaxNameLength, cutAfter.Length);
    }

    [TestMethod]
    public void ACycleOfBeatsIsMeasuredInTheProjectsOwnWrittenBeats()
    {
        // "4 beats" is four clicks of the main pattern: a quarter note in 4/4, an eighth in 6/8, a half note in 3/2.
        Assert.AreEqual(4, LaneEdits.CycleQuarters(4, 4));
        Assert.AreEqual(3, LaneEdits.CycleQuarters(3, 4));
        Assert.AreEqual(2, LaneEdits.CycleQuarters(4, 8));
        Assert.AreEqual(1.5, LaneEdits.CycleQuarters(3, 8));
        Assert.AreEqual(8, LaneEdits.CycleQuarters(4, 2));
        Assert.AreEqual(1, LaneEdits.CycleQuarters(4, 16));
        // Whatever the time signature, the lane that is made is one the project accepts.
        foreach (var denominator in new[] { 1, 2, 4, 8, 16, 32, 64 })
        foreach (var beats in new[] { 3, 4 })
        {
            var lane = new RhythmLane { Pulses = 3, CycleQuarters = LaneEdits.CycleQuarters(beats, denominator), Pattern = [Accent.Normal, Accent.Normal, Accent.Normal] };
            AssertAccepted(lane);
        }
    }
}
