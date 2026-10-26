using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class TempoReadoutTests
{
    // document tempo 100, audible revision tempo 80, program position tempo 95
    private static double Show(bool audibleProgram, bool documentProgram, double? requested = null, double? position = null) =>
        TempoReadout.Select(audibleProgram, documentProgram, requested, 100, position, 80);

    [TestMethod]
    public void WithoutAnyProgramTheReadoutShowsWhatThePlayerAskedFor()
    {
        Assert.AreEqual(100, Show(false, false));
        Assert.AreEqual(123, Show(false, false, requested: 123));
    }

    [TestMethod]
    public void ARunningProgramKeepsShowingItsAudibleTempoAfterTheLastStepIsRemoved()
    {
        Assert.AreEqual(95, Show(audibleProgram: true, documentProgram: false, requested: 123, position: 95));
        Assert.AreEqual(80, Show(audibleProgram: true, documentProgram: false, requested: 123, position: null),
            "Without a position the audible revision's own tempo is used, never the edited document's.");
    }

    [TestMethod]
    public void ARunningRevisionWithoutAProgramKeepsShowingItsAudibleTempoAfterAProgramIsAdded()
    {
        Assert.AreEqual(95, Show(audibleProgram: false, documentProgram: true, requested: 123, position: 95));
        Assert.AreEqual(80, Show(audibleProgram: false, documentProgram: true, position: null));
    }

    [TestMethod]
    public void AStoppedProgramDocumentShowsItsOwnTempoWhenThereIsNoPosition()
    {
        // Stopped: the "audible" settings are the document's own.
        Assert.AreEqual(100, TempoReadout.Select(true, true, null, 100, null, 100));
        Assert.AreEqual(95, TempoReadout.Select(true, true, null, 100, 95, 100));
    }
}
