namespace ResonantPulse.App;

/// <summary>
/// Whether the Settings item of the top navigation has room for its name. The pane gives that item what is left after the item of the
/// selected page and a fixed overhead, and clips the name when that is less: at 320 DIP wide the Dutch, German and French names were cut
/// ("Einstellu"), and the longer the name of the selected page the more of it. Where the name does not fit the item shows its gear alone; the
/// name stays as its tooltip and as its name for a screen reader.
/// The two numbers were measured on the navigation of this Windows App SDK at 100 % (UI Automation: the width of each item against the width of
/// its name): an item adds about 48.7 DIP to its name (icon, gap and padding), and the pane keeps 108 DIP for itself (its padding at both ends,
/// the overflow button and the least space between the two sides). The first-use verifier compares the width of the Settings item on every
/// page in every language, so that a change of the navigation, or of the numbers, shows there.
/// </summary>
internal static class NavigationFit
{
    /// <summary>What an item adds to the width of its name: its icon, the gap after it and its padding (DIP).</summary>
    internal const double ItemChrome = 48.7;

    /// <summary>What the pane keeps for itself: its padding at both ends, the overflow button and the least space between the sides (DIP).</summary>
    internal const double PaneOverhead = 108;

    /// <summary>Room left over, so that a name that fits exactly is not decided by a rounding.</summary>
    internal const double Slack = 2;

    /// <summary>
    /// True when the name of the Settings item fits beside the item of any page: the pane is at least as wide as its overhead, the item of the
    /// page with the longest name (every page can be the selected one, so the answer does not change with the page) and the item of Settings.
    /// </summary>
    internal static bool SettingsNameFits(double paneWidth, double widestPageName, double settingsName) =>
        paneWidth >= PaneOverhead + 2 * ItemChrome + widestPageName + settingsName + Slack;
}
