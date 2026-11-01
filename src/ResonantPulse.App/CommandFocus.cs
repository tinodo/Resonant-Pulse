namespace ResonantPulse.App;

/// <summary>
/// What happens to the keyboard focus of a command that is switched off while it has the focus. The command of the tracks export is off from
/// the press to the end of the export. When a control that has the focus is switched off, the system moves the focus to the next control (on
/// the Library page: the first ready-made rhythm), a question that is asked meanwhile gives the focus back to that control when it closes, and
/// nothing puts it back on the command: a keyboard user, who pressed the command with Enter, has lost their place on the page.
/// </summary>
internal static class CommandFocus
{
    /// <summary>
    /// Whether the command takes the focus back when it is on again. Not decided yet: the focus stays where the system left it, as it did
    /// before this was written down.
    /// </summary>
    internal static bool TakesFocusBack(bool hadFocusAtPress, bool commandCanTakeIt, bool focusIsWhereTheSystemLeftIt) => false;
}
