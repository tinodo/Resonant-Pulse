using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using static ResonantPulse.App.Tests.LiveTestKit;

namespace ResonantPulse.App.Tests;

/// <summary>
/// Beats that are not a whole number of frames (133 BPM at 48 kHz is 21654.135... frames) make the first body loop inside
/// Initial one frame shorter than later Body cycles. Every boundary must still be a real click of both revisions.
/// </summary>
[TestClass]
public sealed class LiveSeamTests
{
    private const int PulsesPerLoop = 64;

    [TestMethod]
    public void TheLoopsAreNotWholeFrameMultiplesSoTheSeamTestIsMeaningful()
    {
        var revision = Revision(Document(133));
        Assert.AreNotEqual(revision.Initial.TotalFrames - revision.CountInFrames, revision.Body.TotalFrames,
            "If these were equal the scenario would not exercise the rounding seam.");
    }

    [TestMethod]
    public void EveryBoundaryOfFractionalBeatsLandsOnARealClickOfBothRevisions()
    {
        var before = Revision(Document(133));
        var after = Revision(Document(100));
        var firstLoopBefore = PrimaryAttacks(before.Initial, before.CountInFrames);
        var bodyBefore = PrimaryAttacks(before.Body);
        var firstLoopAfter = PrimaryAttacks(after.Initial, after.CountInFrames).ToHashSet();
        var bodyAfter = PrimaryAttacks(after.Body).ToHashSet();
        Assert.AreEqual(PulsesPerLoop, firstLoopBefore.Length);
        Assert.AreEqual(PulsesPerLoop, bodyBefore.Length);

        for (var loop = 0; loop < 4; loop++)
            for (var pulse = 0; pulse < PulsesPerLoop; pulse++)
            {
                var own = loop == 0
                    ? firstLoopBefore[pulse]
                    : before.Initial.TotalFrames + (loop - 1) * before.Body.TotalFrames + bodyBefore[pulse];
                var label = $"loop {loop} pulse {pulse}";
                var boundary = LiveSplice.NextPulseBoundary(before, own)!.Value;
                Assert.AreEqual((loop, pulse, own), ((int)boundary.Loop, boundary.Pulse, boundary.OwnFrame), label);
                Assert.AreEqual(own, boundary.Frame, label);

                var mapped = LiveSplice.MapContinuity(after, boundary);
                var inBody = mapped - after.Initial.TotalFrames - Math.Max(0, loop - 1) * after.Body.TotalFrames;
                Assert.IsTrue(loop == 0 ? firstLoopAfter.Contains(mapped) : bodyAfter.Contains(inBody),
                    $"{label}: the new revision has no click at its mapped frame {mapped}.");

                // One frame later the boundary must be the NEXT pulse, never a repeat of this one.
                var next = LiveSplice.NextPulseBoundary(before, own + 1)!.Value;
                Assert.IsTrue(next.OwnFrame > own, $"{label}: boundary did not advance.");
            }
    }

    [TestMethod]
    public void ARestartAtTheSeamStartsTheNewRevisionOnItsOwnDownbeat()
    {
        var before = Revision(Document(133));
        var after = Revision(Document(133, beats: 3));
        // Last pulse of the first loop, the seam itself, and a pulse deep in a later loop.
        foreach (var cursor in new[] { before.Initial.TotalFrames - 1, before.Initial.TotalFrames,
                     before.Initial.TotalFrames + 2 * before.Body.TotalFrames + 12345 })
        {
            var boundary = LiveSplice.NextPulseBoundary(before, cursor)!.Value;
            var spliced = LiveSplice.Splice(before, after, boundary);
            Assert.AreEqual(after.CountInFrames, spliced.ToOwn(boundary.Frame),
                "A meter change puts the boundary on bar 1 beat 1 of the new revision.");
        }
    }
}
