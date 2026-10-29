using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.Core;
using static ResonantPulse.App.Tests.LiveTestKit;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class LiveEditQueueTests
{
    private static PlaybackRevision Prepared(PlaybackRevision active, PulseDocument target) =>
        Revision(LiveSettingsEdits.Apply(active.Document, Requested(target)), active.SampleRate);

    [TestMethod]
    public void OnlyTheNewestUnappliedRequestSurvivesSoRapidEditsCoalesce()
    {
        var first = Revision(Document(120));
        var queue = new LiveEditQueue();
        var older = Prepared(first, Document(100));
        var newer = Prepared(first, Document(90));
        queue.Post(older);
        queue.Post(newer);
        Assert.AreSame(newer, queue.Pending);

        var timeline = queue.Advance(new LiveTimeline(first), 130000, 1000, 130000);
        Assert.AreSame(first, timeline.Active, "A structural request waits for its pulse boundary.");
        Assert.AreSame(newer, queue.Pending, "The armed request is the newest one.");
        Assert.AreEqual(144000L, queue.ArmedBoundary!.Value.Frame);
        queue.Clear();
        Assert.IsNull(queue.Pending);
        Assert.IsNull(queue.ArmedBoundary);
    }

    [TestMethod]
    public void RapidStructuralEditsBeforeTheBoundaryApplyOnlyTheLastOneAtTheFirstPinnedPulse()
    {
        var first = Revision(Document(120));
        const long boundary = 96000 + 2 * 24000;
        const long total = boundary + 100000;
        var run = new LiveRun(first, total, lag: 1024);
        run.RunTo(120500, 500);
        run.Queue.Post(Prepared(first, Document(100)));
        run.RunTo(121000, 500);
        Assert.AreEqual(boundary, run.Queue.ArmedBoundary!.Value.Frame);
        run.Queue.Post(Prepared(first, Document(90)));
        run.RunTo(125000, 500);
        var last = Prepared(first, Document(80));
        run.Queue.Post(last);
        run.RunTo(130000, 500);
        Assert.AreEqual(boundary, run.Queue.ArmedBoundary!.Value.Frame, "The pinned boundary does not move when a newer request replaces the armed one.");
        run.RunTo(total, 500);

        Assert.AreEqual(1, run.TransitionCount());
        Assert.AreEqual(80, run.Timeline.Active.Document.Settings.Tempo);
        Assert.IsTrue(run.Steps.All(step => step.Timeline.Active.Document.Settings.Tempo is 120d or 80d),
            "The coalesced intermediate tempos never play.");
        Assert.AreEqual(boundary, run.FirstTransition()!.Timeline.SwitchFrame);
        // 80 BPM: 36000-frame beats, count-in 144000, so beat 3 lands on own frame 216000.
        Assert.AreEqual(216000L - boundary, run.Timeline.Active.Shift);
        Assert.IsNull(run.Queue.Pending);
    }

    [TestMethod]
    public void AnArmedChangeIsSupersededByARequestEqualToWhatIsPlaying()
    {
        var first = Revision(Document(120));
        var run = new LiveRun(first, 300000);
        run.RunTo(120500, 500);
        run.Queue.Post(Prepared(first, Document(90)));
        run.RunTo(121000, 500);
        Assert.IsNotNull(run.Queue.ArmedBoundary);
        run.Queue.Post(Prepared(first, Document(120)));
        run.RunTo(300000, 500);
        Assert.IsNull(run.Queue.ArmedBoundary);
        Assert.AreEqual(0, run.TransitionCount());
        Assert.AreSame(first, run.Timeline.Active);
    }

    [TestMethod]
    public void SoundOnlyRequestsSwitchAtTheQuantumBoundaryWithoutTouchingClockOrDeadline()
    {
        var first = Revision(Document(120, edit: s => s.PracticeSeconds = 10));
        var glass = Prepared(first, Document(120, edit: s => { s.PracticeSeconds = 10; s.Sound = "Glass"; }));
        const long total = 300000;
        var run = new LiveRun(first, total, lag: 1024);
        run.RunTo(130000, 1000);
        run.Queue.Post(glass);
        run.RunTo(total, 1000);

        var transition = run.FirstTransition()!;
        Assert.AreEqual(130000L, transition.Cursor, "The switch happens at the start of the next quantum, not at a pulse.");
        Assert.AreEqual(130000L, transition.Timeline.SwitchFrame);
        Assert.AreEqual(0L, run.Timeline.Active.Shift);
        Assert.AreEqual(first.EndFrame, run.Timeline.Active.EndFrame);
        Assert.AreEqual("Glass", run.Timeline.Active.Document.Settings.Sound);
        Assert.AreSame(glass.Initial, run.Timeline.Active.Initial, "Sound switches share the prepared plans.");
        var expected = Sum(Partition(first.Initial, 0, 0, total, 0.5f, attacksBeforeOwn: 130000),
            Partition(glass.Initial, 0, 0, total, 0.5f, attacksFromOwn: 130000));
        AssertSamePcm(expected, run.Pcm, total, "sound switch");
    }

    [TestMethod]
    public void SoundSwitchOnASplicedRevisionKeepsItsPlacementAndStopFrame()
    {
        var first = Revision(Document(120, edit: s => s.PracticeSeconds = 10));
        var second = Prepared(first, Document(90, edit: s => s.PracticeSeconds = 10));
        var glass = Prepared(second, Document(90, edit: s => { s.PracticeSeconds = 10; s.Sound = "Glass"; }));
        var run = new LiveRun(first, 400000, lag: 1024);
        run.RunTo(134000, 1000);
        run.Queue.Post(second);
        run.RunTo(200000, 1000);
        var spliced = run.Timeline.Active;
        Assert.AreEqual(48000L, spliced.Shift);
        Assert.AreEqual(576000L, spliced.EndFrameAbsolute);
        run.Queue.Post(glass);
        run.RunTo(400000, 1000);

        Assert.AreEqual(2, run.TransitionCount());
        Assert.AreEqual(48000L, run.Timeline.Active.Shift);
        Assert.AreEqual(spliced.EndFrame, run.Timeline.Active.EndFrame);
        Assert.AreEqual("Glass", run.Timeline.Active.Document.Settings.Sound);
        Assert.AreEqual(90, run.Timeline.Active.Document.Settings.Tempo);
    }

    [TestMethod]
    public void RequestsWaitInTheMailboxWhileATransitionRingsOutAndTheNewestStructuralOneWins()
    {
        var first = Revision(Document(120));
        var second = Prepared(first, Document(90));
        var staleSound = Prepared(second, Document(90, edit: s => s.Sound = "Click"));
        var latest = Prepared(second, Document(100, edit: s => s.Sound = "Glass"));
        var run = new LiveRun(first, 400000, lag: 4096);
        run.RunTo(134000, 1000);
        run.Queue.Post(second);
        run.RunTo(146000, 1000);
        Assert.IsNotNull(run.Timeline.Previous, "The first splice is still ringing out.");
        run.Queue.Post(staleSound);
        run.Queue.Post(latest);
        run.RunTo(150000, 1000);
        Assert.AreSame(latest, run.Queue.Pending, "Nothing is taken from the mailbox while the old tail is audible.");
        Assert.AreEqual("Wood", run.Timeline.Active.Document.Settings.Sound);
        run.RunTo(400000, 1000);

        Assert.AreEqual(2, run.TransitionCount());
        Assert.AreEqual(100, run.Timeline.Active.Document.Settings.Tempo);
        Assert.AreEqual("Glass", run.Timeline.Active.Document.Settings.Sound);
        Assert.IsTrue(run.Steps.All(step => step.Timeline.Active.Document.Settings.Sound != "Click"),
            "The superseded sound-only request never plays.");
        // Pulses of the spliced timeline follow at 176000, 208000, ...: the first free one after the tail retired.
        Assert.AreEqual(176000L, run.Steps.Last(step => step.Timeline.Previous is not null).Timeline.SwitchFrame);
    }

    [TestMethod]
    public void AnOldTailIsRetiredOnlyOnceTheProcessedCursorHasPassedItsWindow()
    {
        var first = Revision(Document(120));
        const long boundary = 144000, window = 2400, lag = 6000;
        var run = new LiveRun(first, 200000, lag: lag);
        run.RunTo(134000, 1000);
        run.Queue.Post(Prepared(first, Document(90)));
        run.RunTo(200000, 1000);
        foreach (var step in run.Steps.Where(candidate => candidate.Cursor >= boundary))
            Assert.AreEqual(step.Cursor - lag < boundary + window, step.Timeline.Previous is not null,
                $"Cursor {step.Cursor}");
    }

    [TestMethod]
    public void ARequestStaysArmedForAsLongAsItsBoundaryIsFarAway()
    {
        // 30 BPM without count-in: a beat is two seconds, so the next pulse is far outside every quantum.
        var first = Revision(Document(30, countIn: 0));
        var armed = Prepared(first, Document(60));
        var run = new LiveRun(first, 96000 * 4 + 10000);
        run.RunTo(96000 * 2 + 500, 480);
        run.Queue.Post(armed);
        run.RunTo(96000 * 2 + 20000, 480);
        var pinned = run.Queue.ArmedBoundary!.Value;
        Assert.AreEqual(96000L * 3, pinned.Frame);
        Assert.AreEqual(0, run.TransitionCount());
        run.RunTo(96000 * 4 + 10000, 480);
        Assert.AreEqual(1, run.TransitionCount());
        Assert.AreEqual(pinned.Frame, run.FirstTransition()!.Timeline.SwitchFrame);
    }

    [TestMethod]
    public void APracticeStopAtOrBeforeTheBoundaryNeverSplices()
    {
        // PracticeSeconds 1 ends at 96000 + 48000 = 144000, which is the pulse the splice would use.
        var timed = Revision(Document(120, edit: s => s.PracticeSeconds = 1));
        var run = new LiveRun(timed, 200000);
        run.RunTo(130000, 1000);
        run.Queue.Post(Prepared(timed, Document(90, edit: s => s.PracticeSeconds = 1)));
        run.RunTo(160000, 1000);
        Assert.AreEqual(0, run.TransitionCount());
        Assert.AreEqual(144000L, run.Queue.ArmedBoundary!.Value.Frame);
        Assert.IsTrue(run.Pcm.AsSpan(144000 * 2, 16000 * 2).ToArray().All(sample => sample == 0), "Nothing plays past the stop frame.");
    }

    [TestMethod]
    public void ThePracticeTimerKeepsItsAbsoluteFrameAcrossASpliceAndFadesOutAtTheNewDeadline()
    {
        // PracticeSeconds 2 ends at 96000 + 96000 = 192000 absolute, after the splice at 144000.
        var first = Revision(Document(120, edit: s => s.PracticeSeconds = 2));
        var second = Prepared(first, Document(90, edit: s => s.PracticeSeconds = 2));
        var run = new LiveRun(first, 210000);
        run.RunTo(130000, 1000);
        run.Queue.Post(second);
        run.RunTo(210000, 1000);
        Assert.AreEqual(1, run.TransitionCount());
        Assert.AreEqual(192000L, run.Timeline.Active.EndFrameAbsolute);
        Assert.AreEqual(192000L + 48000L, run.Timeline.Active.EndFrame);
        Assert.IsTrue(run.Pcm.AsSpan(192000 * 2, 18000 * 2).ToArray().All(sample => sample == 0));
        Assert.AreEqual(0f, run.Pcm[(192000 - 1) * 2], "The last allowed frame is faded to silence.");
        Assert.IsTrue(run.Pcm.AsSpan(176000 * 2, 4000).ToArray().Any(sample => sample != 0), "The beat before the stop still sounds.");
    }

    [TestMethod]
    public void StructuralRequestsAreNeverArmedForAFiniteProgram()
    {
        var document = Document();
        document.Program = [new ProgramStep { Bars = 2, StartTempo = 100, EndTempo = 100 }];
        var finite = Revision(document, 8000);
        var queue = new LiveEditQueue();
        queue.Post(Revision(Document(90), 8000));
        var timeline = new LiveTimeline(finite);
        Assert.AreSame(timeline, queue.Advance(timeline, 0, 1000, 0));
        Assert.IsNull(queue.ArmedBoundary);
        Assert.IsNull(queue.Pending);
    }

    [TestMethod]
    public void SoundRequestsSwitchAFiniteProgramAtTheQuantumBoundaryAndKeepItsStopBoundary()
    {
        var document = Document(120);
        document.Program = [new ProgramStep { Bars = 2, StartTempo = 120, EndTempo = 120 }];
        var finite = Revision(document, 8000);
        var decision = LiveSettingsEdits.Decide(finite, null, Requested(Document(120, edit: s =>
        {
            s.Sound = "Glass";
            s.Accents = [Accent.Strong, Accent.Soft, Accent.Soft, Accent.Soft];
        })));
        Assert.AreEqual(LiveEditOutcome.SoundApplied, decision.Outcome);
        var glass = Revision(decision.Merged, 8000);
        Assert.IsTrue(finite.HasSameClock(glass));
        Assert.AreEqual(48000L, finite.EndFrame, "Count-in 16000 + two 16000-frame bars.");

        const long total = 52000;
        var run = new LiveRun(finite, total, 1f, lag: 1000);
        run.RunTo(20000, 1000);
        run.Queue.Post(glass);
        run.RunTo(total, 1000);

        Assert.AreEqual(20000L, run.FirstTransition()!.Cursor);
        Assert.AreEqual(20000L, run.FirstTransition()!.Timeline.SwitchFrame);
        Assert.IsTrue(run.Timeline.Active.IsFinite);
        Assert.AreEqual(0L, run.Timeline.Active.Shift);
        Assert.AreEqual(finite.EndFrame, run.Timeline.Active.EndFrame);
        Assert.AreEqual("Glass", run.Timeline.Active.Document.Settings.Sound);
        Assert.IsNull(run.Queue.ArmedBoundary);
        var expected = Sum(Partition(finite.Initial, 0, 0, total, 1f, attacksBeforeOwn: 20000),
            Partition(glass.Initial, 0, 0, total, 1f, attacksFromOwn: 20000));
        AssertSamePcm(expected, run.Pcm, total, "finite sound switch");
        Assert.IsTrue(run.Pcm.AsSpan(48000 * 2, 4000 * 2).ToArray().All(sample => sample == 0), "Nothing sounds after the program ends.");
        Assert.IsTrue(run.Pcm.AsSpan(36000 * 2, 4000 * 2).ToArray().Any(sample => sample != 0), "The program still plays up to its end.");
    }

    [TestMethod]
    public void ClearDiscardsPostedAndArmedRequests()
    {
        var first = Revision(Document(120), 8000);
        var queue = new LiveEditQueue();
        queue.Post(Revision(Document(90), 8000));
        queue.Advance(new LiveTimeline(first), 0, 100, 0);
        Assert.IsNotNull(queue.Pending);
        queue.Post(Revision(Document(100), 8000));
        queue.Clear();
        Assert.IsNull(queue.Pending);
        Assert.IsNull(queue.ArmedBoundary);
        var timeline = new LiveTimeline(first);
        Assert.AreSame(timeline, queue.Advance(timeline, 0, 100000, 0));
    }
}
