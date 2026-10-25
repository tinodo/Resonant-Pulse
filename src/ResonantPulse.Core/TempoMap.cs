namespace ResonantPulse.Core;

/// <summary>Resolved section coordinates; end boundaries belong to the next section. Count-in has StepIndex -1.</summary>
public sealed record SectionBoundary(string Name, int StepIndex, int RepeatIndex, long StartFrame, long EndFrame,
    Rational StartQuarter, Rational LengthQuarters, int Bars, int BeatsPerBar, int BeatDenominator, string Ramp);

internal sealed record ResolvedSection(string Name, int StepIndex, int RepeatIndex, Rational Start, Rational Length,
    double StartSeconds, double B0, double B1, double Unit, string Ramp, int Bars, int Numerator, int Denominator,
    int AudibleBars, int SilentBars, bool CountIn)
{
    internal Rational BarLength => new(4 * Numerator, Denominator);
    internal double DurationSeconds => SecondsAt(Length);
    internal double SecondsAt(Rational local)
    {
        double q = local.ToDouble(), length = Length.ToDouble();
        double b0 = B0 * Unit, b1 = B1 * Unit;
        double delta = b1 - b0;
        if (delta == 0 || Ramp == "Constant") return 60 * q / b0;
        if (Ramp == "LinearBeat")
        {
            double x = delta * q / (length * b0);
            // Series avoids cancellation for tiny ramps and positions.
            double log = Math.Abs(x) < 1e-6 ? x * (1 - x / 2 + x * x / 3 - x * x * x / 4) : Math.Log(1 + x);
            return 60 * length / delta * log;
        }
        double duration = 120 * length / (b0 + b1);
        double k = delta / duration;
        return 120 * q / (b0 + Math.Sqrt(b0 * b0 + 120 * k * q));
    }
    internal long FrameAt(Rational local, int rate) =>
        checked((long)Math.Round((StartSeconds + SecondsAt(local)) * rate, MidpointRounding.AwayFromZero));
    internal double QuarterAtSeconds(double seconds)
    {
        double b0 = B0 * Unit, b1 = B1 * Unit, length = Length.ToDouble(), delta = b1 - b0;
        if (delta == 0 || Ramp == "Constant") return Math.Clamp(seconds * b0 / 60, 0, length);
        if (Ramp == "LinearTime")
        {
            double k = delta / DurationSeconds;
            return Math.Clamp((b0 * seconds + k * seconds * seconds / 2) / 60, 0, length);
        }
        double x = delta * seconds / (60 * length);
        double expMinusOne = Math.Abs(x) < 1e-6 ? x * (1 + x / 2 + x * x / 6 + x * x * x / 24) : Math.Exp(x) - 1;
        return Math.Clamp(b0 * length / delta * expMinusOne, 0, length);
    }
    internal double TempoAtQuarter(double quarter)
    {
        if (Ramp == "Constant" || B0 == B1) return B0;
        if (Ramp == "LinearBeat") return B0 + (B1 - B0) * quarter / Length.ToDouble();
        double elapsed = SecondsAt(Rational.FromDouble(quarter));
        return B0 + (B1 - B0) * elapsed / DurationSeconds;
    }
}
