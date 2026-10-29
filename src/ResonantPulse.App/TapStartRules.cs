using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

/// <summary>Why "Tap to start" cannot be switched on right now.</summary>
internal enum TapStartBlock
{
    None,
    /// <summary>The metronome is playing or paused: it has to be stopped first.</summary>
    Playing,
    /// <summary>A start or another operation is under way.</summary>
    Busy,
    /// <summary>No output device is chosen, so nothing could play.</summary>
    NoOutput
}

/// <summary>What the person sees while "Tap to start" is on.</summary>
internal enum TapStartPhase
{
    Off,
    /// <summary>Listening: the taps so far are counted.</summary>
    Counting,
    /// <summary>The last tap was not steady with the others, so the count starts again.</summary>
    Uneven,
    /// <summary>The last tap was taken: the tempo is being saved and the audio prepared.</summary>
    Starting
}

/// <summary>
/// The rules of the "Tap to start" controls that do not need a window: what the switch and the TAP button may do now and what
/// they say, so that the same words and the same answers are tested instead of being spread over the page's code.
/// </summary>
internal static class TapStartRules
{
    internal const int MinimumTaps = 2;
    internal const int MaximumTaps = 16;
    /// <summary>One bar of 4/4.</summary>
    internal const int DefaultTaps = 4;

    /// <summary>
    /// A pause longer than this between two taps begins a new count, whatever the beat was. (The estimator's own rule is the longer
    /// of this and three beats, which lets slow taps wait more than four seconds; Tap to start does not.)
    /// </summary>
    internal const double IdleSeconds = 4;

    /// <summary>The number of taps to use. A settings file is untrusted input, so whatever it holds, the result is in range.</summary>
    internal static int ClampTaps(int taps) => Math.Clamp(taps, MinimumTaps, MaximumTaps);

    /// <summary>
    /// What prevents the switch from going on, or <see cref="TapStartBlock.None"/>. When several things apply the first one the
    /// person can act on is named: stop the metronome, wait for what is running, then choose an output.
    /// </summary>
    internal static TapStartBlock Block(bool transportIdle, bool busy, bool hasOutput) =>
        !transportIdle ? TapStartBlock.Playing :
        busy ? TapStartBlock.Busy :
        !hasOutput ? TapStartBlock.NoOutput : TapStartBlock.None;

    /// <summary>
    /// A program decides the tempo, so a plain tap would change nothing and TAP stays off; while "Tap to start" listens it is the
    /// way to give the tempo the program is scaled to.
    /// </summary>
    internal static bool TapEnabled(bool followsProgram, bool listening) => !followsProgram || listening;

    internal static TapStartPhase PhaseOf(TapInSession session) =>
        session.IsPending ? TapStartPhase.Starting :
        session.IsArmed ? (session.LastTapRejected ? TapStartPhase.Uneven : TapStartPhase.Counting) :
        TapStartPhase.Off;

    /// <summary>The state in a form that assistive technology and the native verifier can read without parsing words.</summary>
    internal static string Status(TapStartPhase phase, int validTaps, int requiredTaps) => phase switch
    {
        TapStartPhase.Off => "idle",
        TapStartPhase.Starting => "preparing",
        _ => FormattableString.Invariant($"armed:{validTaps}:{requiredTaps}:{(phase == TapStartPhase.Uneven ? 1 : 0)}")
    };

    /// <summary>The text under TAP while "Tap to start" is on (the key and its arguments), or null when it is off.</summary>
    internal static (string Key, object[] Args)? Caption(TapStartPhase phase, int validTaps, int requiredTaps) => phase switch
    {
        TapStartPhase.Counting => ("TapStartCount", new object[] { validTaps, requiredTaps }),
        TapStartPhase.Uneven => ("TapStartRejected", Array.Empty<object>()),
        TapStartPhase.Starting => ("TapStartStarting", Array.Empty<object>()),
        _ => null
    };

    /// <summary>
    /// What a screen reader is told after a tap: the count (or "Tap again") and, when the run has a tempo, that tempo. It is the
    /// tempo the dial shows (<see cref="TapInSession.ShownTempo"/>), not whatever the estimator still holds from a run that an
    /// uneven tap has ended.
    /// </summary>
    internal static string Announcement(TapInSession session)
    {
        var phase = PhaseOf(session);
        var counted = Caption(phase, session.ValidTaps, session.RequiredTaps) is { } shown ? Text.Format(shown.Key, shown.Args) : "";
        if (session.ShownTempo is { } tempo)
            counted += (counted.Length > 0 ? ", " : "") + Text.Format("TempoAnnouncement", TempoFormat.Format(tempo, Text.Culture));
        return counted;
    }

    /// <summary>The tooltip of the switch: what it does when it can be used, why it cannot, or what to do while it is on.</summary>
    internal static string HelpKey(TapStartPhase phase, TapStartBlock block, bool followsProgram) =>
        phase != TapStartPhase.Off ? "TapStartActive" :
        block == TapStartBlock.Playing ? "TapStartStopFirst" :
        block == TapStartBlock.NoOutput ? "ChooseOutput" :
        followsProgram ? "TapStartProgramHelp" : "TapStartHelp";
}
