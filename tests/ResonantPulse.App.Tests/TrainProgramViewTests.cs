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

    [TestMethod]
    public void ARunThatHasCompletedIsNotPlayingEvenThoughTheTransportStillHoldsItsRevision()
    {
        // Studio removed the program while it played; the run then finished by itself (or reached a practice limit). The
        // transport keeps the revision until Stop, but nothing plays any more.
        var completed = TrainProgramView.Of([], Warmup, runCompleted: true);
        Assert.AreEqual(new TrainProgramView(false, ProgramNote.None), completed);
        Assert.AreEqual(ProgramNote.Removed, TrainProgramView.Of([], Warmup, runCompleted: false).Note,
            "while it still plays the note stands");
    }

    [TestMethod]
    public void ACompletedRunLeavesNoNoteAboutAChangedProgram()
    {
        var changed = new[] { Step("Warm up"), Step("Build", 120) };
        Assert.AreEqual(new TrainProgramView(true, ProgramNote.None), TrainProgramView.Of(changed, Warmup, runCompleted: true));
    }

    [TestMethod]
    public void WhenTheRunCompletesTheViewChangesSoThePageIsBuiltAgain()
    {
        Assert.AreNotEqual(TrainProgramView.Of([], Warmup, runCompleted: false), TrainProgramView.Of([], Warmup, runCompleted: true));
        Assert.AreEqual(TrainProgramView.Of([], null, runCompleted: false), TrainProgramView.Of([], Warmup, runCompleted: true),
            "and a completed run looks like a stopped one");
    }
}
