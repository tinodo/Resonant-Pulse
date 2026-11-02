namespace ResonantPulse.App;

/// <summary>
/// Where the keyboard focus starts. When Windows activates the window the navigation holds the Settings item and nothing else (its pages are
/// added once the project has been read), so the focus lands on Settings and its ring is the first thing that is seen. Once the page is built
/// the focus is placed once, on what a person starts from: Start, or Audio while there is no output to start on (the status line says so). A
/// focus that is anywhere else was put there by the person (Tab, a click, a screen reader) and is not taken from them.
/// </summary>
internal static class InitialFocus
{
    internal enum Place { None, Start, Audio }

    /// <summary>
    /// Start when it can take the focus. Without an output it is off, and Audio is where one is chosen, so the focus goes there; with neither
    /// there is nothing to give it to.
    /// </summary>
    internal static Place Choose(bool startCanTakeIt, bool audioCanTakeIt) =>
        startCanTakeIt ? Place.Start : audioCanTakeIt ? Place.Audio : Place.None;

    /// <summary>
    /// Whether the focus is placed now: only while the person has not pressed a key or the pointer yet, and only while the focus is still
    /// where the system left it (on nothing, or on an item of the navigation). Anything else is the person's choice.
    /// </summary>
    internal static bool Takes(bool personHasActed, bool focusIsWhereTheSystemLeftIt) =>
        !personHasActed && focusIsWhereTheSystemLeftIt;
}
