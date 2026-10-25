using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class AccentPatternsTests
{
    [TestMethod]
    public void EmptyMainDefaultsToNormalButEmptyLaneHasStrongDownbeat()
    {
        CollectionAssert.AreEqual(
            new[] { Accent.Normal, Accent.Normal, Accent.Normal, Accent.Normal },
            AccentPatterns.ExpandMain([], 4));
        CollectionAssert.AreEqual(
            new[] { Accent.Strong, Accent.Normal, Accent.Normal, Accent.Normal },
            AccentPatterns.ExpandLane([], 4));
    }

    [TestMethod]
    public void SilentSingletonStaysSilentAcrossEveryVisibleMainBeat()
    {
        var source = new[] { Accent.Silent };
        CollectionAssert.AreEqual(Enumerable.Repeat(Accent.Silent, 7).ToArray(),
            AccentPatterns.ExpandMain(source, 7));
        for (var index = 0; index < 32; index++)
            Assert.AreEqual(Accent.Silent, AccentPatterns.MainAt(source, index));
        CollectionAssert.AreEqual(new[] { Accent.Silent }, source);
    }

    [TestMethod]
    public void ShortNonemptyPatternsRepeatInBothEditorsAndMaterialization()
    {
        var source = new[] { Accent.Silent, Accent.Soft, Accent.Strong };
        var expected = new[]
        {
            Accent.Silent, Accent.Soft, Accent.Strong, Accent.Silent,
            Accent.Soft, Accent.Strong, Accent.Silent
        };
        CollectionAssert.AreEqual(expected, AccentPatterns.ExpandMain(source, 7));
        CollectionAssert.AreEqual(expected, AccentPatterns.ExpandLane(source, 7));
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.AreEqual(expected[index], AccentPatterns.MainAt(source, index));
            Assert.AreEqual(expected[index], AccentPatterns.LaneAt(source, index));
        }
    }

    [TestMethod]
    public void ExactEventConversionPreservesImplicitLaneDownbeat()
    {
        var accents = AccentPatterns.ExpandLane([], 4);
        var events = Enumerable.Range(0, 4).Select(index => new RhythmEvent
        {
            PositionQuarters = new Rational(index, 1),
            Accent = accents[index]
        }).ToArray();
        CollectionAssert.AreEqual(
            new[] { Accent.Strong, Accent.Normal, Accent.Normal, Accent.Normal },
            events.Select(item => item.Accent).ToArray());
    }

    [TestMethod]
    public void LaneRotationAndInversionStartFromResolvedImplicitDefaults()
    {
        var accents = AccentPatterns.ExpandLane([], 4);
        CollectionAssert.AreEqual(
            new[] { Accent.Normal, Accent.Normal, Accent.Normal, Accent.Strong },
            accents.Skip(1).Append(accents[0]).ToArray());
        CollectionAssert.AreEqual(Enumerable.Repeat(Accent.Silent, 4).ToArray(),
            accents.Select(accent => accent == Accent.Silent ? Accent.Normal : Accent.Silent).ToArray());
    }
}
