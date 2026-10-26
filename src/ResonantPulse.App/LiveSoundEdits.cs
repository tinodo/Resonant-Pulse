using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App;

internal static class LiveSoundEdits
{
    internal static PulseDocument Apply(PulseDocument active, PulseSettings requested)
    {
        var copy = LocalWorkspace.Clone(active);
        copy.Settings.Sound = requested.Sound;
        copy.Settings.Accents = requested.Accents.ToArray();
        copy.Settings.AccentOverridesGroups = requested.AccentOverridesGroups;
        return copy;
    }
}
