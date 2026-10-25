using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class RationalTests
{
    [TestMethod]
    public void Constructor_DefaultReductionAndSigns_ProducesCanonicalValues()
    {
        Assert.AreEqual(BigInteger.Zero, default(Rational).Numerator);
        Assert.AreEqual(BigInteger.One, default(Rational).Denominator);
        Assert.AreEqual(Rational.Zero, new Rational(0, -9));
        Assert.AreEqual(new Rational(3, 4), new Rational(6, 8));
        Assert.AreEqual(new Rational(3, 4), new Rational(-6, -8));
        Assert.AreEqual(new Rational(-3, 4), new Rational(6, -8));
        Rational large = long.MaxValue;
        Assert.AreEqual(new BigInteger(long.MaxValue), large.Numerator);
        Assert.AreEqual(Rational.One, (Rational)1);
        Assert.AreEqual("-2/1", ((Rational)(-2)).ToString());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public void Constructor_ZeroDenominator_ThrowsDivideByZeroException(int numerator) =>
        Check.Throws<DivideByZeroException>(() => _ = new Rational(numerator, 0));

    [TestMethod]
    public void Operators_IdentitySignsAndFractions_ReturnExactValues()
    {
        var a = new Rational(1, 2); var b = new Rational(1, 3);
        Assert.AreEqual(new Rational(5, 6), a + b);
        Assert.AreEqual(new Rational(1, 6), a - b);
        Assert.AreEqual(new Rational(-1, 2), -a);
        Assert.AreEqual(new Rational(1, 6), a * b);
        Assert.AreEqual(new Rational(3, 2), a / b);
        Assert.AreEqual(Rational.Zero, a + -a);
        Assert.AreEqual(a, a * Rational.One + Rational.Zero);
        Assert.AreEqual(new Rational(-1, 6), -a * b);
        Check.Throws<DivideByZeroException>(() => _ = a / Rational.Zero);
    }

    [TestMethod]
    public void EqualityComparisonAndHash_ReducedEquivalentValues_Agree()
    {
        var a = new Rational(1, 2); var b = new Rational(2, 4);
        Assert.IsTrue(a == b && a <= b && a >= b);
        Assert.IsFalse(a != b || a < b || a > b);
        Assert.IsTrue(-a < 0 && a > 0 && -a <= 0 && a >= 0);
        Assert.AreEqual(0, a.CompareTo(b));
        Assert.IsTrue(a.Equals(b) && a.Equals((object)b));
        Assert.IsFalse(a.Equals(null) || a.Equals("1/2"));
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        var huge = BigInteger.One << 2000;
        Assert.IsTrue(new Rational(huge + 1, huge) > Rational.One);
    }

    [TestMethod]
    [DataRow(3, 2, 1, 2, 1.5, "3/2")]
    [DataRow(-3, 2, -2, -1, -1.5, "-3/2")]
    [DataRow(-2, 1, -2, -2, -2.0, "-2/1")]
    [DataRow(0, 7, 0, 0, 0.0, "0/1")]
    public void FloorCeilingToDoubleAndToString_SignedValues_ReturnExpectedValues(
        int n, int d, int floor, int ceiling, double value, string text)
    {
        var q = new Rational(n, d);
        Assert.AreEqual(new BigInteger(floor), q.Floor());
        Assert.AreEqual(new BigInteger(ceiling), q.Ceiling());
        Assert.AreEqual(value, q.ToDouble());
        Assert.AreEqual(text, q.ToString());
    }

    [TestMethod]
    [DataRow("0.125", 1, 8)]
    [DataRow("1e-3", 1, 1000)]
    [DataRow("-6/-8", 3, 4)]
    [DataRow("+12.", 12, 1)]
    [DataRow("-.25", -1, 4)]
    [DataRow("2E+2", 200, 1)]
    public void Parse_FixedDecimalFractionAndExponentVectors_ReturnsExactRationals(string text, int n, int d)
    {
        var q = Rational.Parse(text);
        Assert.AreEqual(new Rational(n, d), q);
        Assert.IsTrue(q.Denominator.Sign > 0);
    }

    [TestMethod]
    public void Parse_LengthExponentAndMalformedBoundaries_EnforcesLimits()
    {
        Assert.AreEqual(Rational.One, Rational.Parse(new string('0', 255) + "1"));
        Check.Throws<FormatException>(() => Rational.Parse(new string('0', 256) + "1"));
        Assert.AreEqual(BigInteger.Pow(10, 1024), Rational.Parse("1e1024").Numerator);
        Assert.AreEqual(BigInteger.Pow(10, 1024), Rational.Parse("1e-1024").Denominator);
        foreach (var text in new[] { "1e1025", "1e-1025", "", " 1", "1 ", "1/2/3", "NaN", "1,5", ".", "1e" })
            Check.Throws<FormatException>(() => Rational.Parse(text));
        Check.Throws<DivideByZeroException>(() => Rational.Parse("1/0"));
        Check.Throws<ArgumentNullException>(() => Rational.Parse(null!));
    }

    [TestMethod]
    [DataRow("1.5\n")]
    [DataRow("1.5\r\n")]
    [DataRow("1/2\n")]
    [DataRow("1e2\n")]
    [DataRow("-1.25\n")]
    [DataRow("1/-2\n")]
    public void Parse_TrailingNewlines_AreRejectedByParserAndJsonImport(string text)
    {
        Check.Throws<FormatException>(() => Rational.Parse(text));
        Check.Throws<JsonException>(() => JsonSerializer.Deserialize<Rational>(JsonSerializer.Serialize(text)));
    }

    [TestMethod]
    public void FromDouble_RoundTripDecimalAndNonfiniteValues_UsesDocumentedContract()
    {
        Assert.AreEqual(new Rational(1, 10), Rational.FromDouble(0.1));
        Assert.AreEqual(new Rational(3, 2), Rational.FromDouble(1.5));
        Assert.AreEqual(Rational.Zero, Rational.FromDouble(-0.0));
        foreach (var x in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            Check.Throws<ArgumentException>(() => Rational.FromDouble(x));
        var huge = BigInteger.One << 2000;
        Assert.AreEqual(1.5, new Rational(huge * 3, huge * 2).ToDouble());
        Assert.AreEqual(0.5, new Rational(huge + 1, huge * 2 + 1).ToDouble(), 1e-15);
    }

    [TestMethod]
    public void JsonConverter_ReadWrite_UsesInvariantCanonicalString()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
            Assert.AreEqual("\"1/2\"", JsonSerializer.Serialize(new Rational(2, 4)));
            Assert.AreEqual(new Rational(-1, 8), JsonSerializer.Deserialize<Rational>("\"-0.125\""));
            Assert.AreEqual("\"0/1\"", JsonSerializer.Serialize(Rational.Zero));
            foreach (var json in new[] { "1", "null", "{}", "\"1/0\"", "\"bad\"" })
                Check.Throws<JsonException>(() => JsonSerializer.Deserialize<Rational>(json));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
