using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

[TestClass]
public sealed class DocumentValidatorTests
{
    private static PulseDocument Fixture() => new()
    {
        Lanes = [new() { Events = [new() { PositionQuarters = new Rational(1, 2) }] }],
        Program = [new() { Bars = 1 }],
        Setlist = [new() { Name = "Song", Notes = "Notes" }]
    };

    private static void Invalid(Action<PulseDocument> mutate)
    {
        var d = Fixture(); mutate(d);
        Check.Throws<ArgumentException>(() => DocumentValidator.Validate(d));
    }

    private static void Range(Action<PulseDocument, double> set, double min, double max)
    {
        foreach (var value in new[] { min, max })
        {
            var d = Fixture(); set(d, value); DocumentValidator.Validate(d);
            Assert.AreEqual(1, d.SchemaVersion);
        }
        foreach (var value in new[] { Math.BitDecrement(min), Math.BitIncrement(max),
            double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            Invalid(d => set(d, value));
    }

    [TestMethod]
    public void Validate_DefaultAndBoundaryDocuments_AcceptsSupportedValues()
    {
        DocumentValidator.Validate(new());
        foreach (var sound in new[] { "Wood", "Glass", "Click", "Kick", "HiHat" })
        {
            var d = Fixture(); d.Settings.Sound = sound; d.Lanes[0].Sound = sound;
            d.Lanes[0].Events[0].Tie = true; d.Settings.Accents = Enum.GetValues<Accent>();
            DocumentValidator.Validate(d);
            Assert.AreEqual(sound, d.Lanes[0].Sound);
        }
        foreach (var den in new[] { 1, 2, 4, 8, 16, 32, 64 })
        {
            var d = Check.Document(); d.Settings.BeatDenominator = den;
            d.Settings.BeatsPerBar = 64; d.Settings.Subdivision = 64; d.Settings.CountInBars = 32;
            DocumentValidator.Validate(d);
            Assert.AreEqual(den, d.Settings.BeatDenominator);
        }
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(1, true)]
    [DataRow(86400, true)]
    [DataRow(-1, false)]
    [DataRow(86401, false)]
    public void Validate_PracticeDuration_EnforcesBounds(int seconds, bool supported)
    {
        var document = Fixture();
        document.Settings.PracticeSeconds = seconds;
        if (supported) DocumentValidator.Validate(document);
        else Check.Throws<ArgumentException>(() => DocumentValidator.Validate(document));
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(1, true)]
    [DataRow(100000, true)]
    [DataRow(-1, false)]
    [DataRow(100001, false)]
    public void Validate_PracticeBars_EnforcesBounds(int bars, bool supported)
    {
        var document = Fixture();
        document.Settings.PracticeBars = bars;
        if (supported) DocumentValidator.Validate(document);
        else Check.Throws<ArgumentException>(() => DocumentValidator.Validate(document));
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(1, true)]
    [DataRow(6400000, true)]
    [DataRow(-1, false)]
    [DataRow(6400001, false)]
    public void Validate_PracticeBeats_EnforcesBounds(int beats, bool supported)
    {
        var document = Fixture();
        document.Settings.PracticeBeats = beats;
        if (supported) DocumentValidator.Validate(document);
        else Check.Throws<ArgumentException>(() => DocumentValidator.Validate(document));
    }

    [TestMethod]
    public void Validate_TempoMeterUnitSubdivisionAndSwing_EnforcesBounds()
    {
        Range((d, x) => d.Settings.Tempo = x, 1, 1000);
        Range((d, x) => d.Settings.BeatUnitQuarters = x, 1d / 64, 16);
        Range((d, x) => { d.Settings.Subdivision = 2; d.Settings.Swing = x; }, 0.5, 0.9);
        foreach (var n in new[] { 0, 65 }) Invalid(d => d.Settings.BeatsPerBar = n);
        foreach (var n in new[] { 0, 3, 65 }) Invalid(d => d.Settings.BeatDenominator = n);
        foreach (var n in new[] { 0, 65 }) Invalid(d => d.Settings.Subdivision = n);
        foreach (var n in new[] { -1, 33 }) Invalid(d => d.Settings.CountInBars = n);
        Invalid(d => { d.Settings.Subdivision = 3; d.Settings.Swing = 0.6; });
        var straight = Fixture(); straight.Settings.Subdivision = 3;
        DocumentValidator.Validate(straight);
        Assert.AreEqual(0.5, straight.Settings.Swing);
    }

    [TestMethod]
    public void Validate_GainPanProbabilityAndLaneGeometry_EnforcesBounds()
    {
        Range((d, x) => d.Lanes[0].Probability = x, 0, 1);
        Range((d, x) => d.Lanes[0].Events[0].Probability = x, 0, 1);
        Range((d, x) => { d.Lanes[0].Events[0].PositionQuarters = 0; d.Lanes[0].CycleQuarters = x; }, 1d / 1024, 4096);
        Range((d, x) => d.Lanes[0].OffsetQuarters = x, -4096, 4096);
        // Use float neighbors, not double neighbors that round back into the valid float domain.
        Action<PulseDocument, float>[] setters =
        [
            (d, x) => d.Settings.MasterGain = x, (d, x) => d.Lanes[0].Volume = x,
            (d, x) => d.Lanes[0].Events[0].Gain = x
        ];
        foreach (var set in setters)
        {
            foreach (float x in new[] { 0f, 1f }) { var d = Fixture(); set(d, x); DocumentValidator.Validate(d); }
            foreach (float x in new[] { float.BitDecrement(0), float.BitIncrement(1), float.NaN, float.NegativeInfinity, float.PositiveInfinity })
                Invalid(d => set(d, x));
        }
        foreach (float x in new[] { -1f, 1f }) { var d = Fixture(); d.Lanes[0].Pan = x; DocumentValidator.Validate(d); }
        foreach (float x in new[] { float.BitDecrement(-1), float.BitIncrement(1), float.NaN, float.NegativeInfinity, float.PositiveInfinity })
            Invalid(d => d.Lanes[0].Pan = x);
        foreach (var n in new[] { 0, 257 }) Invalid(d => d.Lanes[0].Pulses = n);
        var eight = Fixture(); eight.Lanes = Enumerable.Range(0, 8).Select(_ => new RhythmLane()).ToList();
        DocumentValidator.Validate(eight); eight.Lanes.Add(new());
        Check.Throws<ArgumentException>(() => DocumentValidator.Validate(eight));
    }

    [TestMethod]
    public void Validate_EnumsStringsGroupsAndGapPairs_RejectsInvalidStates()
    {
        Action<PulseDocument>[] bad =
        [
            d => d.SchemaVersion = 2, d => d.ProbabilityAlgorithm = "EVENT-HASH-V1",
            d => d.Settings.Sound = "wood", d => d.Lanes[0].Sound = "Sample",
            d => d.Settings.Accents = [(Accent)4], d => d.Lanes[0].Events[0].Accent = (Accent)(-1),
            d => d.Settings.Groups = [2, 1], d => d.Settings.Groups = [0, 4],
            d => d.Settings.Groups = [5, -1], d => d.Settings.AudibleBars = 1,
            d => d.Settings.SilentBars = 1, d => d.Program[0].AudibleBars = 1,
            d => { d.Settings.AudibleBars = 100001; d.Settings.SilentBars = 1; },
            d => { d.Settings.Groups = [2, 2]; d.Program[0].BeatDenominator = 8; }
        ];
        foreach (var mutate in bad) Invalid(mutate);
        var valid = Fixture(); valid.Settings.Groups = [2, 2];
        valid.Settings.AudibleBars = valid.Settings.SilentBars = 100000;
        DocumentValidator.Validate(valid);
        Assert.AreEqual(4, valid.Settings.Groups.Sum());
    }

    [TestMethod]
    public void Validate_NullGraphsAndTextLimits_RejectsDescriptively()
    {
        Check.Throws<ArgumentNullException>(() => DocumentValidator.Validate(null!));
        Action<PulseDocument>[] bad =
        [
            d => d.Settings = null!, d => d.Lanes = null!, d => d.Program = null!, d => d.Setlist = null!,
            d => d.Lanes[0] = null!, d => d.Program[0] = null!, d => d.Setlist[0] = null!,
            d => d.Setlist[0].Document = null!, d => d.Lanes[0].Events = null!,
            d => d.Lanes[0].Events[0] = null!, d => d.Settings.Accents = null!,
            d => d.Settings.Groups = null!, d => d.Lanes[0].Pattern = null!, d => d.Name = null!,
            d => d.Lanes[0].Name = null!, d => d.Program[0].Name = null!, d => d.Setlist[0].Notes = null!,
            d => d.Name = new string('x', 257), d => d.Lanes[0].Name = new string('x', 257),
            d => d.Program[0].Name = new string('x', 257), d => d.Setlist[0].Name = new string('x', 257),
            d => d.Setlist[0].Notes = new string('x', 32769)
        ];
        foreach (var mutate in bad) Invalid(mutate);
        var d = Fixture(); d.Name = d.Lanes[0].Name = d.Program[0].Name = d.Setlist[0].Name = new string('x', 256);
        d.Setlist[0].Notes = new string('x', 32768); DocumentValidator.Validate(d);
        d.Name = ""; DocumentValidator.Validate(d);
        Assert.AreEqual(32768, d.Setlist[0].Notes.Length);
    }

    [TestMethod]
    public void Validate_AuthoredEvents_EnforcesUniquenessRangeAndRepresentation()
    {
        Invalid(d => d.Lanes[0].Events.Add(new() { PositionQuarters = new Rational(2, 4) }));
        Invalid(d => d.Lanes[0].Events[0].PositionQuarters = -1);
        Invalid(d => d.Lanes[0].Events[0].PositionQuarters = 4);
        Invalid(d => d.Lanes[0].Pattern = [Accent.Normal]);
        Invalid(d => d.Lanes[0].Events[0].PositionQuarters = new Rational(1, BigInteger.One << 256));
        Invalid(d => d.Lanes[0].Events[0].PositionQuarters = new Rational(BigInteger.One << 256, (BigInteger.One << 256) + 1));
        Invalid(d => d.Lanes[0].Events[0].PositionQuarters = new Rational(BigInteger.One << 256, (BigInteger.One << 255) + 1));
        var d = Fixture();
        d.Lanes[0].Events = Enumerable.Range(0, 256).Select(i => new RhythmEvent
        { PositionQuarters = new Rational(i, 256), Accent = Accent.Silent, Tie = true }).ToList();
        DocumentValidator.Validate(d); Assert.AreEqual(256, d.Lanes[0].Events.Count);
        d.Lanes[0].Events.Add(new() { PositionQuarters = 2 });
        Check.Throws<ArgumentException>(() => DocumentValidator.Validate(d));
        d.Lanes[0].Events = [new() { PositionQuarters = new Rational(1, BigInteger.One << 255) }];
        DocumentValidator.Validate(d);
        d.Lanes[0].Events = [new() { PositionQuarters = new Rational((BigInteger.One << 255) + 1, (BigInteger.One << 255) - 1) }];
        DocumentValidator.Validate(d);
        d.Lanes[0].Events = []; d.Lanes[0].Pulses = 256; d.Lanes[0].Pattern = new Accent[256];
        DocumentValidator.Validate(d);
        d.Lanes[0].Pattern = new Accent[257];
        Check.Throws<ArgumentException>(() => DocumentValidator.Validate(d));
        Invalid(x => { x.Lanes[0].Events = []; x.Lanes[0].Pattern = new Accent[5]; });
    }

    [TestMethod]
    public void Validate_ProgramAndExpansionBudgets_EnforcesExactLimits()
    {
        foreach (var ramp in new[] { "Constant", "LinearTime", "LinearBeat" })
        {
            var d = Fixture(); d.Program[0].Ramp = ramp; DocumentValidator.Validate(d);
            Assert.AreEqual(ramp, d.Program[0].Ramp);
        }
        Action<PulseDocument>[] bad =
        [
            d => d.Program[0].EndTempo = 81, d => d.Program[0].Ramp = "linearBeat",
            d => d.Program[0].Bars = 0, d => d.Program[0].Bars = 100001,
            d => d.Program[0].Repeats = 0, d => d.Program[0].Repeats = 1025,
            d => d.Program[0].StartTempo = double.NaN, d => d.Program[0].EndTempo = 1001,
            d => d.Program[0].BeatsPerBar = 0, d => d.Program[0].BeatDenominator = 3
        ];
        foreach (var mutate in bad) Invalid(mutate);
        var sections = Fixture();
        sections.Program = Enumerable.Range(0, 4).Select(_ => new ProgramStep { Bars = 1, Repeats = 1024 }).ToList();
        DocumentValidator.Validate(sections); sections.Program.Add(new() { Bars = 1 });
        Check.Throws<ArgumentException>(() => DocumentValidator.Validate(sections));
        var bars = Fixture(); bars.Program[0].Bars = 100000; DocumentValidator.Validate(bars);
        bars.Program.Add(new() { Bars = 1 }); Check.Throws<ArgumentException>(() => DocumentValidator.Validate(bars));
        var steps = Fixture(); steps.Program = Enumerable.Range(0, 1024).Select(_ => new ProgramStep { Bars = 1 }).ToList();
        DocumentValidator.Validate(steps); steps.Program.Add(new() { Bars = 1 });
        Check.Throws<ArgumentException>(() => DocumentValidator.Validate(steps));
    }

    [TestMethod]
    public void Validate_SetlistDepthCountAndReferenceCycles_EnforcesGraphLimits()
    {
        var root = Check.Document(); var node = root;
        for (int i = 0; i < 8; i++) { var child = Check.Document(); node.Setlist.Add(new() { Document = child }); node = child; }
        DocumentValidator.Validate(root); node.Setlist.Add(new());
        Check.Throws<ArgumentException>(() => DocumentValidator.Validate(root));
        var self = Check.Document(); self.Setlist.Add(new() { Document = self });
        Check.Throws<ArgumentException>(() => DocumentValidator.Validate(self));
        var a = Check.Document(); var b = Check.Document(); a.Setlist.Add(new() { Document = b }); b.Setlist.Add(new() { Document = a });
        Check.Throws<ArgumentException>(() => DocumentValidator.Validate(a));
        var wide = Check.Document(); wide.Setlist = Enumerable.Range(0, 128).Select(_ => new SongEntry()).ToList();
        DocumentValidator.Validate(wide); wide.Setlist.Add(new());
        Check.Throws<ArgumentException>(() => DocumentValidator.Validate(wide));
        // Root + 128 children + 127 grandchildren = exactly 256 visits.
        wide.Setlist.RemoveAt(128);
        wide.Setlist[0].Document.Setlist = Enumerable.Range(0, 127).Select(_ => new SongEntry()).ToList();
        DocumentValidator.Validate(wide); wide.Setlist[0].Document.Setlist.Add(new());
        Check.Throws<ArgumentException>(() => DocumentValidator.Validate(wide));
    }

}
