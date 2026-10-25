namespace ResonantPulse.Core;

public sealed record TimingMatch(int ExpectedIndex, int ObservedIndex, double ExpectedSeconds, double ObservedSeconds)
{
    public double OffsetMilliseconds => (ObservedSeconds - ExpectedSeconds) * 1000;
}

/// <summary>Input-arrival statistics, not calibrated physical performance. Spread is 1.4826 times median absolute deviation.</summary>
public sealed record TimingReport
{
    public int Matched { get; init; }
    public int Missed { get; init; }
    public int Extra { get; init; }
    public double MedianOffsetMilliseconds { get; init; }
    public double SpreadMilliseconds { get; init; }
    public double Coverage { get; init; }
    public int AmbiguousExpected { get; init; }
    public double DriftMillisecondsPerSecond { get; init; }
    public IReadOnlyList<TimingMatch> Matches { get; init; } = Array.Empty<TimingMatch>();
}

/// <summary>Chronological one-to-one matching: maximize matches within tolerance, then minimize total absolute offset.</summary>
public static class TimingAnalyzer
{
    public static TimingReport Analyze(IReadOnlyList<double> expectedSeconds, IReadOnlyList<double> observedSeconds,
        double toleranceSeconds = 0.15)
    {
        ArgumentNullException.ThrowIfNull(expectedSeconds); ArgumentNullException.ThrowIfNull(observedSeconds);
        DocumentValidator.FiniteRange(toleranceSeconds, 0, 10, "Timing tolerance seconds");
        int n = expectedSeconds.Count, m = observedSeconds.Count;
        DocumentValidator.Require(n <= 100_000 && m <= 100_000 && (long)(n + 1) * (m + 1) <= 4_000_000,
            "Timing alignment exceeds four million comparison cells. Analyze shorter, nonoverlapping sections.");
        var expected = Ordered(expectedSeconds); var observed = Ordered(observedSeconds);
        int stride = m + 1;
        byte[] decisions = new byte[(n + 1) * stride];
        int[] counts = new int[stride], nextCounts = new int[stride];
        double[] costs = new double[stride], nextCosts = new double[stride];
        for (int j = 1; j <= m; j++) decisions[j] = 3;
        for (int i = 1; i <= n; i++)
        {
            decisions[i * stride] = 2;
            nextCounts[0] = 0; nextCosts[0] = 0;
            for (int j = 1; j <= m; j++)
            {
                int count = counts[j]; double cost = costs[j]; byte decision = 2;
                if (Better(nextCounts[j - 1], nextCosts[j - 1], count, cost))
                { count = nextCounts[j - 1]; cost = nextCosts[j - 1]; decision = 3; }
                double distance = Math.Abs(expected[i - 1].Time - observed[j - 1].Time);
                if (distance <= toleranceSeconds && (Better(counts[j - 1] + 1, costs[j - 1] + distance, count, cost) ||
                    (counts[j - 1] + 1 == count && costs[j - 1] + distance == cost)))
                { count = counts[j - 1] + 1; cost = costs[j - 1] + distance; decision = 1; }
                nextCounts[j] = count; nextCosts[j] = cost; decisions[i * stride + j] = decision;
            }
            (counts, nextCounts) = (nextCounts, counts); (costs, nextCosts) = (nextCosts, costs);
        }
        var matches = new List<TimingMatch>();
        int row = n, column = m;
        while (row > 0 || column > 0)
        {
            byte decision = decisions[row * stride + column];
            if (decision == 1)
            {
                var a = expected[--row]; var b = observed[--column];
                matches.Add(new(a.Index, b.Index, a.Time, b.Time));
            }
            else if (decision == 2) row--;
            else column--;
        }
        matches.Reverse();
        double[] offsets = matches.Select(p => p.OffsetMilliseconds).ToArray();
        double median = Median(offsets);
        double spread = 1.4826 * Median(offsets.Select(v => Math.Abs(v - median)).ToArray());
        int ambiguous = 0, left = 0, right = 0;
        foreach (var e in expected)
        {
            while (left < m && e.Time - observed[left].Time > toleranceSeconds) left++;
            if (right < left) right = left;
            while (right < m && Math.Abs(e.Time - observed[right].Time) <= toleranceSeconds) right++;
            if (right - left > 1) ambiguous++;
        }
        double drift = 0;
        if (matches.Count >= 4)
        {
            int half = matches.Count / 2;
            var first = matches.Take(half).ToArray(); var last = matches.Skip(matches.Count - half).ToArray();
            double delta = Median(last.Select(p => p.ExpectedSeconds).ToArray()) - Median(first.Select(p => p.ExpectedSeconds).ToArray());
            if (delta > 0)
                drift = (Median(last.Select(p => p.OffsetMilliseconds).ToArray()) - Median(first.Select(p => p.OffsetMilliseconds).ToArray())) / delta;
        }
        return new()
        {
            Matched = matches.Count, Missed = n - matches.Count, Extra = m - matches.Count,
            MedianOffsetMilliseconds = median, SpreadMilliseconds = spread, Coverage = n == 0 ? 0 : (double)matches.Count / n,
            AmbiguousExpected = ambiguous, DriftMillisecondsPerSecond = drift, Matches = matches.AsReadOnly()
        };
    }
    private static bool Better(int aCount, double aCost, int bCount, double bCost) => aCount > bCount || (aCount == bCount && aCost < bCost);
    private static (double Time, int Index)[] Ordered(IReadOnlyList<double> source)
    {
        var values = new (double Time, int Index)[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            if (!double.IsFinite(source[i]) || source[i] < 0) throw new ArgumentException("Timing timestamps must be finite, nonnegative seconds.");
            values[i] = (source[i], i);
        }
        Array.Sort(values, (a, b) => { int c = a.Time.CompareTo(b.Time); return c == 0 ? a.Index.CompareTo(b.Index) : c; });
        return values;
    }
    internal static double Median(double[] values)
    {
        if (values.Length == 0) return 0;
        Array.Sort(values);
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? values[middle - 1] / 2 + values[middle] / 2 : values[middle];
    }
}
