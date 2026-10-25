using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

internal static class Check
{
    internal static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (Exception e)
        {
            Assert.IsTrue(e is T, $"Expected {typeof(T).Name}, got {e.GetType().Name}: {e.Message}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(e.Message));
            return (T)e;
        }
        Assert.Fail($"Expected {typeof(T).Name}.");
        throw new InvalidOperationException("Unreachable");
    }

    internal static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (Exception e)
        {
            Assert.IsTrue(e is T, $"Expected {typeof(T).Name}, got {e.GetType().Name}: {e.Message}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(e.Message));
            return (T)e;
        }
        Assert.Fail($"Expected {typeof(T).Name}.");
        throw new InvalidOperationException("Unreachable");
    }

    internal static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray());

    internal static PulseDocument Document() => new()
    {
        Settings = new() { CountInBars = 0 }
    };

    internal static PulseDocument LaneDocument(float pan = 0, float volume = 0.5f, float eventGain = 1,
        Rational? position = null) => new()
    {
        Settings = new() { CountInBars = 0, Accents = [Accent.Silent], MasterGain = 0.3f },
        Lanes = [new()
        {
            Pan = pan, Volume = volume, CycleQuarters = 4,
            Events = [new() { PositionQuarters = position ?? Rational.Zero, Gain = eventGain }]
        }]
    };

    internal static RenderPlan Plan(PulseDocument? d = null, int bars = 1, int rate = 8000,
        bool countIn = false) => ScheduleCompiler.Compile(d ?? Document(), rate, bars, countIn);

    internal static float[] Render(RenderPlan plan, int frames, long start = 0, int channels = 2,
        float gain = 0.5f, bool loop = false)
    {
        var samples = new float[frames * channels];
        PcmRenderer.Fill(samples, plan, start, channels, gain, loop: loop);
        return samples;
    }
}

internal sealed class TempDirectory : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ResonantPulse.Tests." + Guid.NewGuid().ToString("N"));
    internal TempDirectory() => Directory.CreateDirectory(Root);
    internal string File(string name = "project.json") => Path.Combine(Root, name);
    internal void NoTemps() => Assert.AreEqual(0, Directory.GetFiles(Root, "*.tmp", SearchOption.TopDirectoryOnly).Length);
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
