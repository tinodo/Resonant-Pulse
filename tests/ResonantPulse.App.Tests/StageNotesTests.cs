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
        var song = draft.Request("first");
        draft.Request("second");
        draft.Completed(song, "first");
        Assert.AreEqual("first", draft.Saved);
        Assert.AreEqual("second", draft.Restore(), "the second is still on its way");
        Assert.IsFalse(draft.NeedsSave("second"));
        draft.Completed(song, "second");
        Assert.AreEqual("second", draft.Saved);
        Assert.IsFalse(draft.NeedsSave("second"));
    }

    [TestMethod]
    public void AFailedSaveLeavesTheDraftAskableAgainAndEscPutsBackWhatTheProjectHolds()
    {
        var draft = new StageNotesDraft();
        draft.Show(1, "kept");
        var song = draft.Request("typed");
        draft.Failed(song, "typed");
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
        var song = draft.Request("new");
        draft.Reset();
        Assert.AreEqual("new", draft.Show(0, "old"), "the project has not got it yet, but it is on its way");
        draft.Completed(song, "new");
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
        draft.Completed(first, "a2");
        Assert.AreEqual("b", draft.Saved);
        Assert.IsFalse(draft.NeedsSave("b"));
    }
}
