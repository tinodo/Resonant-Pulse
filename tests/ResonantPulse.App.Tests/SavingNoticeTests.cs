using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

/// <summary>What shares the notice bar with the notice that says the tracks are being saved (the export of separate tracks, the one write that shows a notice).</summary>
[TestClass]
public sealed class SavingNoticeTests
{
    [TestMethod]
    public void WhenTheTimeOfATransientNoticeRunsOutTheNoticeOfAnArchiveThatIsBeingWrittenComesBack()
    {
        Assert.AreEqual(FadedNotice.Close, SavingNotice.WhenTransientNoticeFades(informational: true, tracksAreBeingWritten: false),
            "No archive is being written: the notice that ran out closes, as it always did.");
        Assert.AreEqual(FadedNotice.Keep, SavingNotice.WhenTransientNoticeFades(informational: false, tracksAreBeingWritten: false),
            "A warning or an error stays until it is dismissed.");
        Assert.AreEqual(FadedNotice.ShowSaving, SavingNotice.WhenTransientNoticeFades(informational: true, tracksAreBeingWritten: true),
            "Saved (from Save to library) replaced it, and it comes back.");
        Assert.AreEqual(FadedNotice.Keep, SavingNotice.WhenTransientNoticeFades(informational: false, tracksAreBeingWritten: true),
            "An error is never replaced by it.");
    }

    [TestMethod]
    public void TheNoticeOfAnArchiveThatIsBeingWrittenNeverReplacesAWarningOrAnErrorThatIsOpen()
    {
        Assert.IsTrue(SavingNotice.MayReplace(noticeIsOpen: false, noticeIsInformational: false), "A closed bar is free, whatever it showed last.");
        Assert.IsTrue(SavingNotice.MayReplace(noticeIsOpen: false, noticeIsInformational: true));
        Assert.IsTrue(SavingNotice.MayReplace(noticeIsOpen: true, noticeIsInformational: true), "An informational notice (Saved, a restored session) gives way to it.");
        Assert.IsFalse(SavingNotice.MayReplace(noticeIsOpen: true, noticeIsInformational: false),
            "A warning (a library that could not be read in full) or an error (a setting that could not be saved) that is open stays: it is a failure that still needs attention.");
    }

    [TestMethod]
    public void SavedReplacesWhateverTheBarShowsForAWriteThatNoCloseAffects()
    {
        // As it always did: a person who tries again after an error (saves somewhere else, as the error may say) is told that it worked, and
        // the old error goes, also after a close was refused: only a write that was going when the close was refused, or that ends while a
        // close is under way, is held back (the next tests).
        foreach (var open in new[] { false, true })
            foreach (var informational in new[] { false, true })
                Assert.IsTrue(SavingNotice.SavedMayReplace(open, informational, closeIsUnderWay: false, writeWasLeftGoingByARefusedClose: false),
                    $"open: {open}, informational: {informational}");
    }

    [TestMethod]
    public void AWriteThatWasGoingWhenACloseWasRefusedDoesNotReplaceAWarningOrAnErrorThatIsOpenWithSaved()
    {
        Assert.IsTrue(SavingNotice.SavedMayReplace(noticeIsOpen: false, noticeIsInformational: false, closeIsUnderWay: false, writeWasLeftGoingByARefusedClose: true), "A closed bar is free.");
        Assert.IsTrue(SavingNotice.SavedMayReplace(noticeIsOpen: false, noticeIsInformational: true, closeIsUnderWay: false, writeWasLeftGoingByARefusedClose: true));
        Assert.IsTrue(SavingNotice.SavedMayReplace(noticeIsOpen: true, noticeIsInformational: true, closeIsUnderWay: false, writeWasLeftGoingByARefusedClose: true),
            "An informational notice (the one that says the last change is still being saved) gives way, and fades by itself.");
        Assert.IsFalse(SavingNotice.SavedMayReplace(noticeIsOpen: true, noticeIsInformational: false, closeIsUnderWay: false, writeWasLeftGoingByARefusedClose: true),
            "The error that says why the window stayed is not replaced by the end of a write that was going: the person dismisses it.");
    }

    [TestMethod]
    public void WhileACloseIsUnderWayNoWriteReplacesAWarningOrAnErrorThatIsOpenWithSavedBeforeTheWritesAreMarked()
    {
        // The order of a close that is refused because the recovery copy cannot be saved while the settings save is held: the close begins,
        // the recovery save fails and its error is open, and only after the settings save has ended (the flush is over) the close is refused
        // and the writes that are going are marked. An export that ends in between is not marked yet, and must not replace that error.
        Assert.IsFalse(SavingNotice.SavedMayReplace(noticeIsOpen: true, noticeIsInformational: false, closeIsUnderWay: true, writeWasLeftGoingByARefusedClose: false),
            "The error of the recovery save is the reason the window will stay: a write that ends while the close is under way does not replace it, marked or not.");
        Assert.IsTrue(SavingNotice.SavedMayReplace(noticeIsOpen: false, noticeIsInformational: false, closeIsUnderWay: true, writeWasLeftGoingByARefusedClose: false), "A closed bar is free.");
        Assert.IsTrue(SavingNotice.SavedMayReplace(noticeIsOpen: true, noticeIsInformational: true, closeIsUnderWay: true, writeWasLeftGoingByARefusedClose: false),
            "An informational notice still gives way.");
        // The close is refused: the writes that are going are marked and the close is not under way any more.
        Assert.IsFalse(SavingNotice.SavedMayReplace(noticeIsOpen: true, noticeIsInformational: false, closeIsUnderWay: false, writeWasLeftGoingByARefusedClose: true));
        // Then the person saves again: that write began after the refusal and is told that it worked.
        Assert.IsTrue(SavingNotice.SavedMayReplace(noticeIsOpen: true, noticeIsInformational: false, closeIsUnderWay: false, writeWasLeftGoingByARefusedClose: false));
        // Whichever of the two holds a write back, it is the one rule of MayReplace for what may replace a warning or an error that is open.
        foreach (var open in new[] { false, true })
            foreach (var informational in new[] { false, true })
                foreach (var (underWay, left) in new[] { (true, false), (false, true), (true, true) })
                    Assert.AreEqual(SavingNotice.MayReplace(open, informational), SavingNotice.SavedMayReplace(open, informational, underWay, left),
                        $"open: {open}, informational: {informational}, close under way: {underWay}, left going by a refused close: {left}");
    }
}
