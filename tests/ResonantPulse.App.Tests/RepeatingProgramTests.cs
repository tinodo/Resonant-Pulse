using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;
using static ResonantPulse.App.Tests.LiveTestKit;

namespace ResonantPulse.App.Tests;

/// <summary>
/// A program that is asked to repeat plays on from its first step after its last, until Stop. The engine already loops an open-ended
/// project by playing its first loop inside the initial plan and then its body again and again; for a program the body is one whole pass
/// of it, so these tests hold that the pass that follows the count-in is the one that repeats, and that nothing else about a program
/// (its stop boundary aside) changes.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RepeatingProgramTests
{
    private const int Rate = 8000;

    /// <summary>Count-in of one bar and the program: 2 bars at 100 BPM, then 1 bar at 120 BPM twice (4 bars, 1.2 s and 8000 frames to the bar).</summary>
    private static PulseDocument Program(bool repeat)
    {
        var document = Document(120);
        document.Program =
        [
            new ProgramStep { Name = "Slow", Bars = 2, StartTempo = 100, EndTempo = 100 },
            new ProgramStep { Name = "Quick", Bars = 1, Repeats = 2, StartTempo = 120, EndTempo = 120 }
        ];
        document.RepeatProgram = repeat;
        return document;
    }

    [TestMethod]
    public void AProgramEndsAfterItsLastStepUnlessItIsAskedToRepeat()
    {
        var ends = Revision(Program(false), Rate);
        Assert.IsTrue(ends.HasProgram);
        Assert.IsTrue(ends.IsFinite);
        Assert.AreEqual(ends.Initial.TotalFrames, ends.EndFrame);

        var repeats = Revision(Program(true), Rate);
        Assert.IsTrue(repeats.HasProgram);
        Assert.IsFalse(repeats.IsFinite);
        Assert.IsNull(repeats.EndFrame, "nothing ends it but Stop");
    }

    [TestMethod]
    public void TheSwitchMeansNothingWithoutAProgram()
    {
        var open = Revision(new PulseDocument { RepeatProgram = true }, Rate);
        Assert.IsFalse(open.HasProgram);
        Assert.IsFalse(open.IsFinite, "a project with no program loops, with or without the switch");
        Assert.IsNull(open.EndFrame);
    }

    [TestMethod]
    public void ThePassThatFollowsTheCountInIsTheOneThatRepeats()
    {
        var revision = Revision(Program(true), Rate);
        var countIn = revision.CountInFrames;
        Assert.AreEqual(revision.Initial.TotalFrames - countIn, revision.Body.TotalFrames,
            "the initial plan is the count-in and one pass; the body is that pass again");
        var first = revision.Initial.Onsets.Where(onset => onset.Frame >= countIn)
            .Select(onset => (Frame: onset.Frame - countIn, onset.LaneIndex, onset.Accent)).ToArray();
        var again = revision.Body.Onsets.Select(onset => (onset.Frame, onset.LaneIndex, onset.Accent)).ToArray();
        Assert.IsTrue(first.Length > 0);
        CollectionAssert.AreEqual(first, again, "the second pass has the clicks of the first, on the same frames");
        CollectionAssert.AreEqual(
            revision.Initial.SectionBoundaries.Where(section => section.StepIndex >= 0).Select(section => (section.Name, section.Bars)).ToArray(),
            revision.Body.SectionBoundaries.Select(section => (section.Name, section.Bars)).ToArray());
    }

    [TestMethod]
    public void TheProgramKeepsPlayingPastItsEndAndTheBarsKeepCounting()
    {
        var revision = Revision(Program(true), Rate);
        var timeline = new LiveTimeline(revision);
        var pass = revision.Body.TotalFrames;

        var lastOfTheFirstPass = LiveSplice.Present(timeline, revision.Initial.TotalFrames - 1, false);
        Assert.AreSame(revision.Initial, lastOfTheFirstPass.Plan);
        Assert.AreEqual((4L, 4), BarAndBeat(lastOfTheFirstPass), "the last beat of the fourth bar");

        var secondPass = LiveSplice.Present(timeline, revision.Initial.TotalFrames + 10, false);
        Assert.AreSame(revision.Body, secondPass.Plan);
        Assert.AreEqual(10L, secondPass.MusicalFrame);
        Assert.AreEqual((5L, 1), BarAndBeat(secondPass), "bar 1 of the program is bar 5 of the run");
        Assert.AreEqual("Slow", secondPass.Plan.PositionAtFrame(secondPass.MusicalFrame).SectionName, "it starts over from the first step");

        var thirdPass = LiveSplice.Present(timeline, revision.Initial.TotalFrames + pass + 10, false);
        Assert.AreEqual(10L, thirdPass.MusicalFrame);
        Assert.AreEqual((9L, 1), BarAndBeat(thirdPass));
    }

    [TestMethod]
    public void TheSecondPassSoundsLikeTheFirst()
    {
        var revision = Revision(Program(true), Rate);
        var passFrames = revision.Body.TotalFrames;
        var total = revision.Initial.TotalFrames + passFrames + 2000;
        var run = new LiveRun(revision, total);
        run.RunTo(total, 1000);
        // The first beats of a pass still hold what rang out of the one before, so the start of each is left out.
        const int margin = 4000;
        var first = run.Pcm.AsSpan((int)((revision.CountInFrames + margin) * 2), (int)((passFrames - margin) * 2)).ToArray();
        var second = run.Pcm.AsSpan((int)((revision.Initial.TotalFrames + margin) * 2), (int)((passFrames - margin) * 2)).ToArray();
        Assert.IsTrue(first.Any(sample => sample != 0), "the first pass sounds");
        CollectionAssert.AreEqual(first, second, "the second pass sounds as the first did");
    }

    [TestMethod]
    public void ACountedLimitCountsThroughTheRepeats()
    {
        var document = Program(true);
        document.Settings.PracticeBars = 6;
        var revision = Revision(document, Rate);
        // Four bars in a pass: the limit falls two bars into the second one, the end of the two bars at 100 BPM (19200 frames each at 8 kHz).
        Assert.AreEqual(revision.Initial.TotalFrames + 2 * 19200L, revision.EndFrame);

        var ends = Program(false);
        ends.Settings.PracticeBars = 6;
        Assert.AreEqual(Revision(ends, Rate).Initial.TotalFrames, Revision(ends, Rate).EndFrame, "a program that ends is not made longer by a limit");
    }

    [TestMethod]
    public void AStructuralLiveEditWaitsForTheNextStartForAProgramThatRepeatsAsForOneThatEnds()
    {
        foreach (var repeat in new[] { false, true })
        {
            var revision = Revision(Program(repeat), Rate);
            var decision = LiveSettingsEdits.Decide(revision, null, Requested(Document(90, edit: s => s.Subdivision = 2)));
            Assert.AreEqual(LiveEditOutcome.RequiresRestart, decision.Outcome, $"repeat {repeat}");
            Assert.IsNull(LiveSplice.NextPulseBoundary(revision, 0), $"repeat {repeat}: no pulse grid of its own to splice on");

            var queue = new LiveEditQueue();
            queue.Post(Revision(Document(90), Rate));
            var timeline = new LiveTimeline(revision);
            Assert.AreSame(timeline, queue.Advance(timeline, 0, 1000, 0), $"repeat {repeat}");
            Assert.IsNull(queue.ArmedBoundary, $"repeat {repeat}");
        }
    }

    [TestMethod]
    public void ASoundEditStillFollowsAProgramThatRepeatsAtOnceAndKeepsItsClock()
    {
        var revision = Revision(Program(true), Rate);
        var decision = LiveSettingsEdits.Decide(revision, null, Requested(Document(120, edit: s => s.Sound = "Glass")));
        Assert.AreEqual(LiveEditOutcome.SoundApplied, decision.Outcome);
        var glass = Revision(decision.Merged, Rate);
        Assert.IsTrue(revision.HasSameClock(glass));
        Assert.IsFalse(glass.IsFinite, "the program still repeats after the edit");
        Assert.IsTrue(glass.Document.RepeatProgram);
    }

    [TestMethod]
    public void TheSwitchSurvivesACopyOfTheProject()
    {
        Assert.IsTrue(LocalWorkspace.Clone(Program(true)).RepeatProgram);
        Assert.IsFalse(LocalWorkspace.Clone(Program(false)).RepeatProgram);
    }

    [TestMethod]
    public void TheTextsOfTheSwitchAreTranslatedAndFillEveryPlace()
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var keys = new[] { "ProgramRepeat", "ProgramRepeatHelp", "TrainProgramRepeats" };
            Text.Select("en");
            var english = keys.ToDictionary(key => key, Text.Get);
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                foreach (var key in keys)
                {
                    var value = Text.Get(key);
                    Assert.AreNotEqual(key, value, $"{language}/{key} is missing");
                    Assert.IsFalse(value.Contains('{') || value.Contains('}'), $"{language}/{key} has an unfilled place: {value}");
                    if (language != "en") Assert.AreNotEqual(english[key], value, $"{language}/{key} is untranslated");
                }
                // The two sentences name the command that ends the program, as this language names it.
                var stop = Text.Get("Stop");
                StringAssert.Contains(Text.Get("ProgramRepeatHelp"), stop, language);
                StringAssert.Contains(Text.Get("TrainProgramRepeats"), stop, language);
            }
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
