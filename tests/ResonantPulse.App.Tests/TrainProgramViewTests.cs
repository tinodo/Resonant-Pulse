using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class TrainProgramViewTests
{
    private static ProgramStep Step(string name, double tempo = 80, int bars = 8) =>
        new() { Name = name, StartTempo = tempo, EndTempo = tempo, Bars = bars };

    private static readonly ProgramStep[] Warmup = [Step("Warm up"), Step("Build", 100)];

    [TestMethod]
    public void NoProgramAnywhereShowsNoReadoutsAndNoNote()
    {
        Assert.AreEqual(new TrainProgramView(false, ProgramNote.None), TrainProgramView.Of([], null));
        Assert.AreEqual(new TrainProgramView(false, ProgramNote.None), TrainProgramView.Of([], []),
            "an open-ended revision is not a program either");
    }

    [TestMethod]
    public void AProgramInTheProjectShowsTheReadoutsWhetherOrNothingPlays()
    {
        Assert.AreEqual(new TrainProgramView(true, ProgramNote.None), TrainProgramView.Of(Warmup, null));
        Assert.AreEqual(new TrainProgramView(true, ProgramNote.None), TrainProgramView.Of(Warmup, Warmup.ToList()),
            "the same program playing needs no note");
    }

    [TestMethod]
    public void AProgramThatPlaysKeepsItsReadoutsAfterStudioRemovedItFromTheProject()
    {
        var view = TrainProgramView.Of([], Warmup);
        Assert.IsTrue(view.Readouts, "status, progress and chart follow what is still playing");
        Assert.AreEqual(ProgramNote.Removed, view.Note);
    }

    [TestMethod]
    public void AProgramChangedInStudioWhileAnotherPlaysIsAChangeNotARemoval()
    {
        var changed = new[] { Step("Warm up"), Step("Build", 120) };
        var view = TrainProgramView.Of(changed, Warmup);
        Assert.IsTrue(view.Readouts);
        Assert.AreEqual(ProgramNote.Changed, view.Note);
    }

    [TestMethod]
    public void AProgramAddedWhileAnOpenEndedRevisionPlaysIsAChange()
    {
        var view = TrainProgramView.Of(Warmup, []);
        Assert.IsTrue(view.Readouts);
        Assert.AreEqual(ProgramNote.Changed, view.Note);
    }

    [TestMethod]
    public void ComparingProgramsLooksAtEveryFieldOfEveryStep()
    {
        var other = new[] { Step("Warm up"), Step("Build", 100, bars: 16) };
        Assert.AreEqual(ProgramNote.Changed, TrainProgramView.Of(other, Warmup).Note);
        Assert.AreEqual(ProgramNote.Changed, TrainProgramView.Of(Warmup.Take(1).ToArray(), Warmup).Note, "a step removed");
    }

    [TestMethod]
    public void WhenPlayingEndsAProgramThatWasRemovedLeavesTheCard()
    {
        var whilePlaying = TrainProgramView.Of([], Warmup);
        var afterStop = TrainProgramView.Of([], null);
        Assert.AreNotEqual(whilePlaying, afterStop, "the page is rebuilt when the view changes");
        Assert.AreEqual(new TrainProgramView(false, ProgramNote.None), afterStop);
    }
}
