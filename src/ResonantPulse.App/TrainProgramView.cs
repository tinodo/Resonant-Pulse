using ResonantPulse.Core;

namespace ResonantPulse.App;

/// <summary>How the Train page's program card describes the difference between what plays and what the project holds.</summary>
internal enum ProgramNote
{
    /// <summary>What plays is what the project holds, or nothing plays.</summary>
    None,
    /// <summary>The project's program was changed since the start: the steps shown are heard from the next Start.</summary>
    Changed,
    /// <summary>The project's program was removed since the start: the one that plays keeps going until Stop.</summary>
    Removed
}

/// <summary>
/// What the program card of the Train page shows. Studio edits are heard from the next Start, so the program that plays can
/// be one the project no longer holds: its status, progress and chart stay for as long as it plays, and a note says which is
/// which. Without a program to follow, or to be followed, the card says how to get one.
/// </summary>
internal readonly record struct TrainProgramView(bool Readouts, ProgramNote Note)
{
    /// <param name="edited">The program of the project as it is now (the steps listed on the page).</param>
    /// <param name="playing">The program of the revision that is playing or paused, or null when nothing plays.</param>
    public static TrainProgramView Of(IReadOnlyList<ProgramStep> edited, IReadOnlyList<ProgramStep>? playing)
    {
        var readouts = edited.Count > 0 || playing is { Count: > 0 };
        var note = playing is null || playing.SequenceEqual(edited) ? ProgramNote.None
            : edited.Count == 0 ? ProgramNote.Removed : ProgramNote.Changed;
        return new(readouts, note);
    }

    /// <summary>
    /// A run that has completed (it reached its end or a practice limit) keeps its revision until Stop, but nothing plays any
    /// more: it counts as no program playing, so a program removed in Studio does not keep claiming to play.
    /// </summary>
    /// <param name="edited">The program of the project as it is now.</param>
    /// <param name="retained">The program of the revision the transport holds, or null when it holds none.</param>
    /// <param name="runCompleted">The transport's run has completed.</param>
    public static TrainProgramView Of(IReadOnlyList<ProgramStep> edited, IReadOnlyList<ProgramStep>? retained, bool runCompleted) =>
        Of(edited, runCompleted ? null : retained);

    /// <summary>
    /// A completed run keeps its plan until Stop. When the program it played is not the one in the project (Studio changed it
    /// meanwhile), that plan says nothing about the steps listed beside it, so the card's status, progress and chart go back to
    /// the project's own program, idle, as after Stop. A finished run of the program that is still in the project keeps its
    /// readouts: its final position and progress are true.
    /// </summary>
    public static bool FinishedRunIsStale(IReadOnlyList<ProgramStep> edited, IReadOnlyList<ProgramStep>? retained, bool runCompleted) =>
        runCompleted && retained is not null && !retained.SequenceEqual(edited);
}
