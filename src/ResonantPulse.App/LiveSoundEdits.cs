using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App;

/// <summary>
/// The settings that never move the musical clock: click sound, per-pulse accents (the split clicks' too) and the blind-gaps
/// display flag (the compiler never reads it; the UI reads it from the running revision). They can follow a running transport at
/// the next audio quantum, also during a finite program.
/// </summary>
internal static class LiveSoundEdits
{
    internal static PulseDocument Apply(PulseDocument active, PulseSettings requested)
    {
        var copy = LocalWorkspace.Clone(active);
        copy.Settings.Sound = requested.Sound;
        copy.Settings.Accents = requested.Accents.ToArray();
        copy.Settings.AccentOverridesGroups = requested.AccentOverridesGroups;
        copy.Settings.ManualAccentIndices = requested.ManualAccentIndices?.ToArray();
        copy.Settings.SubdivisionAccents = requested.SubdivisionAccents?.ToArray();
        copy.Settings.BlindGaps = requested.BlindGaps;
        return copy;
    }
}
