namespace ResonantPulse.App;

internal enum CaptionPalette
{
    /// <summary>White caption icons for the dark theme.</summary>
    LightOnDark,
    /// <summary>Black caption icons for the light theme.</summary>
    DarkOnLight,
    /// <summary>Colors owned by Windows: a Contrast theme pairs its own caption colors.</summary>
    System
}

internal static class CaptionPalettes
{
    /// <summary>
    /// Forced black or white caption icons ignore Windows Contrast themes (the app theme only says light or dark),
    /// so a light contrast background with the dark theme selected would leave unreadable white icons. Under a Contrast
    /// theme the caption buttons therefore return to the system's own colors.
    /// </summary>
    internal static CaptionPalette Choose(bool highContrast, bool lightTheme) =>
        highContrast ? CaptionPalette.System : lightTheme ? CaptionPalette.DarkOnLight : CaptionPalette.LightOnDark;
}
