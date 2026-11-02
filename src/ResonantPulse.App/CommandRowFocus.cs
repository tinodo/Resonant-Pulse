namespace ResonantPulse.App;

/// <summary>
/// The keyboard focus of a row of commands whose page is built again when one of them is used (the Train page, after Begin and after Finish).
/// The commands of the old page are gone with it, and a focus that went with them leaves a keyboard user on nothing: the next Tab starts from the
/// top of the page. A command that turns itself off while it has the focus hands the focus to the next one in an ordinary window; this does the
/// same for the page that is built again.
/// </summary>
internal static class CommandRowFocus
{
    /// <summary>
    /// The command that takes the focus on the new page. <paramref name="hadFocus"/> is the place in the row of the command that had it when
    /// the page was built again (null when none of them had: a focus that was anywhere else is not moved). It is the same command when that can
    /// still take the focus, otherwise the next one that can, going on in the order of the row and then back to its start; nothing when none can.
    /// </summary>
    internal static int? After(int? hadFocus, IReadOnlyList<bool> canTakeIt)
    {
        if (hadFocus is not { } start || start < 0 || start >= canTakeIt.Count) return null;
        for (var step = 0; step < canTakeIt.Count; step++)
        {
            var index = (start + step) % canTakeIt.Count;
            if (canTakeIt[index]) return index;
        }
        return null;
    }
}
