namespace ResonantPulse.App;

/// <summary>Chooses the tempo that the dial, the stage readout and the program status show.</summary>
internal static class TempoReadout
{
    /// <summary>
    /// While neither the audible revision nor the edited document plays a program, the readout shows what the player
    /// asked for (the audible revision follows at the next click). Otherwise it shows the audible tempo: a running
    /// program keeps its own tempo until Stop and Start even when the document no longer has one, and a running
    /// revision without a program keeps its tempo even when the document has just gained one.
    /// </summary>
    internal static double Select(bool audibleHasProgram, bool documentHasProgram, double? requested,
        double documentTempo, double? audibleTempo, double audibleSettingsTempo) =>
        !audibleHasProgram && !documentHasProgram ? requested ?? documentTempo : audibleTempo ?? audibleSettingsTempo;
}
