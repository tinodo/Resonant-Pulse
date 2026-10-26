using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.Core;
using static ResonantPulse.App.Tests.LiveTestKit;

namespace ResonantPulse.App.Tests;

/// <summary>
/// The practice stop fades the last 5 ms of audio. A live splice just before it must not change anything the old
/// revision had already committed to play, including that fade.
/// </summary>
[TestClass]
public sealed class LiveDeadlineSpliceTests
{
    private const int Rate = 8000, FadeFrames = Rate / 200;

    // Subdivisions keep soft click tails ringing across every primary pulse; two seconds is the practice timer.
    private static PulseDocument Fast(double tempo) =>
        Document(tempo, edit: settings => { settings.Subdivision = 8; settings.PracticeSeconds = 2; });

    private sealed record Scenario(PlaybackRevision Before, PulseBoundary Boundary, long Stop, LiveRun Reference, long Total);

    // Finds a tempo whose pulse grid puts a boundary inside the fade window while audible tails are still ringing there.
    private static Scenario FindScenario(int quantum)
    {
        for (var tempo = 1000; tempo >= 400; tempo--)
        {
            var before = Revision(Fast(tempo), Rate);
            var stop = before.EndFrameAbsolute!.Value;
            if (LiveSplice.NextPulseBoundary(before, stop - (FadeFrames - 1)) is not { } boundary || boundary.Frame >= stop) continue;
            var total = stop + 200;
            var reference = new LiveRun(before, total);
            reference.RunTo(total, quantum);
            var windowStart = (int)(stop - (FadeFrames - 1));
            var ringing = false;
            for (var frame = windowStart; frame < boundary.Frame && !ringing; frame++)
                ringing = reference.Pcm[frame * 2] != 0 || reference.Pcm[frame * 2 + 1] != 0;
            if (ringing) return new(before, boundary, stop, reference, total);
        }
        throw new AssertFailedException("No tempo puts a pulse boundary in the fade window with ringing tails.");
    }

    [TestMethod]
    [DataRow(480)]
    [DataRow(1024)]
    [DataRow(7001)]
    public void ASpliceInsideTheFinalFadeWindowNeverChangesAudioBeforeItsBoundary(int quantum)
    {
        var scenario = FindScenario(quantum);
        Assert.IsTrue(scenario.Boundary.Frame > scenario.Stop - FadeFrames && scenario.Boundary.Frame < scenario.Stop);
        var after = Revision(Fast(scenario.Before.Document.Settings.Tempo - 50), Rate);

        var spliced = new LiveRun(scenario.Before, scenario.Total);
        // Start the splice quantum well before the window so one quantum straddles the fade start and the boundary.
        spliced.RunTo(scenario.Boundary.Frame - 60, quantum);
        spliced.Queue.Post(after);
        spliced.RunTo(scenario.Total, quantum);

        var transition = spliced.FirstTransition();
        Assert.IsNotNull(transition, "The structural edit must have been spliced in.");
        Assert.AreEqual(scenario.Boundary.Frame, transition.Timeline.SwitchFrame);
        AssertSamePcm(scenario.Reference.Pcm, spliced.Pcm, scenario.Boundary.Frame, $"quantum {quantum}, before the boundary");
    }
}
