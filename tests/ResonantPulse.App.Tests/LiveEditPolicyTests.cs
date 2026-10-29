using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class LiveEditPolicyTests
{
    private static readonly EditorChangeKind[] LiveKinds =
        [EditorChangeKind.SoundAndAccentsOnly, EditorChangeKind.GlobalTempoOnly, EditorChangeKind.PlayStructure];

    [TestMethod]
    public void OnlyLiveEligibleKindsFollowARunningTransport()
    {
        foreach (var kind in Enum.GetValues<EditorChangeKind>())
            Assert.AreEqual(LiveKinds.Contains(kind), LiveEditPolicy.IsLive(kind), kind.ToString());
    }

    [TestMethod]
    [DataRow(LiveEditOutcome.SoundApplied)]
    [DataRow(LiveEditOutcome.StructuralQueued)]
    [DataRow(LiveEditOutcome.NoChange)]
    public void AnAppliedLiveEditLeavesNothingPending(LiveEditOutcome outcome)
    {
        foreach (var kind in LiveKinds)
            Assert.IsFalse(LiveEditPolicy.LeavesPendingEdit(kind, true, true, outcome, false), $"{kind}/{outcome}");
    }

    [TestMethod]
    public void AProgramThatCannotFollowLeavesAPendingEdit() =>
        Assert.IsTrue(LiveEditPolicy.LeavesPendingEdit(EditorChangeKind.GlobalTempoOnly, true, true, LiveEditOutcome.RequiresRestart, false));

    [TestMethod]
    public void AFaultedTransportKeepsItsOldSettingsSoTheEditIsPending() =>
        Assert.IsTrue(LiveEditPolicy.LeavesPendingEdit(EditorChangeKind.SoundAndAccentsOnly, true, true, LiveEditOutcome.NotPlaying, false));

    [TestMethod]
    public void ACountInChangeWaitsForTheNextStartEvenWhenTheRestWasApplied() =>
        Assert.IsTrue(LiveEditPolicy.LeavesPendingEdit(EditorChangeKind.PlayStructure, true, true, LiveEditOutcome.StructuralQueued, true));

    [TestMethod]
    public void NothingIsPendingWithoutARunningRevision()
    {
        foreach (var kind in Enum.GetValues<EditorChangeKind>())
            Assert.IsFalse(LiveEditPolicy.LeavesPendingEdit(kind, true, false, LiveEditOutcome.NotPlaying, true), kind.ToString());
    }

    [TestMethod]
    public void EditsThatAreNotLiveStayPendingUntilStopAndStart()
    {
        Assert.IsTrue(LiveEditPolicy.LeavesPendingEdit(EditorChangeKind.Document, true, true, LiveEditOutcome.NotPlaying, false));
        Assert.IsTrue(LiveEditPolicy.LeavesPendingEdit(EditorChangeKind.PracticeTimerOnly, true, true, LiveEditOutcome.NotPlaying, false));
        Assert.IsFalse(LiveEditPolicy.LeavesPendingEdit(EditorChangeKind.Document, false, true, LiveEditOutcome.NotPlaying, false));
        Assert.IsFalse(LiveEditPolicy.LeavesPendingEdit(EditorChangeKind.MasterGainOnly, false, true, LiveEditOutcome.NotPlaying, false));
        // A field changed in place that does not concern playback (a song's notes) leaves nothing pending.
        Assert.IsFalse(LiveEditPolicy.LeavesPendingEdit(EditorChangeKind.InPlaceField, false, true, LiveEditOutcome.NotPlaying, false));
    }
}
