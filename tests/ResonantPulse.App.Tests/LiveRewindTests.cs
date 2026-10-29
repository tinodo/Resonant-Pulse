using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.App.Localization;
using static ResonantPulse.App.Tests.LiveTestKit;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class LiveRewindTests
{
    // 48 kHz, 120 BPM: beat 24000, one-bar count-in ends at 96000. 90 BPM: beat 32000.
    private const long Beat120 = 24000, CountIn120 = 96000;

    [TestMethod]
    [DataRow(480)]
    [DataRow(1024)]
    [DataRow(7001)]
    public void RetiringTheOldRevisionDoesNotChangeASingleSample(int quantum)
    {
        // A pause, an endpoint change or the UI snapshot all rely on a timeline that still holds the old revision
        // being able to render the same audio as the live callback, which drops that revision once its tails are over.
        var boundary = CountIn120 + 5 * Beat120;
        var total = boundary + 130000;
        var run = new LiveRun(Revision(Document(120)), total, lag: 2 * quantum);
        run.RunTo(boundary - 10000, quantum);
        run.Queue.Post(Revision(Document(90)));
        run.RunTo(total, quantum);

        var transition = run.FirstTransition()!;
        Assert.IsNull(run.Timeline.Previous, "The scenario must run long enough for the old revision to retire.");
        var from = transition.Cursor;
        var frames = total - from;
        var again = new float[checked((int)(frames * 2))];
        var scratch = new float[32768 * 2];
        for (long cursor = from; cursor < total;)
        {
            var count = (int)Math.Min(333, total - cursor);
            LiveSplice.Render(again.AsSpan(checked((int)((cursor - from) * 2)), count * 2), cursor, count,
                transition.Timeline, 0.5f, false, scratch);
            cursor += count;
        }

        var straightThrough = run.Pcm.AsSpan(checked((int)(from * 2)), again.Length).ToArray();
        AssertSamePcm(straightThrough, again, frames, $"quantum {quantum}");
    }

    [TestMethod]
    public void TheWarmUpRunsEveryBranchOfTheStructuralPathAndMayRunAgain()
    {
        LiveSplice.WarmUp();
        LiveSplice.WarmUp();
    }

    [TestMethod]
    public void ASessionChangedDuringAnEditIsAnInvalidOperationWithALocalizedMessage()
    {
        var original = Text.Language;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var uiCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in new[] { "en", "nl", "de", "fr", "es" })
            {
                Text.Select(language);
                var error = new LiveSessionChangedException();
                Assert.IsInstanceOfType<InvalidOperationException>(error);
                Assert.AreEqual(Text.Get("SoundSessionChanged"), error.Message, language);
                Assert.AreNotEqual("SoundSessionChanged", error.Message, language);
            }
        }
        finally
        {
            Text.Select(original);
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = uiCulture;
        }
    }
}
