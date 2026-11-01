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
/// tracks is the one write that shows a notice while it writes: the others write in milliseconds for an ordinary project.
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
}
