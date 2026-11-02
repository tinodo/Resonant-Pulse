namespace ResonantPulse.App;

/// <summary>What the page does with its notice when the time of a transient one has run out (<see cref="SavingNotice.WhenTransientNoticeFades"/>).</summary>
internal enum FadedNotice
{
    /// <summary>Nothing: a warning or an error stays until it is dismissed.</summary>
    Keep,
    /// <summary>The notice closes.</summary>
    Close,
    /// <summary>The notice that says the tracks are being saved comes back.</summary>
    ShowSaving
}

/// <summary>
/// The notice that says the tracks are being saved (<c>StemExportSaving</c>), and what shares the notice bar with it. The export of separate
/// tracks is the one write that shows a notice while it writes: the others show none (they are short for an ordinary project; see docs/DELIVERY.md).
/// </summary>
internal static class SavingNotice
{
    /// <summary>
    /// What the page does when the time of a transient notice (Saved, a restored session) has run out. Only an informational notice fades: a
    /// warning or an error stays until it is dismissed, so nothing replaces it. A transient notice can have replaced the one that says the
    /// tracks are being saved (that one has no time of its own), so while the archive is being written it comes back.
    /// </summary>
    internal static FadedNotice WhenTransientNoticeFades(bool informational, bool tracksAreBeingWritten) =>
        !informational ? FadedNotice.Keep : tracksAreBeingWritten ? FadedNotice.ShowSaving : FadedNotice.Close;

    /// <summary>
    /// Whether the notice that says the tracks are being saved may take the place of what the notice bar shows now. It may not replace a
    /// warning or an error that is open: that is a failure that still needs attention. It takes the bar when it is free or shows an
    /// informational notice, and it comes back when a warning or an error that hid it is dismissed (see the page's notice bar).
    /// </summary>
    internal static bool MayReplace(bool noticeIsOpen, bool noticeIsInformational) => !noticeIsOpen || noticeIsInformational;

    /// <summary>
    /// Whether the notice that a write has ended (<c>Saved</c>) may take the place of what the notice bar shows now. A write that begins when
    /// no close was refused before it, or after one was, always may, as it always did: a person who tries again after an error (saves
    /// somewhere else, as the error may say) is told that it worked, and the old error goes. A write that was going when a close was refused
    /// (<see cref="FileWrite.LeftGoingByRefusedClose"/>) is left alone and ends later, while the bar says why the window stayed (an error
    /// stays until it is dismissed, and the second close leaves even when it cannot save): it does not replace a warning or an error that is
    /// open (the rule of <see cref="MayReplace"/>), so that the person dismisses the reason and has not lost what it said. An informational
    /// notice still gives way, as it does to every notice.
    /// </summary>
    internal static bool SavedMayReplace(bool noticeIsOpen, bool noticeIsInformational, bool writeWasLeftGoingByARefusedClose) =>
        !writeWasLeftGoingByARefusedClose || MayReplace(noticeIsOpen, noticeIsInformational);
}
