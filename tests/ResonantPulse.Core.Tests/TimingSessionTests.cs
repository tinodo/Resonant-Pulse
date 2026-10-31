using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class TimingSessionTests
{
    private static TimingSessionContext Context() => TimingTestKit.Context();

    [TestMethod]
    public void Session_CopiesItsInputsSoLaterChangesToThemCannotReachIt()
    {
        double[] expected = [1, 2];
        var observed = new List<double> { 1.01, 2.02 };
        var context = Context();
        var session = new TimingSession(context, expected, observed, 0.1, TimingTestKit.Completed);
        expected[0] = 100;
        observed[0] = 200;
        observed.Add(3);
        context = context with { ProjectName = "Changed" };
        Assert.AreEqual(1d, session.ExpectedSeconds[0]);
        Assert.AreEqual(1.01, session.ObservedSeconds[0]);
        Assert.AreEqual(2, session.ObservedSeconds.Count);
        Assert.AreEqual("Practice", session.Context.ProjectName);
        Assert.AreEqual(2, session.Report.Matched);
        Assert.AreEqual(0.1, session.ToleranceSeconds);
        Assert.AreEqual(5d, session.DurationSeconds);
        // What it hands out cannot be used to change it either.
        Assert.IsNotInstanceOfType<double[]>(session.ExpectedSeconds);
        Assert.IsNotInstanceOfType<double[]>(session.ObservedSeconds);
        Check.Throws<NotSupportedException>(() => ((IList<double>)session.ExpectedSeconds)[0] = 9);
        Check.Throws<NotSupportedException>(() => ((IList<double>)session.ObservedSeconds).Add(9));
        Check.Throws<NotSupportedException>(() => ((IList<TimingMatch>)session.Report.Matches).Clear());
    }

    [TestMethod]
    public void Session_KeepsTheTimeItWasCompletedAtInUtc()
    {
        var local = new DateTimeOffset(2026, 10, 31, 1, 46, 32, 242, TimeSpan.FromHours(1));
        var session = new TimingSession(Context(), [], [], 0.1, local);
        Assert.AreEqual(TimeSpan.Zero, session.CompletedAtUtc.Offset);
        Assert.AreEqual(local.UtcDateTime, session.CompletedAtUtc.UtcDateTime);
        Assert.AreEqual(TimingTestKit.Completed, new TimingSession(Context(), [], [], 0.1, TimingTestKit.Completed).CompletedAtUtc);

        var before = DateTimeOffset.UtcNow;
        var now = new TimingSession(Context(), [], [], 0.1).CompletedAtUtc;
        var after = DateTimeOffset.UtcNow;
        Assert.AreEqual(TimeSpan.Zero, now.Offset);
        Assert.IsTrue(now >= before && now <= after, $"{now:O} is not between {before:O} and {after:O}");
    }

    [TestMethod]
    public void Session_AcceptsEveryContextAtTheEdgesOfWhatARunCanProduce()
    {
        var longest = new string('x', TimingSession.MaximumNameLength);
        foreach (var context in new[]
        {
            Context() with { ProjectName = longest, TargetName = longest },
            Context() with { ProjectName = "" , TargetName = "" },
            Context() with { LaneIndex = -1 }, Context() with { LaneIndex = 0, TargetName = "Lane 1" },
            Context() with { LaneIndex = 7, TargetName = "Lane 8" },
            Context() with { SampleRate = 8000 }, Context() with { SampleRate = 192000 },
            Context() with { StartFrame = 0, EndFrame = 0 },
            // A run that went to the ten-minute limit.
            Context() with { StartFrame = 5, EndFrame = 5 + 600L * 8000 }
        })
        {
            var session = new TimingSession(context, [], [], 0.1, TimingTestKit.Completed);
            Assert.AreEqual(context, session.Context);
        }
        Assert.AreEqual(0d, new TimingSession(Context() with { EndFrame = 1234 }, [], [], 0, TimingTestKit.Completed).DurationSeconds);
        Assert.AreEqual(600d, new TimingSession(Context() with { EndFrame = 1234 + 600L * 8000 }, [], [], 0.1, TimingTestKit.Completed).DurationSeconds);
    }

    [TestMethod]
    public void Session_RejectsAContextThatNoRunCouldHaveProduced()
    {
        var tooLong = new string('x', TimingSession.MaximumNameLength + 1);
        foreach (var context in new[]
        {
            Context() with { ProjectName = null! }, Context() with { TargetName = null! },
            Context() with { ProjectName = tooLong }, Context() with { TargetName = tooLong },
            Context() with { LaneIndex = -2 }, Context() with { LaneIndex = 8 },
            Context() with { SampleRate = 0 }, Context() with { SampleRate = 7999 }, Context() with { SampleRate = 192001 },
            Context() with { StartFrame = -1 }, Context() with { EndFrame = 1233 },
            // Longer than a check can last (ten minutes).
            Context() with { EndFrame = 1234 + 600L * 8000 + 1 }, Context() with { EndFrame = long.MaxValue }
        })
            Check.Throws<ArgumentException>(() => new TimingSession(context, [], [], 0.1, TimingTestKit.Completed));
    }

    [TestMethod]
    public void Session_RejectsANameThatIsNotWellFormedText()
    {
        // Half of a surrogate pair is not text: it cannot be written as JSON, so it is refused here and not when the file is written.
        foreach (var name in new[] { "a\uD800b", "\uDC00", "a high half at the end \uD83D", "\uDC00\uD800" })
        {
            Check.Throws<ArgumentException>(() => new TimingSession(Context() with { ProjectName = name }, [], [], 0.1, TimingTestKit.Completed));
            Check.Throws<ArgumentException>(() => new TimingSession(Context() with { TargetName = name }, [], [], 0.1, TimingTestKit.Completed));
        }
        // A whole pair is one character, and is fine.
        var emoji = new TimingSession(Context() with { ProjectName = "Drums \U0001F941" }, [], [], 0.1, TimingTestKit.Completed);
        Assert.AreEqual("Drums \U0001F941", emoji.Context.ProjectName);
    }

    [TestMethod]
    public void Session_RejectsTimestampsOutsideTheWindowOfTheRun()
    {
        // The window is [0, 5): the end frame itself is not in it.
        foreach (var time in new[] { -1d, -double.Epsilon, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 5, 5.5, double.MaxValue })
        {
            Check.Throws<ArgumentException>(() => new TimingSession(Context(), [time], [], 0.1, TimingTestKit.Completed));
            Check.Throws<ArgumentException>(() => new TimingSession(Context(), [], [time], 0.1, TimingTestKit.Completed));
        }
        var last = Math.BitDecrement(5d);
        var inside = new TimingSession(Context(), [0, last], [0, last], 0, TimingTestKit.Completed);
        Assert.AreEqual(2, inside.Report.Matched);
    }

    [TestMethod]
    public void Session_RejectsMissingArgumentsAndAToleranceTheAnalyzerRefuses()
    {
        Check.Throws<ArgumentNullException>(() => new TimingSession(null!, [], [], 0.1, TimingTestKit.Completed));
        Check.Throws<ArgumentNullException>(() => new TimingSession(Context(), null!, [], 0.1, TimingTestKit.Completed));
        Check.Throws<ArgumentNullException>(() => new TimingSession(Context(), [], null!, 0.1, TimingTestKit.Completed));
        foreach (var tolerance in new[] { double.NaN, -0.001, 10.001, double.PositiveInfinity })
            Check.Throws<ArgumentException>(() => new TimingSession(Context(), [], [], tolerance, TimingTestKit.Completed));
        Assert.AreEqual(10d, new TimingSession(Context(), [], [], 10, TimingTestKit.Completed).ToleranceSeconds);
    }

    [TestMethod]
    public void Session_KeepsTheAnalyzersBudgetsAndSaysSo()
    {
        // 1,999 x 1,999 is the largest square that the analyzer takes (2,000 x 2,000 is four million and one cells with the borders).
        var square = new TimingSession(Context(), new double[1999], new double[1999], 0, TimingTestKit.Completed);
        Assert.AreEqual(1999, square.Report.Matched);
        var wide = new TimingSession(Context(), new double[TimingAnalyzer.MaximumTimestamps], [], 0.1, TimingTestKit.Completed);
        Assert.AreEqual(TimingAnalyzer.MaximumTimestamps, wide.Report.Missed);
        var tooMany = Check.Throws<ArgumentException>(() =>
            new TimingSession(Context(), new double[TimingAnalyzer.MaximumTimestamps + 1], [], 0.1, TimingTestKit.Completed));
        StringAssert.Contains(tooMany.Message, "shorter section");
        Check.Throws<ArgumentException>(() => new TimingSession(Context(), [], new double[TimingAnalyzer.MaximumTimestamps + 1], 0.1, TimingTestKit.Completed));
        Check.Throws<ArgumentException>(() => new TimingSession(Context(), new double[2000], new double[2000], 0.1, TimingTestKit.Completed));
    }

    [TestMethod]
    public void Session_ReportsExactlyWhatTheAnalyzerFound()
    {
        double[] expected = [0, 1, 2, 3, 4];
        double[] observed = [0.015625, 1.03125, 2.046875, 3.0625, 4.75];
        var session = new TimingSession(Context(), expected, observed, 0.125, TimingTestKit.Completed);
        var direct = TimingAnalyzer.Analyze(expected, observed, 0.125);
        Assert.AreEqual(direct.Matched, session.Report.Matched);
        Assert.AreEqual(direct.Missed, session.Report.Missed);
        Assert.AreEqual(direct.Extra, session.Report.Extra);
        Assert.AreEqual(direct.MedianOffsetMilliseconds, session.Report.MedianOffsetMilliseconds);
        Assert.AreEqual(direct.SpreadMilliseconds, session.Report.SpreadMilliseconds);
        Assert.AreEqual(direct.DriftMillisecondsPerSecond, session.Report.DriftMillisecondsPerSecond);
        Assert.AreEqual(direct.HasDriftEstimate, session.Report.HasDriftEstimate);
        Assert.IsTrue(session.Report.HasDriftEstimate);
        Assert.AreEqual(15.625, session.Report.DriftMillisecondsPerSecond);
        Check.Sequence(direct.Matches, session.Report.Matches);
    }

    [TestMethod]
    public void Session_ADriftThatOverflowsIsUnavailableNotInfinite()
    {
        // Four targets about 1e-320 seconds apart cannot be told apart in time: dividing the change in offset by that
        // gap gives more than a double can hold. The report says there is no estimate, and every number in it is finite.
        var session = new TimingSession(Context(), [0, 1e-320, 2e-320, 3e-320], [1, 1.01, 1.02, 1.03], 2, TimingTestKit.Completed);
        Assert.AreEqual(4, session.Report.Matched);
        Assert.IsFalse(session.Report.HasDriftEstimate);
        Assert.AreEqual(0d, session.Report.DriftMillisecondsPerSecond);
        Assert.IsTrue(double.IsFinite(session.Report.MedianOffsetMilliseconds) && double.IsFinite(session.Report.SpreadMilliseconds));
    }
}
