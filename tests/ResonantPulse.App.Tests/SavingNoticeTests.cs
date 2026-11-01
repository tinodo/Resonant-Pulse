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
}
