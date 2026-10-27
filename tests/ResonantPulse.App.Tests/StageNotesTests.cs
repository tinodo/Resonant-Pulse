using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class StageNotesTests
{
    [TestMethod]
    public async Task SavesRunInTheOrderTheyWereAskedForEvenWhenTheFirstIsSlow()
    {
        var queue = new SaveQueue();
        var order = new List<string>();
        var releaseFirst = new TaskCompletionSource();
        _ = queue.Enqueue(async () => { await releaseFirst.Task; order.Add("first"); });
        _ = queue.Enqueue(() => { order.Add("second"); return Task.CompletedTask; });
        _ = queue.Enqueue(() => { order.Add("third"); return Task.CompletedTask; });
        await Task.Delay(50);
        CollectionAssert.AreEqual(Array.Empty<string>(), order, "nothing overtakes the first");
        releaseFirst.SetResult();
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
        CollectionAssert.AreEqual(new[] { "first", "second", "third" }, order);
    }

    [TestMethod]
    public async Task AFailedSaveDoesNotStopTheOnesAfterIt()
    {
        var queue = new SaveQueue();
        var ran = new List<string>();
        _ = queue.Enqueue(() => throw new InvalidOperationException("disk full"));
        _ = queue.Enqueue(() => { ran.Add("later"); return Task.CompletedTask; });
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
        CollectionAssert.AreEqual(new[] { "later" }, ran);
    }

    [TestMethod]
    public async Task WaitingForAnIdleQueueReturnsAtOnceAndNeverThrows()
    {
        var queue = new SaveQueue();
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
        _ = queue.Enqueue(() => throw new InvalidOperationException("boom"));
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public void ADraftIsSavedOnceNoMatterHowOftenTheBoxLosesFocus()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "intro");
        Assert.IsFalse(draft.NeedsSave("intro"));
        Assert.IsTrue(draft.NeedsSave("intro, slowly"));
        draft.Request("intro, slowly");
        Assert.IsFalse(draft.NeedsSave("intro, slowly"), "already on its way");
        Assert.IsTrue(draft.NeedsSave("intro, slowly, then fast"), "a newer draft is a new request");
    }

    [TestMethod]
    public void ASecondDraftWhileTheFirstIsBeingSavedIsNeitherDroppedNorLostWhenTheFirstFinishes()
    {
        var draft = new StageNotesDraft();
        draft.Show(2, "");
        var first = draft.Request("first");
        var second = draft.Request("second");
        draft.Completed(first);
        Assert.AreEqual("first", draft.Saved);
        Assert.AreEqual("second", draft.Restore(), "the second is still on its way");
        Assert.IsFalse(draft.NeedsSave("second"));
        draft.Completed(second);
        Assert.AreEqual("second", draft.Saved);
        Assert.IsFalse(draft.NeedsSave("second"));
    }

    [TestMethod]
    public void EveryRequestHasItsOwnIncreasingNumber()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "");
        var first = draft.Request("same");
        var second = draft.Request("same");
        Assert.IsTrue(second.Id > first.Id);
        Assert.AreNotEqual(first, second, "two requests for the same text are still two requests");
    }

    [TestMethod]
    public void NotesAskedForAThenBThenAAreReleasedByTheirOwnRequestOnly()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "");
        var first = draft.Request("A");
        var second = draft.Request("B");
        var third = draft.Request("A");

        draft.Completed(first);
        Assert.AreEqual("A", draft.Saved);
        Assert.IsFalse(draft.NeedsSave("A"), "the last A is still on its way; the first A finishing does not release it");
        Assert.AreEqual("A", draft.Restore());

        draft.Completed(second);
        Assert.AreEqual("B", draft.Saved, "the project holds B for now");
        Assert.IsFalse(draft.NeedsSave("A"), "but the last A is still on its way");
        Assert.AreEqual("A", draft.Restore(), "Esc puts back what is about to be saved, not the stale B");
        draft.Reset();
        Assert.AreEqual("A", draft.Show(0, "B"), "and so does a rebuilt page");

        draft.Completed(third);
        Assert.AreEqual("A", draft.Saved);
        Assert.IsFalse(draft.NeedsSave("A"));
    }

    [TestMethod]
    public void AFailureOfAnEarlierRequestForTheSameTextDoesNotMarkTheLastOneFailed()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved");
        var first = draft.Request("A");
        var second = draft.Request("B");
        var third = draft.Request("A");
        draft.Failed(first);
        draft.Failed(second);
        Assert.IsFalse(draft.HasFailed(0), "neither is the newest request");
        Assert.IsFalse(draft.NeedsSave("A"), "the last A is still on its way");
        draft.Failed(third);
        Assert.IsTrue(draft.HasFailed(0));
        Assert.IsTrue(draft.NeedsSave("A"), "now it is unsaved and can be asked for again");
    }

    [TestMethod]
    public void AFailedSaveLeavesTheDraftAskableAgainAndEscPutsBackWhatTheProjectHolds()
    {
        var draft = new StageNotesDraft();
        draft.Show(1, "kept");
        var request = draft.Request("typed");
        draft.Failed(request);
        Assert.IsTrue(draft.NeedsSave("typed"), "the same draft can be saved again");
        Assert.AreEqual("kept", draft.Saved);
        Assert.AreEqual("kept", draft.Restore());
    }

    [TestMethod]
    public void PuttingBackARefusedSelectionKeepsWhatWasTyped()
    {
        var draft = new StageNotesDraft();
        Assert.AreEqual("verse", draft.Show(0, "verse"));
        // The same song again (the list was put back after a selection was refused): the box keeps its draft.
        Assert.IsNull(draft.Show(0, "verse"));
        Assert.IsNull(draft.Show(0, "something else the project holds now"));
        Assert.AreEqual("verse", draft.Saved);
    }

    [TestMethod]
    public void AnotherSongLoadsItsOwnSavedNotes()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "verse");
        Assert.AreEqual("chorus", draft.Show(1, "chorus"));
        Assert.AreEqual(1, draft.Song);
        Assert.AreEqual("", draft.Show(-1, "ignored"));
        Assert.IsFalse(draft.NeedsSave("anything"), "no song, nothing to save");
    }

    [TestMethod]
    public void ANewPageShowsTheTextOfASaveStillOnItsWayInsteadOfTheOlderSavedText()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "old");
        var request = draft.Request("new");
        draft.Reset();
        Assert.AreEqual("new", draft.Show(0, "old"), "the project has not got it yet, but it is on its way");
        draft.Completed(request);
        Assert.AreEqual("new", draft.Saved);
        Assert.IsFalse(draft.NeedsSave("new"));
    }

    [TestMethod]
    public void ASaveForAnotherSongNeverChangesWhatTheBoxHolds()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "a");
        var first = draft.Request("a2");
        draft.Reset();
        draft.Show(1, "b");
        draft.Completed(first);
        Assert.AreEqual("b", draft.Saved);
        Assert.IsFalse(draft.NeedsSave("b"));
    }

    [TestMethod]
    public void ADraftTheProjectRefusedSurvivesTheBoxBeingRebuiltAndCanBeSavedAgain()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved");
        var request = draft.Request("far too long");
        draft.Failed(request);
        draft.Reset();
        Assert.AreEqual("far too long", draft.Show(0, "saved"), "the page shows what was typed, not what the project holds");
        Assert.IsTrue(draft.NeedsSave("far too long"), "so it is still unsaved and will be asked for again");
        Assert.AreEqual("saved", draft.Saved);
    }

    [TestMethod]
    public void EscDropsARefusedDraftForGood()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved");
        var request = draft.Request("refused");
        draft.Failed(request);
        Assert.AreEqual("saved", draft.Restore());
        Assert.IsFalse(draft.HasFailed(0));
        draft.Reset();
        Assert.AreEqual("saved", draft.Show(0, "saved"));
    }

    [TestMethod]
    public void ASuccessfulSaveForTheSongMakesAnEarlierRefusedDraftOutOfDate()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved");
        var refused = draft.Request("refused");
        draft.Failed(refused);
        var shorter = draft.Request("shorter");
        draft.Completed(shorter);
        Assert.IsFalse(draft.HasFailed(0));
        draft.Reset();
        Assert.AreEqual("shorter", draft.Show(0, "shorter"));
    }

    [TestMethod]
    public void AFailureOfASaveThatANewerOneHasReplacedIsNotRemembered()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved");
        var first = draft.Request("first");
        var second = draft.Request("second");
        draft.Failed(first);
        Assert.AreEqual("second", draft.Restore(), "the second is still on its way");
        draft.Completed(second);
        draft.Reset();
        Assert.AreEqual("second", draft.Show(0, "second"));
    }

    [TestMethod]
    public void ARefusedDraftBelongsToItsSongOnly()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "a");
        var request = draft.Request("refused");
        draft.Failed(request);
        draft.Reset();
        Assert.AreEqual("b", draft.Show(1, "b"));
        Assert.IsFalse(draft.NeedsSave("b"));
    }

    [TestMethod]
    public void ADraftBelongsToTheProjectItWasTypedIn()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved", project: 1);
        var request = draft.Request("typed in the old project");
        Assert.AreEqual(1, request.Project);
        draft.Failed(request);
        draft.Reset();
        // The project was replaced: song 0 of the new project is another song, and the old draft is not shown in its box.
        Assert.AreEqual("the new project's notes", draft.Show(0, "the new project's notes", project: 2));
        Assert.IsFalse(draft.NeedsSave("the new project's notes"));
        Assert.IsFalse(draft.HasFailed(2));
    }

    [TestMethod]
    public void ASaveForAReplacedProjectNeverChangesWhatTheBoxHolds()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "old", project: 1);
        var request = draft.Request("typed");
        draft.Reset();
        draft.Show(0, "new", project: 2);
        draft.Completed(request);
        Assert.AreEqual("new", draft.Saved);
        Assert.IsFalse(draft.NeedsSave("new"));
    }

    [TestMethod]
    public void ADraftTheProjectRefusedIsAskedForAgainWhenItsPageIsGone()
    {
        var draft = new StageNotesDraft();
        draft.Show(1, "saved", project: 7);
        var request = draft.Request("typed");
        draft.Failed(request);
        draft.Reset();
        Assert.IsTrue(draft.HasFailed(7), "closing or replacing the project must not lose it");

        var retry = draft.RetryFailed(7);
        Assert.IsNotNull(retry);
        Assert.AreEqual("typed", retry.Value.Text);
        Assert.AreEqual(1, retry.Value.Song);
        Assert.AreEqual(7, retry.Value.Project);
        Assert.IsTrue(retry.Value.Id > request.Id);

        draft.Completed(retry.Value);
        Assert.IsFalse(draft.HasFailed(7));
        Assert.IsNull(draft.RetryFailed(7));
    }

    [TestMethod]
    public void ARetryThatFailsAgainKeepsTheDraft()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved", project: 3);
        draft.Failed(draft.Request("typed"));
        draft.Reset();
        var retry = draft.RetryFailed(3)!.Value;
        draft.Failed(retry);
        Assert.IsTrue(draft.HasFailed(3));
        Assert.AreEqual("typed", draft.Show(0, "saved", project: 3), "a page built later still shows it");
    }

    [TestMethod]
    public void ADraftFromAReplacedProjectIsDroppedNotRetried()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved", project: 4);
        draft.Failed(draft.Request("typed"));
        draft.Reset();
        Assert.IsNull(draft.RetryFailed(5));
        Assert.IsFalse(draft.HasFailed(4), "and it is forgotten");
        Assert.IsNull(draft.RetryFailed(4));
    }

    [TestMethod]
    public void NothingFailedMeansNothingToRetry()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved");
        Assert.IsFalse(draft.HasFailed(0));
        Assert.IsNull(draft.RetryFailed(0));
    }

    [TestMethod]
    public void AskingWithTextTheProjectAlreadyHoldsSavesNothingAndDropsAnEarlierFailedDraft()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved");
        draft.Failed(draft.Request("typed"));
        Assert.IsTrue(draft.HasFailed(0));
        Assert.IsNull(draft.Ask("saved"), "the box is back to what the project holds");
        Assert.IsFalse(draft.HasFailed(0), "so the draft that failed is no longer wanted");
        draft.Reset();
        Assert.AreEqual("saved", draft.Show(0, "saved"), "and a rebuilt page does not bring it back");
    }

    [TestMethod]
    public void AskingWithANewDraftReturnsARequestAndWithTheSameDraftAgainReturnsNone()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved");
        var request = draft.Ask("typed");
        Assert.IsNotNull(request);
        Assert.AreEqual("typed", request.Value.Text);
        Assert.AreEqual(0, request.Value.Song);
        Assert.IsNull(draft.Ask("typed"), "already on its way");
    }

    [TestMethod]
    public void AskingWithTheDraftThatFailedRetriesIt()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved");
        draft.Failed(draft.Request("typed"));
        var retry = draft.Ask("typed");
        Assert.IsNotNull(retry);
        draft.Completed(retry.Value);
        Assert.IsFalse(draft.HasFailed(0));
        Assert.AreEqual("typed", draft.Saved);
    }

    [TestMethod]
    public void ANewerRequestSupersedesADraftThatFailedBeforeIt()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved", project: 3);
        draft.Failed(draft.Request("A"));
        Assert.IsTrue(draft.HasFailed(3));
        var newer = draft.Request("B");
        Assert.IsFalse(draft.HasFailed(3), "B stands for the newest words; the draft that failed is out of date");

        // Leaving Stage and closing or saving asks for a retry while B is still on its way: there is nothing to retry, so the
        // older A can never be queued behind B and land on top of it.
        draft.Reset();
        Assert.IsNull(draft.RetryFailed(3));
        Assert.AreEqual("B", draft.Show(0, "saved", project: 3), "a page built now shows B, which is on its way");
        draft.Completed(newer);
        Assert.AreEqual("B", draft.Saved);
        Assert.IsNull(draft.RetryFailed(3));
    }

    [TestMethod]
    public void ARequestForAnotherSongLeavesAFailedDraftAlone()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved", project: 3);
        draft.Failed(draft.Request("A"));
        draft.Reset();
        draft.Show(1, "other", project: 3);
        draft.Request("typed for the second song");
        Assert.IsTrue(draft.HasFailed(3), "the first song's draft is still unsaved");
    }

    [TestMethod]
    public void WhenTheNewerRequestFailsToo()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved", project: 3);
        draft.Failed(draft.Request("A"));
        var newer = draft.Request("B");
        draft.Failed(newer);
        Assert.IsTrue(draft.HasFailed(3));
        draft.Reset();
        Assert.AreEqual("B", draft.Show(0, "saved", project: 3), "it is B that is remembered, not A");
    }

    [TestMethod]
    public void NothingIsAskedForWithoutASong()
    {
        var draft = new StageNotesDraft();
        draft.Show(-1, "");
        Assert.IsNull(draft.Ask("anything"));
    }

    [TestMethod]
    public void AnAbandonedRetryLeavesNothingAskingForASaveThatCannotHappen()
    {
        var draft = new StageNotesDraft();
        draft.Show(2, "saved", project: 5);
        draft.Failed(draft.Request("typed"));
        draft.Reset();
        var retry = draft.RetryFailed(5)!.Value;
        draft.Abandoned(retry);
        Assert.IsFalse(draft.HasFailed(5), "closing or replacing the project is not blocked for ever by it");
        Assert.IsNull(draft.RetryFailed(5));
    }

    [TestMethod]
    public void AnAbandonedRequestIsNoLongerOnItsWayAndIsNotRememberedAsFailed()
    {
        var draft = new StageNotesDraft();
        draft.Show(0, "saved");
        var request = draft.Request("typed");
        draft.Abandoned(request);
        Assert.IsTrue(draft.NeedsSave("typed"), "the box still holds text the project does not have");
        Assert.IsFalse(draft.HasFailed(0));
    }
}
