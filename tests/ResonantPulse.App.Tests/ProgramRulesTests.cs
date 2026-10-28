using System.Globalization;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class ProgramRulesTests
{
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static PulseDocument DocumentWith(params ProgramStep[] steps) => new() { Program = [.. steps] };

    // ---- steps ------------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void ANewStepStartsFromTheProjectsOwnTempoAndTimeSignature()
    {
        var settings = new PulseSettings { Tempo = 92.5, BeatsPerBar = 7, BeatDenominator = 8 };
        var step = ProgramEdits.NewStep(settings, "Step 1");
        Assert.AreEqual("Step 1", step.Name);
        Assert.AreEqual(92.5, step.StartTempo);
        Assert.AreEqual(92.5, step.EndTempo);
        Assert.AreEqual(7, step.BeatsPerBar);
        Assert.AreEqual(8, step.BeatDenominator);
        Assert.IsTrue(ProgramEdits.IsSteady(step));
        DocumentValidator.Validate(DocumentWith(step));
    }

    [TestMethod]
    public void ChoosingASteadyTempoMakesTheEndTempoTheStartTempoAndAnyRampKeepsBoth()
    {
        var step = new ProgramStep { StartTempo = 60, EndTempo = 120, Ramp = "LinearBeat" };
        ProgramEdits.SetRamp(step, "Constant");
        Assert.AreEqual(60, step.EndTempo);
        DocumentValidator.Validate(DocumentWith(step));

        ProgramEdits.SetRamp(step, "LinearTime");
        Assert.AreEqual("LinearTime", step.Ramp);
        Assert.AreEqual(60, step.StartTempo);
        Assert.AreEqual(60, step.EndTempo, "choosing a ramp keeps the step sounding the same until the end tempo is changed");
        DocumentValidator.Validate(DocumentWith(step));

        ProgramEdits.SetEndTempo(step, 140);
        Assert.AreEqual(140, step.EndTempo);
        DocumentValidator.Validate(DocumentWith(step));
    }

    [TestMethod]
    public void EveryTempoBehaviourLeavesAValidStep()
    {
        foreach (var ramp in ProgramEdits.Ramps)
        {
            var step = new ProgramStep { StartTempo = 80, EndTempo = 130, Ramp = "LinearTime" };
            ProgramEdits.SetRamp(step, ramp);
            ProgramEdits.SetStartTempo(step, 100);
            ProgramEdits.SetEndTempo(step, 150);
            DocumentValidator.Validate(DocumentWith(step));
            Assert.AreEqual(100, step.StartTempo, ramp);
        }
    }

    [TestMethod]
    public void ASteadyStepHasOneTempoWhicheverBoxIsEdited()
    {
        var step = new ProgramStep { StartTempo = 80, EndTempo = 80 };
        ProgramEdits.SetStartTempo(step, 96);
        Assert.AreEqual(96, step.StartTempo);
        Assert.AreEqual(96, step.EndTempo);
        ProgramEdits.SetEndTempo(step, 200);
        Assert.AreEqual(96, step.EndTempo, "a steady step has no end tempo of its own");
        DocumentValidator.Validate(DocumentWith(step));
    }

    [TestMethod]
    public void ARampTempoChangesOnlyTheTempoThatWasEdited()
    {
        var step = new ProgramStep { StartTempo = 60, EndTempo = 120, Ramp = "LinearBeat" };
        ProgramEdits.SetStartTempo(step, 70);
        Assert.AreEqual(120, step.EndTempo);
        ProgramEdits.SetEndTempo(step, 140);
        Assert.AreEqual(70, step.StartTempo);
        Assert.AreEqual(140, step.EndTempo);
    }

    [TestMethod]
    public void LeavingBarsSilentSetsBothNumbersTogetherAndTurningItOffClearsThem()
    {
        var step = new ProgramStep();
        Assert.IsFalse(ProgramEdits.HasSilentBars(step));
        ProgramEdits.SetSilentBars(step, true);
        Assert.AreEqual(ProgramEdits.DefaultAudibleBars, step.AudibleBars);
        Assert.AreEqual(ProgramEdits.DefaultSilentBars, step.SilentBars);
        Assert.IsTrue(ProgramEdits.HasSilentBars(step));
        DocumentValidator.Validate(DocumentWith(step));

        ProgramEdits.SetSilentBars(step, false);
        Assert.AreEqual(0, step.AudibleBars);
        Assert.AreEqual(0, step.SilentBars);
        Assert.IsFalse(ProgramEdits.HasSilentBars(step));
        DocumentValidator.Validate(DocumentWith(step));
    }

    [TestMethod]
    public void OnlyAGroupedProjectFixesTheTimeSignatureOfItsSteps()
    {
        Assert.IsFalse(ProgramEdits.MeterIsFixed(new PulseSettings()));
        Assert.IsTrue(ProgramEdits.MeterIsFixed(new PulseSettings { BeatsPerBar = 7, BeatDenominator = 8, Groups = [2, 2, 3] }));
    }

    [TestMethod]
    public void AProgramIsOverTheLimitOnlyWhenTheProjectWouldRefuseIt()
    {
        Assert.IsNull(ProgramEdits.BrokenLimit([]));
        Assert.IsNull(ProgramEdits.BrokenLimit([new ProgramStep { Bars = 100_000, Repeats = 1 }]));
        Assert.AreEqual("ProgramTooLong", ProgramEdits.BrokenLimit([new ProgramStep { Bars = 50_000, Repeats = 3 }]));
        Assert.AreEqual("ProgramTooLong", ProgramEdits.BrokenLimit([new ProgramStep { Bars = 1, Repeats = 1000 }, new ProgramStep { Bars = 1, Repeats = 1000 },
            new ProgramStep { Bars = 1, Repeats = 1000 }, new ProgramStep { Bars = 1, Repeats = 1000 }, new ProgramStep { Bars = 1, Repeats = 1000 }]),
            "five thousand repeats are more than 4096 sections");
        Assert.AreEqual("StepLimit", ProgramEdits.BrokenLimit(Enumerable.Range(0, 1025).Select(_ => new ProgramStep()).ToList()));
        Assert.IsNull(ProgramEdits.BrokenLimit(Enumerable.Range(0, 1024).Select(_ => new ProgramStep()).ToList()));
    }

    [TestMethod]
    public void EveryLimitTheRulesNameIsOneTheProjectRefusesToo()
    {
        var tooLong = DocumentWith(new ProgramStep { Bars = 50_000, Repeats = 3 });
        Assert.ThrowsExactly<ArgumentException>(() => DocumentValidator.Validate(tooLong));
        var tooManySteps = DocumentWith(Enumerable.Range(0, 1025).Select(_ => new ProgramStep()).ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => DocumentValidator.Validate(tooManySteps));
    }

    [TestMethod]
    public void EverythingTheRulesAllowAtTheirLimitsIsAcceptedByTheProjectToo()
    {
        DocumentValidator.Validate(DocumentWith(Enumerable.Range(0, ProgramEdits.MaxSteps).Select(_ => new ProgramStep()).ToArray()));
        DocumentValidator.Validate(DocumentWith(new ProgramStep { Bars = 100_000, Repeats = 1 }));
        DocumentValidator.Validate(DocumentWith(new ProgramStep { Bars = 1, Repeats = 1024 }, new ProgramStep { Bars = 1, Repeats = 1024 },
            new ProgramStep { Bars = 1, Repeats = 1024 }, new ProgramStep { Bars = 1, Repeats = 1024 }));
    }

    [TestMethod]
    public void MovingAStepSwapsItWithItsNeighbourAndStopsAtTheEnds()
    {
        var program = new List<ProgramStep> { new() { Name = "A" }, new() { Name = "B" }, new() { Name = "C" } };
        Assert.IsTrue(ProgramEdits.Move(program, 1, -1));
        CollectionAssert.AreEqual(new[] { "B", "A", "C" }, program.Select(step => step.Name).ToArray());
        Assert.IsTrue(ProgramEdits.Move(program, 1, 1));
        CollectionAssert.AreEqual(new[] { "B", "C", "A" }, program.Select(step => step.Name).ToArray());
        Assert.IsFalse(ProgramEdits.Move(program, 0, -1));
        Assert.IsFalse(ProgramEdits.Move(program, 2, 1));
        Assert.IsFalse(ProgramEdits.Move(program, 3, -1));
        Assert.IsFalse(ProgramEdits.Move(program, 1, 0));
        CollectionAssert.AreEqual(new[] { "B", "C", "A" }, program.Select(step => step.Name).ToArray());
    }

    [TestMethod]
    public void ACopyOfAStepIsNamedAfterItAndIsAnotherStep()
    {
        var original = new ProgramStep { Name = "Warm up", StartTempo = 60, EndTempo = 90, Ramp = "LinearTime", Bars = 4, Repeats = 2 };
        var copy = ProgramEdits.Copy(original, " (copy)");
        Assert.AreEqual("Warm up (copy)", copy.Name);
        Assert.AreEqual(original.StartTempo, copy.StartTempo);
        Assert.AreEqual(original.EndTempo, copy.EndTempo);
        Assert.AreEqual(original.Ramp, copy.Ramp);
        Assert.AreEqual(original.Bars, copy.Bars);
        Assert.AreEqual(original.Repeats, copy.Repeats);
        copy.Bars = 99;
        Assert.AreEqual(4, original.Bars);
        var longest = ProgramEdits.Copy(new ProgramStep { Name = new string('x', LaneEdits.MaxNameLength) }, " (copy)");
        Assert.AreEqual(LaneEdits.MaxNameLength, longest.Name.Length);
    }

    // ---- positions ----------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void ANewPositionGoesInTheMiddleOfTheLongestGap()
    {
        Rational Q(int numerator, int denominator = 1) => new(numerator, denominator);
        Assert.AreEqual(Q(0), PositionEdits.Midpoint(Q(4), []));
        Assert.AreEqual(Q(2), PositionEdits.Midpoint(Q(4), [Q(0)]));
        Assert.AreEqual(Q(5, 2), PositionEdits.Midpoint(Q(4), [Q(0), Q(1)]), "between 1 and the end of the cycle");
        Assert.AreEqual(Q(5, 2), PositionEdits.Midpoint(Q(4), [Q(1)]), "the gap after the only position is longer than the one before it");
        Assert.AreEqual(Q(1), PositionEdits.Midpoint(Q(4), [Q(2), Q(3)]), "the longest gap is the one before the first position");
    }

    [TestMethod]
    public void ANewPositionNeverLandsOnOneThatIsTaken()
    {
        var random = new Random(7);
        for (var round = 0; round < 200; round++)
        {
            var cycle = new Rational(random.Next(1, 33), random.Next(1, 5));
            var taken = new HashSet<Rational>();
            for (var count = random.Next(0, 12); count > 0; count--)
                taken.Add(cycle * new Rational(random.Next(0, 1000), 1000));
            var point = PositionEdits.Midpoint(cycle, taken);
            Assert.IsFalse(taken.Contains(point), $"{point} is taken in a cycle of {cycle}");
            Assert.IsTrue(point >= Rational.Zero && point < cycle, $"{point} is outside a cycle of {cycle}");
        }
    }

    [TestMethod]
    public void APositionIsReadAsAWholeNumberAFractionOrADecimalInTheLanguagesOwnFormat()
    {
        Assert.IsTrue(PositionEdits.TryParse(" 2 ", English, out var whole));
        Assert.AreEqual(new Rational(2, 1), whole);
        Assert.IsTrue(PositionEdits.TryParse("4/3", English, out var fraction));
        Assert.AreEqual(new Rational(4, 3), fraction);
        Assert.IsTrue(PositionEdits.TryParse("0.375", English, out var point));
        Assert.AreEqual(new Rational(3, 8), point);
        Assert.IsTrue(PositionEdits.TryParse("0,375", Dutch, out var comma));
        Assert.AreEqual(new Rational(3, 8), comma, "a Dutch decimal comma");
        Assert.IsTrue(PositionEdits.TryParse("0.375", Dutch, out var dotInDutch));
        Assert.AreEqual(new Rational(3, 8), dotInDutch, "a point is always a decimal point");
        Assert.IsTrue(PositionEdits.TryParse("1e1", English, out var scientific));
        Assert.AreEqual(new Rational(10, 1), scientific);
    }

    [TestMethod]
    public void TextThatIsNotAPositionIsRefusedNotGuessedAt()
    {
        foreach (var text in new[] { null, "", "  ", "abc", "1/0", "1/", "/3", "1,2,3", "1.2.3", "--1", "0x10", "1 / 3", "½" })
            Assert.IsFalse(PositionEdits.TryParse(text, English, out _), $"'{text}'");
        // A comma is a decimal comma only where the language has one, and only when it is the only separator.
        Assert.IsFalse(PositionEdits.TryParse("0,5", English, out _));
        Assert.IsFalse(PositionEdits.TryParse("0,5.5", Dutch, out _));
    }

    [TestMethod]
    public void APositionMustLieInsideTheCycleAndNotBeTaken()
    {
        Rational Q(int numerator, int denominator = 1) => new(numerator, denominator);
        Assert.IsNull(PositionEdits.Problem(Q(0), Q(4), []));
        Assert.IsNull(PositionEdits.Problem(Q(15, 4), Q(4), [Q(0), Q(1)]));
        Assert.AreEqual("PositionRange", PositionEdits.Problem(Q(4), Q(4), []), "the cycle itself is the start of the next one");
        Assert.AreEqual("PositionRange", PositionEdits.Problem(Q(-1, 8), Q(4), []));
        Assert.AreEqual("PositionRange", PositionEdits.Problem(Q(9), Q(4), [Q(9)]));
        Assert.AreEqual("PositionTaken", PositionEdits.Problem(Q(1), Q(4), [Q(0), Q(2, 2)]));
    }

    [TestMethod]
    public void APositionIsRefusedForPrecisionExactlyWhereTheProjectWouldRefuseIt()
    {
        var cycle = new Rational(4, 1);
        var most = new Rational(1, BigInteger.Pow(2, 255));
        var tooMuch = new Rational(1, BigInteger.Pow(2, 256));
        Assert.IsTrue(PositionEdits.FitsProject(most));
        Assert.IsFalse(PositionEdits.FitsProject(tooMuch));
        Assert.IsNull(PositionEdits.Problem(most, cycle, []));
        Assert.AreEqual("PositionPrecise", PositionEdits.Problem(tooMuch, cycle, []));

        // The same line the project draws, so what the Studio accepts is never refused later with a vaguer message.
        static PulseDocument WithPosition(Rational position) => new()
        {
            Lanes = [new RhythmLane { Pulses = 1, CycleQuarters = 4, Pattern = [], Events = [new RhythmEvent { PositionQuarters = position }] }]
        };
        DocumentValidator.Validate(WithPosition(most));
        Assert.ThrowsExactly<ArgumentException>(() => DocumentValidator.Validate(WithPosition(tooMuch)));

        Assert.IsTrue(PositionEdits.TryParse("1e-100", English, out var tiny));
        Assert.AreEqual("PositionPrecise", PositionEdits.Problem(tiny, cycle, []), "an exact value, but longer than the project keeps");
        Assert.IsTrue(PositionEdits.TryParse("1e300", English, out var huge));
        Assert.AreEqual("PositionRange", PositionEdits.Problem(huge, cycle, []), "out of the cycle is said first");
    }

    [TestMethod]
    public void APositionIsShownShortAndReadsBackAsTheSameValue()
    {
        Assert.AreEqual("0", PositionEdits.Format(Rational.Zero, English));
        Assert.AreEqual("3", PositionEdits.Format(new Rational(3, 1), English));
        Assert.AreEqual("1.5", PositionEdits.Format(new Rational(3, 2), English));
        Assert.AreEqual("0.375", PositionEdits.Format(new Rational(3, 8), English));
        Assert.AreEqual("0,375", PositionEdits.Format(new Rational(3, 8), Dutch));
        Assert.AreEqual("-0.5", PositionEdits.Format(new Rational(-1, 2), English));
        Assert.AreEqual("0.2", PositionEdits.Format(new Rational(1, 5), English));
        Assert.AreEqual("1/3", PositionEdits.Format(new Rational(1, 3), English), "no finite decimal form");
        Assert.AreEqual("7/6", PositionEdits.Format(new Rational(7, 6), Dutch));
        Assert.AreEqual("1/1024", PositionEdits.Format(new Rational(1, 1024), English), "too many decimals to read at a glance");
        foreach (var culture in new[] { English, Dutch })
        foreach (var value in new[] { new Rational(0, 1), new Rational(5, 4), new Rational(1, 3), new Rational(63, 16), new Rational(1, 1024), new Rational(-7, 8) })
        {
            Assert.IsTrue(PositionEdits.TryParse(PositionEdits.Format(value, culture), culture, out var back), $"{value} in {culture.Name}");
            Assert.AreEqual(value, back, culture.Name);
        }
    }

    [TestMethod]
    public void ConvertingAPatternKeepsEveryClickWhereItWas()
    {
        var lane = new RhythmLane
        {
            Pulses = 3, CycleQuarters = 4, Pattern = [Accent.Strong, Accent.Silent, Accent.Soft]
        };
        var events = PositionEdits.FromPattern(lane);
        CollectionAssert.AreEqual(
            new[] { new Rational(0, 1), new Rational(4, 3), new Rational(8, 3) }, events.Select(item => item.PositionQuarters).ToArray());
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Silent, Accent.Soft }, events.Select(item => item.Accent).ToArray());

        lane.Events = events;
        lane.Pattern = [];
        DocumentValidator.Validate(new PulseDocument { Lanes = [lane] });
    }

    [TestMethod]
    public void ConvertingAnEmptyPatternGivesEveryClickTheDefaultStrength()
    {
        var events = PositionEdits.FromPattern(new RhythmLane { Pulses = 4, CycleQuarters = 4, Pattern = [] });
        Assert.AreEqual(4, events.Count);
        Assert.AreEqual(Accent.Strong, events[0].Accent);
        Assert.IsTrue(events.Skip(1).All(item => item.Accent == Accent.Normal));
    }

    [TestMethod]
    public void RemovingTheLastPositionLeavesOneSilentClickSoTheLaneStaysQuiet()
    {
        var lane = new RhythmLane { Pulses = 2, CycleQuarters = 4, Pattern = [] };
        lane.Events = [new RhythmEvent { PositionQuarters = Rational.Zero }, new RhythmEvent { PositionQuarters = new Rational(2, 1) }];
        PositionEdits.Remove(lane, 0);
        Assert.AreEqual(1, lane.Events.Count);
        Assert.AreEqual(new Rational(2, 1), lane.Events[0].PositionQuarters);
        DocumentValidator.Validate(new PulseDocument { Lanes = [lane] });

        PositionEdits.Remove(lane, 0);
        Assert.AreEqual(0, lane.Events.Count);
        Assert.AreEqual(1, lane.Pulses);
        CollectionAssert.AreEqual(new[] { Accent.Silent }, lane.Pattern);
        DocumentValidator.Validate(new PulseDocument { Lanes = [lane] });
        var plan = ScheduleCompiler.Compile(new PulseDocument { Lanes = [lane] }, 48000, 1, false);
        Assert.IsFalse(plan.Onsets.Any(onset => onset.LaneIndex == 0), "the lane makes no sound");
    }

    [TestMethod]
    public void PositionsAreKeptInTimeOrder()
    {
        var events = new List<RhythmEvent>
        {
            new() { PositionQuarters = new Rational(3, 1) }, new() { PositionQuarters = new Rational(1, 3) },
            new() { PositionQuarters = Rational.Zero }, new() { PositionQuarters = new Rational(5, 2) }
        };
        PositionEdits.Sort(events);
        CollectionAssert.AreEqual(
            new[] { Rational.Zero, new Rational(1, 3), new Rational(5, 2), new Rational(3, 1) }, events.Select(item => item.PositionQuarters).ToArray());
    }

    // ---- the sentence with its boxes ------------------------------------------------------------------------------------------

    [TestMethod]
    public void ASentenceIsCutAtItsPlacesInTheOrderTheLanguageUsesThem()
    {
        var english = SentenceTemplate.Split("Play {0} bars, then {1} bars silent");
        CollectionAssert.AreEqual(new[]
        {
            SentenceTemplate.Part.Words("Play "), SentenceTemplate.Part.Place(0), SentenceTemplate.Part.Words(" bars, then "),
            SentenceTemplate.Part.Place(1), SentenceTemplate.Part.Words(" bars silent")
        }, english.ToArray());

        var german = SentenceTemplate.Split("{0} Takte spielen, dann {1} Takte stumm");
        CollectionAssert.AreEqual(new[]
        {
            SentenceTemplate.Part.Place(0), SentenceTemplate.Part.Words(" Takte spielen, dann "), SentenceTemplate.Part.Place(1),
            SentenceTemplate.Part.Words(" Takte stumm")
        }, german.ToArray());

        var swapped = SentenceTemplate.Split("{1} silent after {0} played");
        CollectionAssert.AreEqual(new[]
        {
            SentenceTemplate.Part.Place(1), SentenceTemplate.Part.Words(" silent after "), SentenceTemplate.Part.Place(0),
            SentenceTemplate.Part.Words(" played")
        }, swapped.ToArray());
    }

    [TestMethod]
    public void OnlyANumberedPlaceInBracesIsAPlace()
    {
        CollectionAssert.AreEqual(new[] { SentenceTemplate.Part.Words("no places { here } {x} {12} {") },
            SentenceTemplate.Split("no places { here } {x} {12} {").ToArray());
        Assert.AreEqual(0, SentenceTemplate.Split("").Count);
        CollectionAssert.AreEqual(new[] { SentenceTemplate.Part.Place(0) }, SentenceTemplate.Split("{0}").ToArray());
        CollectionAssert.AreEqual(new[] { SentenceTemplate.Part.Place(0), SentenceTemplate.Part.Place(1) },
            SentenceTemplate.Split("{0}{1}").ToArray());
    }
}
