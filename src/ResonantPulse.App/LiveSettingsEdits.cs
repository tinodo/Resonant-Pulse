using ResonantPulse.App.Audio;
using ResonantPulse.Core;

namespace ResonantPulse.App;

internal enum LiveChange { None, Sound, Structural }

/// <summary>
/// What <c>UpdateLiveAsync</c> should do with a request. <see cref="Merged"/> is the document to compile and queue
/// (for a finite program only its sound-class settings differ from the active one); <see cref="Publish"/> says whether
/// it differs from what the transport already plays or is converging to, i.e. whether it must be compiled and queued.
/// </summary>
internal readonly record struct LiveDecision(LiveEditOutcome Outcome, PulseDocument Merged, bool Publish);

/// <summary>
/// Merges and classifies live edits. Live-eligible settings: Tempo, BeatsPerBar, BeatDenominator, BeatUnitQuarters,
/// Subdivision, Swing, Groups, Accents, AccentOverridesGroups, ManualAccentIndices, Sound, AudibleBars, SilentBars and
/// BlindGaps. Everything else (count-in, practice limits, master gain, name, lanes, program, setlist, seed) stays as in
/// the active revision. A request carries the COMPLETE live settings, so the newest request fully replaces older ones.
/// Finite programs only follow the sound-class subset (<see cref="LiveSoundEdits"/>), see <see cref="Decide"/>.
/// </summary>
internal static class LiveSettingsEdits
{
    /// <summary>A clone of <paramref name="active"/> with every live-eligible setting taken from <paramref name="requested"/>.</summary>
    internal static PulseDocument Apply(PulseDocument active, PulseSettings requested)
    {
        var copy = LiveSoundEdits.Apply(active, requested);
        var settings = copy.Settings;
        settings.Tempo = requested.Tempo;
        settings.BeatsPerBar = requested.BeatsPerBar;
        settings.BeatDenominator = requested.BeatDenominator;
        settings.BeatUnitQuarters = requested.BeatUnitQuarters;
        settings.Subdivision = requested.Subdivision;
        settings.Swing = requested.Swing;
        settings.Groups = requested.Groups.ToArray();
        settings.AudibleBars = requested.AudibleBars;
        settings.SilentBars = requested.SilentBars;
        settings.BlindGaps = requested.BlindGaps;
        return copy;
    }

    /// <summary>
    /// Sound: only Sound/Accents/AccentOverridesGroups/ManualAccentIndices differ, so the musical clock is untouched.
    /// Structural: any other live-eligible setting differs. A null and an empty manual-index list are equivalent.
    /// </summary>
    internal static LiveChange Classify(PulseSettings active, PulseSettings other)
    {
        if (active.Tempo != other.Tempo || active.BeatsPerBar != other.BeatsPerBar ||
            active.BeatDenominator != other.BeatDenominator || active.BeatUnitQuarters != other.BeatUnitQuarters ||
            active.Subdivision != other.Subdivision || active.Swing != other.Swing ||
            active.AudibleBars != other.AudibleBars || active.SilentBars != other.SilentBars ||
            active.BlindGaps != other.BlindGaps || !Same(active.Groups, other.Groups))
            return LiveChange.Structural;
        return active.Sound != other.Sound || active.AccentOverridesGroups != other.AccentOverridesGroups ||
            !Same(active.Accents, other.Accents) || !Same(active.ManualAccentIndices, other.ManualAccentIndices)
            ? LiveChange.Sound : LiveChange.None;
    }

    /// <summary>
    /// <paramref name="target"/> is the document the transport is already converging to (the latest queued request),
    /// or null when nothing is queued. A request equal to it is not recompiled; a request equal to the active revision
    /// while something else is queued IS published, so the callback can supersede the queued change.
    /// <para>
    /// The request is classified against the active revision first. A finite program (Document.Program) keeps its
    /// musical clock and stop boundary, so only the sound-class settings can follow it, exactly like the former
    /// sound-only update: <see cref="LiveChange.Sound"/> is SoundApplied, <see cref="LiveChange.None"/> is NoChange,
    /// and any structural difference is RequiresRestart. Even then the sound part of the same request is still
    /// published (the merged document never carries the structural fields), so a pending structural edit cannot
    /// stop a later sound/accent edit from being heard. For open-ended revisions a structural difference is
    /// StructuralQueued.
    /// </para>
    /// </summary>
    internal static LiveDecision Decide(PlaybackRevision active, PulseDocument? target, PulseSettings requested)
    {
        var requestedLive = Apply(active.Document, requested);
        var change = Classify(active.Document.Settings, requestedLive.Settings);
        var outcome = change switch
        {
            LiveChange.None => LiveEditOutcome.NoChange,
            LiveChange.Sound => LiveEditOutcome.SoundApplied,
            _ when active.IsFinite => LiveEditOutcome.RequiresRestart,
            _ => LiveEditOutcome.StructuralQueued
        };
        var merged = active.IsFinite ? LiveSoundEdits.Apply(active.Document, requested) : requestedLive;
        var publish = Classify((target ?? active.Document).Settings, merged.Settings) != LiveChange.None;
        return new(outcome, merged, publish);
    }

    private static bool Same<T>(T[]? first, T[]? second) => (first ?? []).SequenceEqual(second ?? []);
}
