using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class TimingRunRulesTests
{
    [TestMethod]
    public void ArmingOptionsReenableWhenArmedFlagClearsAndSupportAnotherRun()
    {
        var transitions = new[] { false, true, false, true, false };
        CollectionAssert.AreEqual(new[] { true, false, true, false, true },
            transitions.Select(TimingRunRules.CanEditArmingOptions).ToArray());
    }

    [TestMethod]
    public void NormalCompletionFinishesOnceWithoutInvalidatingTheReport()
    {
        Assert.AreEqual(TimingRunUpdate.Finish,
            TimingRunRules.Update(true, true, false, true));
        Assert.AreEqual(TimingRunUpdate.None,
            TimingRunRules.Update(false, true, false, true));
    }

    [TestMethod]
    public void SamePlayingRevisionContinuesRegardlessOfPendingEditorChanges()
    {
        // Pending document edits do not change the active revision; the
        // production decision intentionally depends only on that revision.
        Assert.AreEqual(TimingRunUpdate.Continue,
            TimingRunRules.Update(true, true, true, false));
        Assert.IsFalse(TimingRunRules.CanArm(true, -1, 2));
        Assert.IsFalse(TimingRunRules.CanArm(true, 0, 2));
    }

    [TestMethod]
    public void ChangedRevisionOrInterruptedTransportInvalidates()
    {
        Assert.AreEqual(TimingRunUpdate.Invalidate,
            TimingRunRules.Update(true, false, true, false));
        Assert.AreEqual(TimingRunUpdate.Invalidate,
            TimingRunRules.Update(true, false, false, true));
        Assert.AreEqual(TimingRunUpdate.Invalidate,
            TimingRunRules.Update(true, true, false, false));
    }

    [TestMethod]
    public void ArmingRejectsRemovedTargetsAndAcceptsThePrimaryTargetWithoutLanes()
    {
        Assert.IsTrue(TimingRunRules.CanArm(false, -1, 0));
        Assert.IsFalse(TimingRunRules.CanArm(false, 0, 0));
        Assert.IsFalse(TimingRunRules.CanArm(false, 1, 1));
        Assert.IsFalse(TimingRunRules.CanArm(false, -2, 2));
        Assert.IsTrue(TimingRunRules.CanArm(false, 1, 2));
    }

    [TestMethod]
    public void LateCompletionRefreshCapsAnalysisAtTheFinitePlanEnd()
    {
        const int sampleRate = 48000;
        var document = new PulseDocument
        {
            Settings = new PulseSettings { CountInBars = 0, Tempo = 120 },
            Program = [new ProgramStep { Bars = 1, Repeats = 1, StartTempo = 120, EndTempo = 120 }]
        };
        var plan = ScheduleCompiler.Compile(document, sampleRate, 1, false);
        var endFrame = TimingRunRules.EndFrame(0, 10, sampleRate, plan.TotalFrames, plan.TotalFrames + 480);
        Assert.AreEqual(plan.TotalFrames, endFrame);
        var seconds = (double)endFrame / sampleRate;
        var expected = plan.Targets.Where(onset => onset.LaneIndex == -1 && onset.Frame < endFrame)
            .Select(onset => (double)onset.Frame / sampleRate).ToArray();
        Assert.IsTrue(expected.Length > 0);
        var observed = expected.Append(seconds + 0.1).Where(time => time < seconds).ToArray();
        var report = TimingAnalyzer.Analyze(expected, observed, 0.15);
        Assert.AreEqual(expected.Length, report.Matched);
        Assert.AreEqual(0, report.Missed);
        Assert.AreEqual(0, report.Extra);
    }

    [TestMethod]
    public void AnalysisWindowIsBoundedAndNeverBecomesNegative()
    {
        Assert.AreEqual(1000L + 600 * 48000,
            TimingRunRules.EndFrame(1000, 700, 48000, null, null));
        Assert.AreEqual(1000L, TimingRunRules.EndFrame(1000, 10, 48000, 500, 500));
    }
}
