namespace ResonantPulse.App;

/// <summary>Whole-number rules for numeric fields.</summary>
internal static class WholeNumbers
{
    /// <summary>True, with the number, only when <paramref name="value"/> is a finite whole number that fits an int.</summary>
    internal static bool TryGet(double value, out int whole)
    {
        whole = 0;
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value is < int.MinValue or > int.MaxValue) return false;
        whole = (int)value;
        return true;
    }
}
