using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class PlaybackResumeTests
{
    private static PlaybackRevision Revision(string sound, int rate, Accent accent = Accent.Normal) =>
        new(new PulseDocument { Settings = new()
        {
            CountInBars = 0, Sound = sound, Accents = [accent], PracticeSeconds = 2
        } }, rate);

    [TestMethod]
    [DataRow(8000, 8000)]
    [DataRow(8000, 16000)]
    [DataRow(48000, 44100)]
    public void ExistingTransitionAndQueuedEditSurviveReopeningWithoutLosingOldTail(int oldRate, int newRate)
    {
        var state = new RetainedPlayback(Revision("Glass", oldRate), Revision("Wood", oldRate),
            Revision("Click", oldRate), oldRate / 50, oldRate / 100);
        var resumed = state.Rebuild(newRate);
        var previous = resumed.Previous;
        var pending = resumed.Pending;
        Assert.IsNotNull(previous);
        Assert.IsNotNull(pending);
        Assert.AreEqual((long)Math.Round((double)state.SwitchFrame * newRate / oldRate), resumed.SwitchFrame);
        Assert.AreEqual((long)Math.Round((double)state.ProcessedFrame * newRate / oldRate), resumed.ProcessedFrame);
        Assert.AreEqual("Glass", resumed.Active.Document.Settings.Sound);
        Assert.AreEqual("Wood", previous.Document.Settings.Sound);
        Assert.AreEqual("Click", pending.Document.Settings.Sound);
        Assert.AreEqual(newRate, resumed.Active.Initial.SampleRate);
        Assert.AreEqual(newRate, previous.Initial.SampleRate);
        Assert.AreEqual(newRate, pending.Initial.SampleRate);
        Assert.AreEqual((long)newRate * 2, resumed.Active.EndFrame);
        float[] before = new float[256], after = new float[256], baseline = new float[256];
        PcmRenderer.Fill(before, previous.Initial, resumed.ProcessedFrame, loop: false,
            attacksBeforeFrame: resumed.SwitchFrame, limitPeaks: false);
        PcmRenderer.Fill(after, resumed.Active.Initial, resumed.ProcessedFrame, loop: false,
            attacksFromFrame: resumed.SwitchFrame, limitPeaks: false);
        PcmRenderer.Fill(baseline, previous.Initial, resumed.ProcessedFrame, loop: false);
        Assert.IsTrue(baseline.Any(sample => sample != 0), "The retained tail must be audible in this fixture.");
        for (int index = 0; index < baseline.Length; index++)
            Assert.AreEqual(baseline[index], Math.Clamp(before[index] + after[index], -1f, 1f));
        if (oldRate == newRate) Assert.AreSame(state, resumed);
    }

    [TestMethod]
    [DataRow(8000, 8000, false)]
    [DataRow(8000, 16000, false)]
    [DataRow(48000, 44100, false)]
    [DataRow(8000, 8000, true)]
    [DataRow(8000, 16000, true)]
    [DataRow(48000, 44100, true)]
    public void QueuedAccentDoesNotRewriteAttacksBeforeResumedCursor(int oldRate, int newRate, bool wasSilent)
    {
        var state = new RetainedPlayback(
            Revision("Wood", oldRate, wasSilent ? Accent.Silent : Accent.Normal), null,
            Revision("Glass", oldRate, wasSilent ? Accent.Normal : Accent.Silent), 0, oldRate / 100);
        var resumed = state.Rebuild(newRate);
        Assert.IsNull(resumed.Previous);
        var pending = resumed.Pending;
        Assert.IsNotNull(pending);
        float[] before = new float[256], after = new float[256], baseline = new float[256];
        PcmRenderer.Fill(before, resumed.Active.Initial, resumed.ProcessedFrame, loop: false,
            attacksBeforeFrame: resumed.ProcessedFrame, limitPeaks: false);
        PcmRenderer.Fill(after, pending.Initial, resumed.ProcessedFrame, loop: false,
            attacksFromFrame: resumed.ProcessedFrame, limitPeaks: false);
        PcmRenderer.Fill(baseline, resumed.Active.Initial, resumed.ProcessedFrame, loop: false);
        Assert.AreEqual(wasSilent, baseline.All(sample => sample == 0));
        for (int index = 0; index < baseline.Length; index++)
            Assert.AreEqual(baseline[index], Math.Clamp(before[index] + after[index], -1f, 1f));
    }

    [TestMethod]
    [DataRow(8000, 8000, false)]
    [DataRow(8000, 16000, false)]
    [DataRow(48000, 44100, false)]
    [DataRow(8000, 8000, true)]
    [DataRow(8000, 16000, true)]
    [DataRow(48000, 44100, true)]
    public void RenderedAheadTailIsRetainedForReopeningFromCompletedCursor(int oldRate, int newRate, bool wasSilent)
    {
        var boundary = oldRate / 100;
        var processed = boundary + oldRate / 500;
        var rendered = boundary + oldRate / 20 + 128;
        Assert.IsTrue(RetainedPlayback.HasProcessedTail(rendered, boundary, oldRate));
        Assert.IsFalse(RetainedPlayback.HasProcessedTail(processed, boundary, oldRate));
        var state = new RetainedPlayback(
            Revision("Glass", oldRate, wasSilent ? Accent.Normal : Accent.Silent),
            Revision("Wood", oldRate, wasSilent ? Accent.Silent : Accent.Normal),
            null, boundary, processed);
        var resumed = state.Rebuild(newRate);
        var previous = resumed.Previous;
        Assert.IsNotNull(previous);
        float[] before = new float[256], after = new float[256], baseline = new float[256];
        PcmRenderer.Fill(before, previous.Initial, resumed.ProcessedFrame, loop: false,
            attacksBeforeFrame: resumed.SwitchFrame, limitPeaks: false);
        PcmRenderer.Fill(after, resumed.Active.Initial, resumed.ProcessedFrame, loop: false,
            attacksFromFrame: resumed.SwitchFrame, limitPeaks: false);
        PcmRenderer.Fill(baseline, previous.Initial, resumed.ProcessedFrame, loop: false);
        Assert.AreEqual(wasSilent, baseline.All(sample => sample == 0));
        for (int index = 0; index < baseline.Length; index++)
            Assert.AreEqual(baseline[index], Math.Clamp(before[index] + after[index], -1f, 1f));
    }

    [TestMethod]
    public void RetirementRequiresTheExactProcessedTailHorizon()
    {
        const long boundary = 12345, horizon = boundary + 2400;
        Assert.IsFalse(RetainedPlayback.HasProcessedTail(boundary - 1, boundary, 48000));
        Assert.IsFalse(RetainedPlayback.HasProcessedTail(horizon - 1, boundary, 48000));
        Assert.IsTrue(RetainedPlayback.HasProcessedTail(horizon, boundary, 48000));
        Assert.IsTrue(RetainedPlayback.HasProcessedTail(horizon + 1, boundary, 48000));
    }
}
