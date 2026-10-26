using ResonantPulse.App.Audio;

namespace ResonantPulse.App;

/// <summary>
/// Which edits follow a running transport at once, and when the audio has been left behind the document so that
/// only Stop + Start can catch up (the "pending edits" notice).
/// </summary>
internal static class LiveEditPolicy
{
    /// <summary>Kinds whose settings are live-eligible: sound, accents, tempo, meter, groups, feel and gap bars.</summary>
    internal static bool IsLive(EditorChangeKind kind) =>
        kind is EditorChangeKind.SoundAndAccentsOnly or EditorChangeKind.GlobalTempoOnly or EditorChangeKind.PlayStructure;

    /// <summary>
    /// True when the document now differs from what plays in a way only Stop + Start resolves.
    /// </summary>
    /// <param name="kind">What the edit changed.</param>
    /// <param name="affectsPlayback">Whether the edit concerns the audio at all (a project rename does not).</param>
    /// <param name="holdsRevision">The transport is Playing, Paused or Faulted, so it holds a revision of its own.</param>
    /// <param name="outcome">What <c>UpdateLiveAsync</c> reported for a live kind.</param>
    /// <param name="countInChanged">The count-in length differs from the running revision (it is heard at the next Start).</param>
    internal static bool LeavesPendingEdit(EditorChangeKind kind, bool affectsPlayback, bool holdsRevision,
        LiveEditOutcome outcome, bool countInChanged)
    {
        if (!holdsRevision) return false;
        if (!IsLive(kind)) return affectsPlayback;
        // NotPlaying while a revision is held means Faulted: the retained revision resumes with the old settings.
        return outcome is LiveEditOutcome.NotPlaying or LiveEditOutcome.RequiresRestart || countInChanged;
    }
}
