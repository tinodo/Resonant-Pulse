using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class SubmissionTrackerTests
{
    [TestMethod]
    public void AnAppliedValueIsAnsweredAsApplied()
    {
        var tracker = new SubmissionTracker<int>();
        var ticket = tracker.Begin(6);
        Assert.IsTrue(tracker.TryGetPending(out var pending));
        Assert.AreEqual(6, pending);
        Assert.AreEqual(SubmissionAnswer.Applied, tracker.Resolve(ticket, applied: true));
        Assert.IsFalse(tracker.TryGetPending(out _), "nothing is on its way any more");
    }

    [TestMethod]
    public void ARefusedValueIsAnsweredAsRefusedWhenNothingNewerExists()
    {
        var tracker = new SubmissionTracker<int>();
        var ticket = tracker.Begin(6);
        Assert.AreEqual(SubmissionAnswer.Refused, tracker.Resolve(ticket, applied: false));
        Assert.IsFalse(tracker.TryGetPending(out _));
    }

    [TestMethod]
    public void AnEmptyBoxWhileAValueIsOnItsWayShowsThatValueAndTheValueStillCounts()
    {
        // Four clicks are saved; six are submitted and wait in the queue; the person clears the box. The box must show six (what
        // is on its way), not four (what the project holds right now): six is applied next, and the box would end up wrong.
        var tracker = new SubmissionTracker<double>();
        var ticket = tracker.Begin(6);
        Assert.IsTrue(tracker.TryGetPending(out var shown));
        Assert.AreEqual(6, shown);
        // An invalid draft is not a submission, so it neither supersedes six nor hides it from a later refusal.
        Assert.AreEqual(SubmissionAnswer.Applied, tracker.Resolve(ticket, applied: true));
    }

    [TestMethod]
    public void AnEmptyBoxWhileAValueIsOnItsWayStillPutsTheOldValueBackIfThatValueIsRefused()
    {
        var tracker = new SubmissionTracker<double>();
        var ticket = tracker.Begin(6);
        Assert.IsTrue(tracker.TryGetPending(out _), "the box showed six for the empty draft");
        Assert.AreEqual(SubmissionAnswer.Refused, tracker.Resolve(ticket, applied: false), "six is refused: the box must go back to what the project holds");
    }

    [TestMethod]
    public void AnAnswerToAnOlderSubmissionIsIgnoredOnceANewerOneWasMade()
    {
        var tracker = new SubmissionTracker<string>();
        var first = tracker.Begin("A");
        var second = tracker.Begin("B");
        Assert.AreEqual(SubmissionAnswer.Superseded, tracker.Resolve(first, applied: false));
        Assert.IsTrue(tracker.TryGetPending(out var pending));
        Assert.AreEqual("B", pending, "the newer value is still on its way");
        Assert.AreEqual(SubmissionAnswer.Applied, tracker.Resolve(second, applied: true));
        Assert.IsFalse(tracker.TryGetPending(out _));
    }

    [TestMethod]
    public void AnOlderValueThatIsAppliedAfterANewerOneWasSubmittedChangesNothing()
    {
        var tracker = new SubmissionTracker<int>();
        var first = tracker.Begin(40);
        var second = tracker.Begin(60);
        Assert.AreEqual(SubmissionAnswer.Superseded, tracker.Resolve(first, applied: true));
        Assert.AreEqual(SubmissionAnswer.Refused, tracker.Resolve(second, applied: false));
    }

    [TestMethod]
    public void ASliderThatIsStillBeingMovedIsNotPutBackByAnEarlierRefusal()
    {
        // The first gesture was submitted; a second one starts and the thumb moves, but it is not submitted until it ends. If the
        // first is refused now, putting the slider back would overwrite the second gesture, and releasing it would commit the
        // value that was put back.
        var tracker = new SubmissionTracker<int>();
        var first = tracker.Begin(40);
        tracker.Changed();
        Assert.AreEqual(SubmissionAnswer.Superseded, tracker.Resolve(first, applied: false));
        var second = tracker.Begin(60);
        Assert.AreEqual(SubmissionAnswer.Refused, tracker.Resolve(second, applied: false), "the second is refused on its own account");
    }

    [TestMethod]
    public void NothingIsPendingBeforeTheFirstSubmission()
    {
        Assert.IsFalse(new SubmissionTracker<double>().TryGetPending(out _));
    }

    [TestMethod]
    public void AValueRefusedAtOnceWhileAnOlderOneIsInTheQueueShowsTheOlderOneAndItsAnswerDecidesNext()
    {
        // "Kick" waits in the queue; a blank name is typed and refused straight away. The box must show Kick (the project is about
        // to hold it), not the old name; and when Kick is applied, nothing is left to put right.
        var tracker = new SubmissionTracker<string>();
        var kick = tracker.Begin("Kick");
        var blank = tracker.Begin("   ");
        Assert.AreEqual(SubmissionAnswer.Refused, tracker.Resolve(blank, applied: false));
        Assert.AreEqual("Kick", tracker.OnItsWayOr("Lane 1"));
        Assert.AreEqual(SubmissionAnswer.Applied, tracker.Resolve(kick, applied: true), "Kick is the newest that is left, so it decides");
        Assert.AreEqual("Kick", tracker.OnItsWayOr("Kick"), "nothing is on its way any more: what the project holds is shown");
        Assert.IsFalse(tracker.TryGetPending(out _));
    }

    [TestMethod]
    public void TheOlderOneThatTheRefusalLeftInTheQueueIsPutRightIfItIsRefusedToo()
    {
        var tracker = new SubmissionTracker<string>();
        var kick = tracker.Begin("Kick");
        var blank = tracker.Begin("   ");
        Assert.AreEqual(SubmissionAnswer.Refused, tracker.Resolve(blank, applied: false));
        Assert.AreEqual(SubmissionAnswer.Refused, tracker.Resolve(kick, applied: false), "the box showed Kick, so it must now go back");
        Assert.AreEqual("Lane 1", tracker.OnItsWayOr("Lane 1"));
    }

    [TestMethod]
    public void OfThreeSubmissionsTheOneRefusedAtOnceLeavesTheNewestOfTheOtherTwoToDecide()
    {
        var tracker = new SubmissionTracker<int>();
        var first = tracker.Begin(1);
        var second = tracker.Begin(2);
        var third = tracker.Begin(3);
        Assert.AreEqual(SubmissionAnswer.Refused, tracker.Resolve(third, applied: false));
        Assert.AreEqual(2, tracker.OnItsWayOr(0));
        Assert.AreEqual(SubmissionAnswer.Superseded, tracker.Resolve(first, applied: true));
        Assert.AreEqual(SubmissionAnswer.Applied, tracker.Resolve(second, applied: true));
        Assert.AreEqual(9, tracker.OnItsWayOr(9));
    }

    [TestMethod]
    public void WhenTheNewestIsAppliedOlderValuesStillWaitingForTheirAnswerNoLongerCount()
    {
        // The queue runs in order, so once the newest is applied the older ones have been dealt with: a late answer to one of them
        // changes nothing, and the old value is not offered as "on its way".
        var tracker = new SubmissionTracker<int>();
        var older = tracker.Begin(40);
        var newest = tracker.Begin(60);
        Assert.AreEqual(SubmissionAnswer.Applied, tracker.Resolve(newest, applied: true));
        Assert.IsFalse(tracker.TryGetPending(out _));
        Assert.AreEqual(SubmissionAnswer.Superseded, tracker.Resolve(older, applied: false));
    }

    [TestMethod]
    public void ASliderThatIsStillBeingMovedIgnoresEveryOlderAnswerWhateverTheyAre()
    {
        // A slider: two gestures were submitted, a third is being moved. Neither answer may put anything back or hand the
        // decision to the other: the thumb is where the person left it, and the third gesture is answered on its own account.
        var tracker = new SubmissionTracker<int>();
        var first = tracker.Begin(40);
        var second = tracker.Begin(50);
        tracker.Changed();
        Assert.AreEqual(SubmissionAnswer.Superseded, tracker.Resolve(second, applied: false));
        Assert.AreEqual(SubmissionAnswer.Superseded, tracker.Resolve(first, applied: true));
        var third = tracker.Begin(70);
        Assert.AreEqual(SubmissionAnswer.Applied, tracker.Resolve(third, applied: true));
    }

    [TestMethod]
    public void OnItsWayOrGivesWhatTheProjectHoldsWhenNothingWaits()
    {
        var tracker = new SubmissionTracker<double>();
        Assert.AreEqual(4, tracker.OnItsWayOr(4));
        tracker.Begin(6);
        Assert.AreEqual(6, tracker.OnItsWayOr(4));
    }

    [TestMethod]
    public void IsPendingIsTrueFromTheSubmissionUntilItsAnswerWhateverTheAnswerIs()
    {
        var tracker = new SubmissionTracker<bool>();
        Assert.IsFalse(tracker.IsPending);
        var applied = tracker.Begin(true);
        Assert.IsTrue(tracker.IsPending);
        tracker.Resolve(applied, applied: true);
        Assert.IsFalse(tracker.IsPending);
        var refused = tracker.Begin(false);
        Assert.IsTrue(tracker.IsPending);
        tracker.Resolve(refused, applied: false);
        Assert.IsFalse(tracker.IsPending);
    }

    [TestMethod]
    public void AnOlderSubmissionThatIsStillWaitingKeepsTheControlPendingAfterANewerOneIsRefusedAtOnce()
    {
        var tracker = new SubmissionTracker<string>();
        var older = tracker.Begin("Kick");
        var newer = tracker.Begin("  ");
        Assert.AreEqual(SubmissionAnswer.Refused, tracker.Resolve(newer, applied: false));
        Assert.IsTrue(tracker.IsPending, "Kick is still on its way, so a screen that shows the project must not overwrite the box");
        Assert.AreEqual(SubmissionAnswer.Applied, tracker.Resolve(older, applied: true));
        Assert.IsFalse(tracker.IsPending);
    }
}
