namespace ResonantPulse.App.Audio;

/// <summary>
/// A live edit was prepared for a playback session that was stopped, restarted or disposed in the meantime. The edit is
/// still a valid document change: callers keep it and treat the transport as not playing it.
/// </summary>
public sealed class LiveSessionChangedException()
    : InvalidOperationException(ResonantPulse.App.Localization.Text.Get("SoundSessionChanged"));

/// <summary>Result of <c>AudioTransport.UpdateLiveAsync</c>.</summary>
public enum LiveEditOutcome
{
    /// <summary>No active revision, or the transport is Stopped/Completed/Faulted. The caller keeps its normal pending-edit behaviour.</summary>
    NotPlaying,
    /// <summary>The requested live settings equal the ones already active (or already queued). A queued request that contradicted them was superseded.</summary>
    NoChange,
    /// <summary>Only sound/accents changed: they switch at the next audio quantum on the same musical clock (also during a finite program).</summary>
    SoundApplied,
    /// <summary>Tempo, meter, unit, subdivision, swing, groups, gaps...: queued and spliced in at the next pulse boundary. Newer requests replace it until then.</summary>
    StructuralQueued,
    /// <summary>
    /// A finite program is playing and the request differs structurally (tempo, meter, unit, subdivision, swing,
    /// groups, gap bars): restart playback to apply it. Sound/accent/blind-gap changes in the same request are
    /// still applied live; sound-only requests never return this.
    /// </summary>
    RequiresRestart
}
