using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

/// <summary>
/// The approximate Italian tempo markings that the Tempo panel shows beside the tempo typed in it (old PR #10, ported onto the
/// panel). The bands overlap on purpose, so two markings can fit one speed; a marking is about a speed, which is counted in quarter
/// notes a minute whatever note value the tempo is counted in; and what the guide names is the tempo that Done would apply, to the
/// tenth. TempoGuideTextTests checks the sentences around the names.
/// </summary>
[TestClass]
public sealed class TempoGuideTests
{
    private const int Rate = 48000;

    private static string[] Names(double tempo, double unit = 1) =>
        TempoGuide.For(tempo, unit)?.Names.ToArray() ?? throw new AssertFailedException($"No markings for {tempo} BPM counted in {unit} quarter notes.");

    // ---- the bands -----------------------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(1d, "Larghissimo")]
    [DataRow(24.5d, "Larghissimo")]
    [DataRow(30d, "Grave")]
    [DataRow(50d, "Largo")]
    [DataRow(63d, "Larghetto")]
    [DataRow(70d, "Adagio")]
    [DataRow(90d, "Andante")]
    [DataRow(110d, "Moderato")]
    [DataRow(140d, "Allegro")]
    [DataRow(160d, "Vivace")]
    [DataRow(180d, "Presto")]
    [DataRow(250d, "Prestissimo")]
    [DataRow(1000d, "Prestissimo")]
    public void ASpeedInsideABandIsGivenItsMarking(double tempo, string expected)
    {
        CollectionAssert.AreEqual(new[] { expected }, Names(tempo));
    }

    [TestMethod]
    [DataRow(25d, "Larghissimo,Grave")]
    [DataRow(40d, "Grave,Largo")]
    [DataRow(42d, "Grave,Largo")]
    [DataRow(45d, "Grave,Largo")]
    [DataRow(60d, "Largo,Larghetto")]
    [DataRow(66d, "Larghetto,Adagio")]
    [DataRow(76d, "Adagio,Andante")]
    [DataRow(108d, "Andante,Moderato")]
    [DataRow(120d, "Moderato,Allegro")]
    [DataRow(156d, "Allegro,Vivace")]
    [DataRow(168d, "Vivace,Presto")]
    [DataRow(170d, "Vivace,Presto")]
    [DataRow(176d, "Vivace,Presto")]
    [DataRow(200d, "Presto,Prestissimo")]
    public void WhereBandsOverlapOrMeetEveryMarkingIsNamedSoNoneIsPresentedAsTheOnlyAnswer(double tempo, string expected)
    {
        CollectionAssert.AreEqual(expected.Split(','), Names(tempo));
    }

    [TestMethod]
    public void NoSpeedFromTheSlowestToTheFastestLeavesTheGuideWithoutAMarking()
    {
        // A tenth of a quarter note a minute at a time, up to 1000 BPM counted in whole notes (16,000 quarter notes a minute is the most
        // the project allows: 1000 BPM of the longest note value it accepts).
        var order = TempoGuide.Names.ToList();
        var previous = -1;
        for (var step = 0; step <= 160_000; step++)
        {
            var rate = step / 10d;
            var names = TempoGuide.MarkingsAt(rate);
            Assert.IsTrue(names.Count is 1 or 2, $"{rate} quarter notes a minute: {names.Count} markings");
            var first = order.IndexOf(names[0]);
            Assert.IsTrue(first >= previous, $"{rate}: the guide went back from {(previous < 0 ? "" : order[previous])} to {names[0]}");
            if (names.Count == 2) Assert.AreEqual(first + 1, order.IndexOf(names[1]), $"{rate}: {names[0]} and {names[1]} are not neighbours");
            previous = first;
        }
        Assert.AreEqual(order.Count - 1, previous, "the fastest marking is reached");
    }

    [TestMethod]
    public void TheMarkingsAreTheElevenItalianTermsFromSlowToFast()
    {
        CollectionAssert.AreEqual(new[]
        {
            "Larghissimo", "Grave", "Largo", "Larghetto", "Adagio", "Andante", "Moderato", "Allegro", "Vivace", "Presto", "Prestissimo"
        }, TempoGuide.Names.ToArray());
    }

    // ---- a speed, not a number ------------------------------------------------------------------------------------------------

    [TestMethod]
    public void TheSameSpeedHasTheSameMarkingsInAnyNoteValue()
    {
        // 80 BPM counted in dotted quarters is as fast as 240 BPM counted in eighths (the Help's own example) and as 120 BPM counted
        // in quarters: the guide names the speed, so all of them are a Moderato and an Allegro.
        foreach (var (tempo, unit) in new[] { (120d, 1d), (80d, 1.5), (240d, 0.5), (60d, 2d), (30d, 4d), (480d, 0.25) })
        {
            var markings = TempoGuide.For(tempo, unit)!.Value;
            Assert.AreEqual(120d, markings.QuarterNotesPerMinute, $"{tempo} BPM counted in {unit} quarter notes");
            CollectionAssert.AreEqual(new[] { "Moderato", "Allegro" }, markings.Names.ToArray(), $"{tempo} BPM counted in {unit} quarter notes");
        }
    }

    [TestMethod]
    public void PickingATimeSignatureCountsItsOwnNoteSoTheSameNumberIsAnotherSpeed()
    {
        // The picker counts the written beat: eighth notes in 6/8, half notes in 2/2. The number in the box stays, so 120 BPM in 6/8 is
        // not the 120 quarter notes a minute that it is in 4/4. A guide that read the bare number would call 6/8 at 120 a Moderato.
        var sixEight = new PulseSettings { Tempo = 120 };
        MeterPresets.Apply(sixEight, 6, 8, [3, 3]);
        var slow = TempoGuide.For(sixEight.Tempo, sixEight.BeatUnitQuarters)!.Value;
        Assert.AreEqual(60d, slow.QuarterNotesPerMinute);
        CollectionAssert.AreEqual(new[] { "Largo", "Larghetto" }, slow.Names.ToArray());

        var cutTime = new PulseSettings { Tempo = 60 };
        MeterPresets.Apply(cutTime, 2, 2);
        Assert.AreEqual(120d, TempoGuide.For(cutTime.Tempo, cutTime.BeatUnitQuarters)!.Value.QuarterNotesPerMinute);

        var fourFour = new PulseSettings { Tempo = 120 };
        MeterPresets.Apply(fourFour, 4, 4);
        CollectionAssert.AreEqual(new[] { "Moderato", "Allegro" }, TempoGuide.For(fourFour.Tempo, fourFour.BeatUnitQuarters)!.Value.Names.ToArray());
    }

    [TestMethod]
    [DataRow(120d, 1d, 4, 4)]
    [DataRow(240d, 0.5, 6, 8)]
    [DataRow(80d, 1.5, 6, 8)]
    [DataRow(60d, 2d, 2, 2)]
    [DataRow(30d, 4d, 4, 4)]
    [DataRow(480d, 0.25, 4, 4)]
    [DataRow(100d, 0.5, 4, 4)]
    [DataRow(90.5d, 1.5, 6, 8)]
    public void TheGuideCountsQuarterNotesAMinuteAsTheEngineDoes(double tempo, double unit, int beats, int denominator)
    {
        // A lane with one click for every quarter note is placed by quarter-note position, which the engine turns into time through
        // the tempo and the note value; the spacing of its clicks is what a quarter note lasts.
        var document = LiveTestKit.Document(tempo: tempo, beats: beats, denominator: denominator, unit: unit, countIn: 0);
        document.Lanes.Add(new RhythmLane { Name = "Quarter notes", Pulses = 1, CycleQuarters = 1 });
        var plan = ScheduleCompiler.Compile(document, Rate, 4, false);
        var frames = plan.Onsets.Where(onset => onset.LaneIndex == 0).Select(onset => onset.Frame).ToArray();
        Assert.IsTrue(frames.Length >= 4, "the lane's clicks");
        var engine = 60d * Rate / (frames[3] - frames[2]);
        Assert.AreEqual(engine, TempoGuide.For(tempo, unit)!.Value.QuarterNotesPerMinute, 0.1,
            $"{tempo} BPM counted in {unit} quarter notes");
    }

    // ---- what is named: the tempo that Done would apply ---------------------------------------------------------------------

    [TestMethod]
    public void TheGuideNamesTheTempoThatDoneWouldApplyToTheTenth()
    {
        // The Tempo panel writes the tempo to a tenth (Math.Round(value, 1)), so 119.96 is 120 and 120.04 is 120; 119.94 is 119.9.
        CollectionAssert.AreEqual(new[] { "Moderato", "Allegro" }, Names(119.96));
        CollectionAssert.AreEqual(new[] { "Moderato", "Allegro" }, Names(120.04));
        CollectionAssert.AreEqual(new[] { "Moderato" }, Names(119.94));
        CollectionAssert.AreEqual(new[] { "Allegro" }, Names(120.06));
        Assert.AreEqual(1d, TempoGuide.For(0.96, 1)!.Value.QuarterNotesPerMinute, "0.96 is applied as 1.0, which the project accepts");
        Assert.AreEqual(1000d, TempoGuide.For(1000.04, 1)!.Value.QuarterNotesPerMinute, "1000.04 is applied as 1000.0");
    }

    [TestMethod]
    public void TheMarkingsFollowTheSpeedAsTheSentencePrintsIt()
    {
        // 100.1 BPM counted in sixteenth notes is 25.025 quarter notes a minute and 99.9 BPM is 24.975. The sentence prints a tenth,
        // 25, so what it names is what 25 has: the line is never one thing in numbers and another in names.
        foreach (var tempo in new[] { 100.1, 99.9 })
        {
            var markings = TempoGuide.For(tempo, 0.25)!.Value;
            Assert.AreEqual(25d, markings.QuarterNotesPerMinute, $"{tempo} BPM");
            CollectionAssert.AreEqual(new[] { "Larghissimo", "Grave" }, markings.Names.ToArray(), $"{tempo} BPM");
        }
    }

    [TestMethod]
    [DataRow(double.NaN, 1d)]
    [DataRow(double.PositiveInfinity, 1d)]
    [DataRow(double.NegativeInfinity, 1d)]
    [DataRow(0d, 1d)]
    [DataRow(-5d, 1d)]
    [DataRow(0.94d, 1d)]
    [DataRow(1000.1d, 1d)]
    [DataRow(5000d, 1d)]
    [DataRow(120d, double.NaN)]
    [DataRow(120d, 0d)]
    [DataRow(120d, -1d)]
    [DataRow(120d, 16.5d)]
    [DataRow(120d, double.PositiveInfinity)]
    public void ATempoOrANoteValueTheProjectWouldRefuseIsNotGivenAMarking(double tempo, double unit)
    {
        Assert.IsNull(TempoGuide.For(tempo, unit));
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(-0.1)]
    public void ASpeedThatIsNotANumberOfQuarterNotesIsRejectedRatherThanAssignedAMarking(double rate)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TempoGuide.MarkingsAt(rate));
    }

    [TestMethod]
    public void TheSlowestAndFastestSpeedsTheProjectAllowsStillHaveAMarking()
    {
        // 1 BPM counted in sixteenth notes is a quarter of a quarter note a minute; 1000 BPM counted in whole notes is 4000.
        Assert.AreEqual("Larghissimo", Names(1, 0.25).Single());
        Assert.AreEqual("Larghissimo", Names(1, 1d / 64).Single());
        Assert.AreEqual("Prestissimo", Names(1000, 16).Single());
    }
}
