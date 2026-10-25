using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ResonantPulse.Core;

/// <summary>Reduced, arbitrary-precision rational. The default value is zero. JSON uses "numerator/denominator".</summary>
[JsonConverter(typeof(RationalJsonConverter))]
public readonly struct Rational : IComparable<Rational>, IEquatable<Rational>
{
    internal const int MaximumTextLength = 256;
    private readonly BigInteger denominator;
    public BigInteger Numerator { get; }
    public BigInteger Denominator => denominator.IsZero ? BigInteger.One : denominator;
    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException("A musical duration cannot have a zero denominator.");
        if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / gcd;
        this.denominator = denominator / gcd;
    }
    public static Rational Zero => default;
    public static Rational One => new(1, 1);
    public static implicit operator Rational(int value) => new(value, 1);
    public static implicit operator Rational(long value) => new(value, 1);
    /// <summary>Converts the invariant round-trip decimal spelling, not an approximate grid quantization.</summary>
    public static Rational FromDouble(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentException("Musical values must be finite.");
        return Parse(value.ToString("R", CultureInfo.InvariantCulture));
    }
    public static Rational Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaximumTextLength) throw new FormatException($"Rational values are limited to {MaximumTextLength} characters.");
        var slash = value.IndexOf('/');
        if (slash >= 0)
        {
            if (!Regex.IsMatch(value, @"\A[+-]?[0-9]+/[+-]?[0-9]+\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
                throw new FormatException("Expected numerator/denominator.");
            return new(BigInteger.Parse(value[..slash], CultureInfo.InvariantCulture),
                BigInteger.Parse(value[(slash + 1)..], CultureInfo.InvariantCulture));
        }
        if (!Regex.IsMatch(value, @"\A[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            throw new FormatException("Expected a rational fraction or invariant decimal.");
        var parts = value.Split(new[] { 'e', 'E' });
        if (parts.Length > 2) throw new FormatException("Invalid rational exponent.");
        int exponent = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        if (Math.Abs((long)exponent) > 1024) throw new FormatException("Rational exponent exceeds 1024.");
        string mantissa = parts[0];
        int dot = mantissa.IndexOf('.');
        int decimals = dot < 0 ? 0 : mantissa.Length - dot - 1;
        var n = BigInteger.Parse(mantissa.Replace(".", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        int power = exponent - decimals;
        return power >= 0 ? new(n * BigInteger.Pow(10, power), 1) : new(n, BigInteger.Pow(10, -power));
    }
    public BigInteger Floor() => Numerator.Sign >= 0 ? Numerator / Denominator :
        (Numerator - Denominator + 1) / Denominator;
    public BigInteger Ceiling() => -(-this).Floor();
    public double ToDouble()
    {
        double n = (double)Numerator, d = (double)Denominator;
        if (double.IsFinite(n) && double.IsFinite(d)) return n / d;
        if (Numerator.IsZero) return 0;
        var absolute = BigInteger.Abs(Numerator);
        int ns = checked((int)Math.Max(0, absolute.GetBitLength() - 53));
        int ds = checked((int)Math.Max(0, Denominator.GetBitLength() - 53));
        long exponent = (long)ns - ds;
        if (exponent > 1024) return Numerator.Sign > 0 ? double.PositiveInfinity : double.NegativeInfinity;
        if (exponent < -1075) return Numerator.Sign > 0 ? 0d : -0d;
        return Numerator.Sign * Math.ScaleB((double)(absolute >> ns) / (double)(Denominator >> ds), (int)exponent);
    }
    public int CompareTo(Rational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    public bool Equals(Rational other) => Numerator == other.Numerator && Denominator == other.Denominator;
    public override bool Equals(object? obj) => obj is Rational other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");
    public static Rational operator +(Rational a, Rational b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Rational operator -(Rational a, Rational b) => a + -b;
    public static Rational operator -(Rational a) => new(-a.Numerator, a.Denominator);
    public static Rational operator *(Rational a, Rational b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    public static Rational operator /(Rational a, Rational b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    public static bool operator ==(Rational a, Rational b) => a.Equals(b);
    public static bool operator !=(Rational a, Rational b) => !a.Equals(b);
    public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;
    public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;
    public static bool operator <=(Rational a, Rational b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Rational a, Rational b) => a.CompareTo(b) >= 0;
}

public sealed class RationalJsonConverter : JsonConverter<Rational>
{
    public override Rational Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("A rational must be a numerator/denominator string.");
        try { return Rational.Parse(reader.GetString()!); }
        catch (Exception e) when (e is FormatException or OverflowException or DivideByZeroException)
        { throw new JsonException("Invalid rational value.", e); }
    }
    public override void Write(Utf8JsonWriter writer, Rational value, JsonSerializerOptions options)
    {
        var text = value.ToString();
        if (text.Length > Rational.MaximumTextLength)
            throw new JsonException($"Canonical rational values are limited to {Rational.MaximumTextLength} characters.");
        writer.WriteStringValue(text);
    }
}
