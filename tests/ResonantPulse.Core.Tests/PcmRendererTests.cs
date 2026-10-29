using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class PcmRendererTests
{
    [TestMethod]
    public void Fill_OnsetAndCursorOffsets_PlacesAttackWithoutPreEcho()
    {
        var p = Check.Plan(Check.LaneDocument(position: new Rational(1, 2)));
        var whole = Check.Render(p, 2500, channels: 1);
        Assert.IsTrue(whole.Take(2001).All(x => x == 0)); // attack envelope is zero at its scheduled frame.
        Assert.AreNotEqual(0f, whole[2001], "The first post-attack sample belongs at onset + 1, not merely somewhere later.");
        Assert.IsTrue(whole.Skip(2001).Take(100).Any(x => Math.Abs(x) > 0.001f));
        var atZero = Check.Render(Check.Plan(Check.LaneDocument()), 400, channels: 1);
        Check.Sequence(atZero, whole.Skip(2000).Take(400));
        Check.Sequence(whole.Skip(2017).Take(71), Check.Render(p, 71, 2017, channels: 1));
        Assert.AreEqual(2000L, p.Onsets.Single().Frame);
    }

    [TestMethod]
    public void Fill_OneShotAndIrregularBlocks_ProduceIdenticalSamples()
    {
        var p = Check.Plan();
        var whole = Check.Render(p, 17013, start: 15900, loop: true);
        var blocks = new float[whole.Length];
        int cursor = 0;
        foreach (int size in new[] { 1, 37, 399, 8191, 8385 })
        {
            PcmRenderer.Fill(blocks.AsSpan(cursor * 2, size * 2), p, 15900 + cursor, loop: true);
            cursor += size;
        }
        Assert.AreEqual(17013, cursor); Check.Sequence(whole, blocks);
        Assert.IsTrue(blocks.Any(x => x != 0));
    }

    [TestMethod]
    public void Fill_MonoStereoAndEqualPowerPan_UsesExpectedChannelValues()
    {
        var left = Check.Plan(Check.LaneDocument(pan: -1));
        var right = Check.Plan(Check.LaneDocument(pan: 1));
        var center = Check.Plan(Check.LaneDocument(pan: 0));
        var a = Check.Render(left, 400, gain: 1); var b = Check.Render(right, 400, gain: 1);
        var c = Check.Render(center, 400, gain: 1); var mono = Check.Render(center, 400, channels: 1, gain: 1);
        Assert.IsTrue(mono.Any(x => Math.Abs(x) > 0.01));
        for (int i = 0; i < 400; i++)
        {
            Assert.AreEqual(0f, a[i * 2 + 1]);
            Assert.AreEqual(0d, (double)b[i * 2], 1e-15); // cos(pi/2) is not bit-exact zero.
            Assert.AreEqual(a[i * 2], b[i * 2 + 1]);
            Assert.AreEqual(c[i * 2], c[i * 2 + 1]);
            Assert.AreEqual(mono[i] / Math.Sqrt(2), c[i * 2], 3e-8);
            Assert.AreEqual(mono[i], a[i * 2]);
        }
    }

    [TestMethod]
    public void Fill_MasterAndLaneEventGains_ApplyOnce()
    {
        var reference = Check.Plan(Check.LaneDocument(volume: 1, eventGain: 1));
        var scaled = Check.Plan(Check.LaneDocument(volume: 0.5f, eventGain: 0.25f));
        var full = Check.Render(reference, 400, channels: 1, gain: 1);
        var half = Check.Render(reference, 400, channels: 1, gain: 0.5f);
        var eighth = Check.Render(scaled, 400, channels: 1, gain: 1);
        Assert.AreEqual(0.3f, reference.DefaultMasterGain);
        Assert.IsTrue(full.Any(x => Math.Abs(x) > 0.1f));
        for (int i = 0; i < full.Length; i++)
        {
            Assert.AreEqual(full[i] * 0.5f, half[i]);
            Assert.AreEqual(full[i] * 0.125f, eighth[i]);
        }
    }

    [TestMethod]
    public void Fill_MuteZeroGainEmptyAndFiniteExhaustion_ClearsPoisonedDestination()
    {
        var p = Check.Plan();
        foreach (var setting in new[] { (true, 1f, 0L), (false, 0f, 0L), (false, 1f, p.TotalFrames) })
        {
            var poisoned = Enumerable.Repeat(float.NaN, 200).ToArray();
            PcmRenderer.Fill(poisoned, p, setting.Item3, masterGain: setting.Item2, mute: setting.Item1, loop: false);
            Assert.IsTrue(poisoned.All(x => x == 0));
        }
        var silent = Check.Document(); silent.Settings.Accents = [Accent.Silent];
        var buffer = Enumerable.Repeat(12f, 200).ToArray();
        PcmRenderer.Fill(buffer, Check.Plan(silent), 0);
        Assert.IsTrue(buffer.All(x => x == 0));
        PcmRenderer.Fill(Span<float>.Empty, p, 0);
        var endpoint = Check.Render(p, 500, p.TotalFrames - 10);
        Assert.IsTrue(endpoint.Skip(20).All(x => x == 0));
        Assert.AreEqual(16000L, p.TotalFrames);
    }

    [TestMethod]
    public void Fill_LoopSeamTailsAndCountIn_OverlapOnlyWhenLooping()
    {
        var d = Check.LaneDocument(volume: 0.1f);
        d.Lanes[0].Events = [new() { PositionQuarters = 0 }, new() { PositionQuarters = new Rational(399, 100) }];
        var p = Check.Plan(d);
        var isolated = Check.Plan(Check.LaneDocument(volume: 0.1f));
        var first = Check.Render(isolated, 400, channels: 1, gain: 1);
        var seam = Check.Render(p, 100, p.TotalFrames + 10, channels: 1, gain: 1, loop: true);
        for (int i = 0; i < seam.Length; i++)
            Assert.AreEqual(first[10 + i] + first[50 + i], seam[i], 1e-8f);
        Assert.IsTrue(seam.Any(x => x != 0));
        Assert.IsTrue(Check.Render(p, 100, p.TotalFrames, channels: 1, loop: false).All(x => x == 0));
        Check.Sequence(first.Take(100), Check.Render(p, 100, channels: 1, gain: 1, loop: true)); // no imaginary previous cycle at start.
        d.Settings.CountInBars = 1;
        var withCountIn = Check.Plan(d, countIn: true);
        Assert.AreEqual(32000L, withCountIn.TotalFrames);
        Check.Sequence(Check.Render(withCountIn, 100, 4000), Check.Render(withCountIn, 100, withCountIn.TotalFrames + 4000, loop: true));
    }

    [TestMethod]
    public void Fill_OverlappingVoices_ClampsAfterSummingToFiniteRange()
    {
        var d = Check.Document(); d.Settings.Sound = "Glass";
        d.Lanes = Enumerable.Range(0, 8).Select(_ => new RhythmLane { Volume = 1, Sound = "Glass" }).ToList();
        var samples = Check.Render(Check.Plan(d), 400, gain: 1);
        Assert.IsTrue(samples.All(x => float.IsFinite(x) && x >= -1 && x <= 1));
        Assert.IsTrue(samples.Any(x => x == 1 || x == -1), "Fixture must actually engage peak protection.");
        Assert.IsTrue(samples.Any(x => x > 0) && samples.Any(x => x < 0));
        Assert.AreEqual(0f, samples[0]); Assert.AreEqual(0f, samples[^1]);
    }

    [TestMethod]
    public void Fill_BufferChannelsGainAndCursorBounds_RejectsInvalidArguments()
    {
        var p = Check.Plan();
        foreach (int channels in new[] { 0, 3, -1 })
            Check.Throws<ArgumentException>(() => PcmRenderer.Fill(new float[6], p, 0, channels));
        Check.Throws<ArgumentException>(() => PcmRenderer.Fill(new float[3], p, 0));
        Check.Throws<ArgumentException>(() => PcmRenderer.Fill(new float[32769], p, 0, channels: 1));
        Check.Throws<ArgumentNullException>(() => PcmRenderer.Fill(new float[2], null!, 0));
        foreach (long cursor in new[] { -1L, long.MaxValue })
            Check.Throws<ArgumentOutOfRangeException>(() => PcmRenderer.Fill(new float[2], p, cursor));
        foreach (float gain in new[] { -0.1f, 1.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Check.Throws<ArgumentOutOfRangeException>(() => PcmRenderer.Fill(new float[2], p, 0, masterGain: gain));
        var maximum = new float[32768 * 2]; PcmRenderer.Fill(maximum, p, 0);
        Assert.IsTrue(maximum.All(float.IsFinite)); Assert.IsTrue(maximum.Any(x => x != 0));
    }

    [TestMethod]
    [DoNotParallelize]
    public void Fill_WarmedPreallocatedCalls_AllocateZeroManagedBytes()
    {
        var d = Check.LaneDocument(position: new Rational(399, 100));
        var p = Check.Plan(d); var buffer = new float[256];
        for (int i = 0; i < 64; i++)
        {
            PcmRenderer.Fill(buffer, p, 15990, loop: true);
            PcmRenderer.Fill(buffer, p, 0, mute: true);
            PcmRenderer.Fill(buffer, p, 0);
            PcmRenderer.Fill(buffer, p, 15970, loop: false);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            PcmRenderer.Fill(buffer, p, 15990, loop: true);
            PcmRenderer.Fill(buffer, p, 0, mute: true);
            PcmRenderer.Fill(buffer, p, 15970, loop: false);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(p);
        Assert.AreEqual(0L, allocated, "Compilation/caches and assertions are outside the synchronous measured region.");
        Assert.IsTrue(buffer.Any(x => x != 0));
        Assert.IsTrue(buffer.Skip(60).All(x => x == 0)); // last measured finite call crosses its endpoint.
    }

    [TestMethod]
    public void Fill_GapMask_RemovesPriorTailAtHalfOpenBoundary()
    {
        var d = Check.LaneDocument(position: new Rational(399, 100));
        d.Settings.AudibleBars = d.Settings.SilentBars = 1;
        var p = Check.Plan(d, 2);
        var samples = Check.Render(p, 100, 15990, channels: 1);
        Assert.IsTrue(samples.Take(10).Any(x => x != 0));
        Assert.IsTrue(samples.Skip(10).All(x => x == 0));
        Assert.IsTrue(p.PositionAtFrame(16000).IsSilent);
        Assert.IsFalse(p.PositionAtFrame(15999).IsSilent);
    }

    [TestMethod]
    public void ApplyGapMask_ClearsExactlyTheSilentRanges_ForBlockOffsetsLoopingAndChannels()
    {
        var d = Check.LaneDocument();
        d.Settings.AudibleBars = d.Settings.SilentBars = 1;
        var p = Check.Plan(d, 2);
        var gap = p.SilentRanges.Single();
        Assert.AreEqual((16000L, 32000L), gap);

        // A window that straddles both edges of the gap, without looping.
        var window = Enumerable.Repeat(1f, 2 * 20000).ToArray();
        PcmRenderer.ApplyGapMask(window, p, 10000, 2, loop: false);
        for (var frame = 0; frame < 20000; frame++)
        {
            var expected = 10000 + frame >= gap.StartFrame && 10000 + frame < gap.EndFrame ? 0f : 1f;
            Assert.AreEqual(expected, window[frame * 2], $"left {frame}");
            Assert.AreEqual(expected, window[frame * 2 + 1], $"right {frame}");
        }

        // Looping: the plan repeats, so the gap recurs every TotalFrames, including across the loop edge.
        var looped = Enumerable.Repeat(1f, 40000).ToArray();
        PcmRenderer.ApplyGapMask(looped, p, 30000, 1, loop: true);
        for (var frame = 0; frame < looped.Length; frame++)
        {
            var local = (30000 + frame) % p.TotalFrames;
            Assert.AreEqual(local >= gap.StartFrame && local < gap.EndFrame ? 0f : 1f, looped[frame], $"loop {frame}");
        }

        // Block-wise application equals one call: masks have no state.
        var whole = Enumerable.Repeat(1f, 2 * 5000).ToArray();
        PcmRenderer.ApplyGapMask(whole, p, 14000, 2, loop: true);
        var pieces = Enumerable.Repeat(1f, 2 * 5000).ToArray();
        for (var done = 0; done < 5000; done += 700)
        {
            var count = Math.Min(700, 5000 - done);
            PcmRenderer.ApplyGapMask(pieces.AsSpan(done * 2, count * 2), p, 14000 + done, 2, loop: true);
        }
        Check.Sequence(whole, pieces);
    }

    [TestMethod]
    public void ApplyGapMask_RejectsInvalidArguments_AndIgnoresPlansWithoutGaps()
    {
        var gapless = Check.Plan();
        var buffer = Enumerable.Repeat(1f, 200).ToArray();
        PcmRenderer.ApplyGapMask(buffer, gapless, 0);
        Assert.IsTrue(buffer.All(x => x == 1f));
        Assert.ThrowsExactly<ArgumentNullException>(() => PcmRenderer.ApplyGapMask(buffer, null!, 0));
        Assert.ThrowsExactly<ArgumentException>(() => PcmRenderer.ApplyGapMask(buffer, gapless, 0, 3));
        Assert.ThrowsExactly<ArgumentException>(() => PcmRenderer.ApplyGapMask(buffer.AsSpan(0, 199), gapless, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PcmRenderer.ApplyGapMask(buffer, gapless, -1));
    }

    [TestMethod]
    public void Fill_EachSoundAndAccent_IsFiniteDistinctAndTapered()
    {
        var signatures = new List<float[]>();
        foreach (string sound in new[] { "Wood", "Glass", "Click", "Kick", "HiHat" })
        {
            var d = Check.LaneDocument(volume: 1); d.Lanes[0].Sound = sound;
            d.Lanes[0].Events[0].Accent = Accent.Strong;
            var strong = Check.Render(Check.Plan(d), 450, channels: 1, gain: 1);
            d.Lanes[0].Events[0].Accent = Accent.Soft;
            var soft = Check.Render(Check.Plan(d), 450, channels: 1, gain: 1);
            Assert.IsTrue(strong.All(float.IsFinite) && soft.All(float.IsFinite));
            Assert.AreEqual(0f, strong[0]); Assert.IsTrue(strong.Skip(399).All(x => x == 0));
            Assert.IsTrue(strong.Max(x => Math.Abs(x)) > soft.Max(x => Math.Abs(x)), sound);
            Assert.IsTrue(soft.Any(x => Math.Abs(x) > 0.001f), sound);
            signatures.Add(strong);
        }
        for (int i = 0; i < signatures.Count; i++)
            for (int j = i + 1; j < signatures.Count; j++)
                Assert.IsFalse(signatures[i].SequenceEqual(signatures[j]), $"Sounds {i} and {j} must not alias.");
    }
}
