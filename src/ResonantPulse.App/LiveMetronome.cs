using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

/// <summary>
/// What the metronome pinned to the top of the Studio page shows: the beat that sounds now, the beats that are drawn larger,
/// and what a screen reader reads. Pure, so the rules can be tested without a window.
/// </summary>
internal static class LiveMetronome
{
    /// <summary>
    /// The beat to light, counted from 0, or -1 for none. Nothing is lit while nothing plays (stopped, or finished) and in a
    /// silent gap, where nothing sounds. A paused metronome keeps the beat it stopped on.
    /// </summary>
    internal static int LitBeat(bool plays, bool silentGap, int beatIndex, int beats) =>
        !plays || silentGap || beats < 1 ? -1 : Math.Clamp(beatIndex, 0, beats - 1);

    /// <summary>The beats that begin the bar or an additive group (3+2+2): they are drawn larger, as the long ticks of the dial are.</summary>
    internal static IReadOnlySet<int> Emphasized(int beats, IReadOnlyList<int> groups)
    {
        var starts = new HashSet<int> { 0 };
        var at = 0;
        foreach (var group in groups)
        {
            starts.Add(at);
            at += group;
        }
        starts.RemoveWhere(index => index >= beats);
        return starts;
    }

    /// <summary>What a screen reader hears when it reads the metronome: the tempo, and the beat that is lit or how many beats a bar has.</summary>
    internal static string Describe(string tempo, int beats, int lit) =>
        lit >= 0 ? Text.Format("LiveBeatOf", tempo, lit + 1, beats) : Text.Format("LiveBeats", tempo, beats);
}
