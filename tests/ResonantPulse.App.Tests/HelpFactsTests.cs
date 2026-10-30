using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

/// <summary>
/// What the sentences of the Help panel claim, checked against the engine and the rules of the window that they describe
/// (HelpContentTests checks the words). Every example is computed here: a number in the Help that stopped being true would fail
/// one of these before it reached a person.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HelpFactsTests
{
    private const int Rate = 48000;

    private static long[] Clicks(PulseDocument document, int bars, params int[] lanes)
    {
        var plan = ScheduleCompiler.Compile(document, Rate, bars, false);
        return plan.Onsets.Where(onset => lanes.Contains(onset.LaneIndex)).Select(onset => onset.Frame).ToArray();
    }

    private static string English(string id)
    {
        var previousLanguage = Text.Language;
        try
        {
            Text.Select("en");
            return HelpContent.Entries().Single(entry => entry.Id == id).Text;
        }
        finally { Text.Select(previousLanguage); }
    }

    // ---- BPM and the note value ------------------------------------------------------------------------------------------

    [TestMethod]
    public void In68EightyBpmWithDottedQuartersIsAsFastAs240BpmWithEighths()
    {
        var dotted = LiveTestKit.Document(tempo: 80, beats: 6, denominator: 8, unit: 1.5);
        var eighths = LiveTestKit.Document(tempo: 240, beats: 6, denominator: 8, unit: 0.5);
        var slow = Clicks(dotted, 2, -1);
        var fast = Clicks(eighths, 2, -1);
        // A dotted quarter lasts 0.75 s at 80 a minute, an eighth a third of that at 240 a minute, and they land together.
        CollectionAssert.AreEqual(new long[] { 0, 36000, 72000, 108000 }, slow);
        Assert.AreEqual(12, fast.Length);
        CollectionAssert.AreEqual(slow, fast.Where((_, index) => index % 3 == 0).ToArray());
        Assert.AreEqual(3 * (fast[1] - fast[0]), slow[1] - slow[0]);
    }

    [TestMethod]
    public void BpmCountsQuarterNotesWhenTheNoteIsAQuarter()
    {
        // 120 BPM with quarter notes is 120 quarter notes a minute: one every half second.
        CollectionAssert.AreEqual(new long[] { 0, 24000, 48000, 72000 }, Clicks(LiveTestKit.Document(), 1, -1));
    }

    // ---- the time signature and the click -------------------------------------------------------------------------------

    [TestMethod]
    public void TheBottomNumberNamesTheNoteOfOneBeat()
    {
        var settings = new PulseSettings();
        MeterPresets.Apply(settings, 3, 4);
        Assert.AreEqual(1d, settings.BeatUnitQuarters);
        MeterPresets.Apply(settings, 6, 8, [3, 3]);
        Assert.AreEqual(0.5, settings.BeatUnitQuarters);
    }

    [TestMethod]
    public void ChoosingSixEightClicksSixTimesABarAndADottedQuarterMakesItTwo()
    {
        var settings = new PulseSettings();
        MeterPresets.ApplyOneClickPerBeat(settings, 6, 8, [3, 3]);
        Assert.AreEqual(0.5, settings.BeatUnitQuarters);
        Assert.AreEqual(6, ClickGrid.Of(settings).Count);
        // What the Tempo panel does: the note value is a dotted quarter and the tempo is typed again.
        Assert.IsTrue(MeterPresets.UnitFitsBar(6, 8, 1.5), "a dotted quarter is offered in 6/8");
        settings.BeatUnitQuarters = 1.5;
        settings.Tempo = 80;
        Assert.AreEqual(2, ClickGrid.Of(settings).Count);
        // The picker offers only a note that the bar holds a whole number of times, which is why the example is in 6/8.
        Assert.IsFalse(MeterPresets.UnitFitsBar(4, 4, 1.5));
    }

    [TestMethod]
    public void TheClickCountsOfATimeSignatureHoldWhileSubdivisionIsOne()
    {
        // The Help's counts (six clicks in a 6/8 bar, two once the note value is a dotted quarter, one click for every written
        // beat) are for Subdivision 1. Changing the note value in the Tempo panel keeps a subdivision, and so does choosing a
        // time signature that has the same click unit as the one before.
        var settings = new PulseSettings();
        settings.BeatsPerBar = 6;
        settings.BeatDenominator = 8;
        settings.BeatUnitQuarters = 1.5;
        Assert.AreEqual(2, ClickGrid.Of(settings).Count, "Subdivision 1: two clicks a bar");
        settings.Subdivision = 3;
        Assert.AreEqual(6, ClickGrid.Of(settings).Count, "Subdivision 3: each of the two beats is split in three");

        var kept = new PulseSettings { Subdivision = 3 };
        MeterPresets.ApplyOneClickPerBeat(kept, 3, 4);
        Assert.AreEqual(3, kept.Subdivision, "4/4 to 3/4 keeps the click unit, so the split stays");
        Assert.AreEqual(9, ClickGrid.Of(kept).Count, "three beats of three clicks, not one click for every written beat");
        var ended = new PulseSettings { Subdivision = 3 };
        MeterPresets.ApplyOneClickPerBeat(ended, 6, 8, [3, 3]);
        Assert.AreEqual(1, ended.Subdivision, "a new click unit ends the split");
        Assert.AreEqual(6, ClickGrid.Of(ended).Count);
    }

    [TestMethod]
    public void TheQuickPicksIncludeTheTimeSignaturesTheHelpNames()
    {
        foreach (var (beats, denominator) in new[] { (3, 4), (6, 8), (4, 4) })
            Assert.IsTrue(MeterPresets.All.Any(preset => preset.Beats == beats && preset.Denominator == denominator), $"{beats}/{denominator}");
        // A new project is in 4/4 at 120 BPM with a note value of a quarter and one bar of count-in.
        var fresh = new PulseSettings();
        Assert.AreEqual((120d, 4, 4, 1d, 1), (fresh.Tempo, fresh.BeatsPerBar, fresh.BeatDenominator, fresh.BeatUnitQuarters, fresh.CountInBars));
    }

    [TestMethod]
    public void AFreshProjectCountsInOneBarBeforeTheFirstClick()
    {
        var plan = ScheduleCompiler.Compile(LiveTestKit.Document(), Rate, 1, true);
        Assert.AreEqual(4L * 24000, plan.CountInFrames, "one bar of 4/4 at 120 BPM");
        Assert.IsTrue(plan.Onsets.Min(onset => onset.Frame) < plan.CountInFrames, "the count-in is audible");
    }

    // ---- subdivision and swing ------------------------------------------------------------------------------------------

    [TestMethod]
    public void ASubdivisionOfThreeGivesThreeClicksInEveryBeat()
    {
        var document = LiveTestKit.Document(edit: settings => settings.Subdivision = 3);
        // A beat is 24000 frames at 120 BPM, so three clicks a beat are 8000 frames apart.
        CollectionAssert.AreEqual(Enumerable.Range(0, 12).Select(index => index * 8000L).ToArray(), Clicks(document, 1, -1, -2));
    }

    [TestMethod]
    public void AboutTwoThirdsSwingGivesALongShortPairAndHalfIsEven()
    {
        var even = LiveTestKit.Document(edit: settings => settings.Subdivision = 2);
        CollectionAssert.AreEqual(new long[] { 0, 12000, 24000, 36000, 48000, 60000, 72000, 84000 }, Clicks(even, 1, -1, -2));
        var swung = LiveTestKit.Document(edit: settings => { settings.Subdivision = 2; settings.Swing = 0.67; });
        var clicks = Clicks(swung, 1, -1, -2);
        // The second click of each pair is delayed: the pair is 0.67 of a beat long and then 0.33.
        Assert.AreEqual(0.67 * 24000, clicks[1], 1);
        Assert.AreEqual(24000, clicks[2]);
        Assert.AreEqual(2.0, (double)clicks[1] / (clicks[2] - clicks[1]), 0.05, "about two to one");
    }

    [TestMethod]
    public void SwingNeedsAnEvenSubdivision()
    {
        var odd = LiveTestKit.Document(edit: settings => { settings.Subdivision = 3; settings.Swing = 0.6; });
        Assert.ThrowsExactly<ArgumentException>(() => ScheduleCompiler.Compile(odd, Rate, 1, false));
        foreach (var even in new[] { 2, 4 })
        {
            var document = LiveTestKit.Document(edit: settings => { settings.Subdivision = even; settings.Swing = 0.6; });
            Assert.IsTrue(ScheduleCompiler.Compile(document, Rate, 1, false).Onsets.Count > 0, $"subdivision {even}");
        }
    }

    // ---- the strength of a beat -----------------------------------------------------------------------------------------

    [TestMethod]
    public void ATapStepsThroughTheFourStrengthsAndTheFirstBeatStartsStrong()
    {
        Assert.AreEqual(Accent.Strong, new PulseSettings().Accents[0]);
        CollectionAssert.AreEqual(new[] { Accent.Strong, Accent.Normal, Accent.Normal }, MeterPresets.DefaultAccents(3));
        var seen = new List<Accent> { Accent.Silent };
        for (var tap = 0; tap < 4; tap++) seen.Add(AccentPatterns.NextStrength(seen[^1], 1, wrap: true));
        CollectionAssert.AreEqual(new[] { Accent.Silent, Accent.Soft, Accent.Normal, Accent.Strong, Accent.Silent }, seen);
    }

    [TestMethod]
    public void ASilentClickMakesNoSoundButKeepsItsPlaceInTheCount()
    {
        var document = LiveTestKit.Document(edit: settings => settings.Accents = [Accent.Strong, Accent.Normal, Accent.Silent, Accent.Normal]);
        // Beat 3 is not played, and beat 4 still comes where it would have.
        CollectionAssert.AreEqual(new long[] { 0, 24000, 72000 }, Clicks(document, 1, -1));
    }

    [TestMethod]
    public void ClicksBetweenBeatsStartSoft()
    {
        var document = LiveTestKit.Document(edit: settings => settings.Subdivision = 2);
        var plan = ScheduleCompiler.Compile(document, Rate, 1, false);
        Assert.IsTrue(plan.Onsets.Where(onset => onset.LaneIndex == -2).All(onset => onset.Accent == Accent.Soft));
        Assert.AreEqual(4, plan.Onsets.Count(onset => onset.LaneIndex == -2));
    }

    // ---- tapping the tempo, and Tap to start ----------------------------------------------------------------------------

    [TestMethod]
    public void TappingTheTempoNeedsAtLeastTwoTaps()
    {
        var estimator = new TapTempoEstimator();
        Assert.IsNull(estimator.Tap(1.0), "one tap is not a tempo");
        Assert.AreEqual(120d, estimator.Tap(1.5)!.Value, 1e-9);
    }

    [TestMethod]
    public void TapToStartTakesTwoToSixteenTapsAndBeginsAgainAfterMoreThanFourSeconds()
    {
        // The number of taps is clamped to 2 to 16 wherever it is used.
        Assert.AreEqual(2, TapStartRules.ClampTaps(int.MinValue));
        Assert.AreEqual(16, TapStartRules.ClampTaps(int.MaxValue));
        Assert.AreEqual(TapStartBlock.Playing, TapStartRules.Block(transportIdle: false, busy: false, hasOutput: true), "only while stopped");
        var session = new TapInSession();
        session.Arm(2, "output");
        session.Tap(100);
        Assert.IsFalse(session.Tap(104.01), "a pause of more than four seconds begins the count again");
        Assert.AreEqual(1, session.ValidTaps);
        session.Arm(2, "output");
        session.Tap(100);
        Assert.IsTrue(session.Tap(104), "four seconds exactly is still one count");
        var idle = new Dictionary<string, string> { ["en"] = "four", ["nl"] = "vier", ["de"] = "vier", ["fr"] = "quatre", ["es"] = "cuatro" };
        foreach (var language in idle.Keys)
        {
            var previousLanguage = Text.Language;
            try
            {
                Text.Select(language);
                var text = HelpContent.Entries().Single(entry => entry.Id == "HelpTapStartLimits").Text;
                StringAssert.Contains(text, "2", language);
                StringAssert.Contains(text, "16", language);
                StringAssert.Contains(text, idle[language], language);
            }
            finally { Text.Select(previousLanguage); }
        }
        StringAssert.Contains(English("HelpTapStartLimits"), "2 to 16");
    }

    // ---- what plays at once and what waits for Stop, then Start --------------------------------------------------------

    [TestMethod]
    public void SoundAndStrengthChangeAtOnceAndTempoTimeSignatureNoteSubdivisionAndSwingFromTheNextBeat()
    {
        var active = LiveTestKit.Document();
        PulseSettings Changed(Action<PulseSettings> edit)
        {
            var requested = LiveTestKit.Requested(active);
            edit(requested);
            return requested;
        }
        // Sound and strength do not move the musical clock, so they are heard at the next audio buffer.
        Assert.AreEqual(LiveChange.Sound, LiveSettingsEdits.Classify(active.Settings, Changed(settings => settings.Sound = "Glass")));
        Assert.AreEqual(LiveChange.Sound, LiveSettingsEdits.Classify(active.Settings,
            Changed(settings => settings.Accents = [Accent.Soft, Accent.Normal, Accent.Normal, Accent.Normal])));
        // These move it, so they are joined in at the next beat.
        var structural = new Action<PulseSettings>[]
        {
            settings => settings.Tempo = 90, settings => settings.BeatsPerBar = 3, settings => settings.BeatDenominator = 8,
            settings => settings.BeatUnitQuarters = 0.5, settings => settings.Subdivision = 2,
            settings => { settings.Subdivision = 2; settings.Swing = 0.6; }
        };
        foreach (var edit in structural)
            Assert.AreEqual(LiveChange.Structural, LiveSettingsEdits.Classify(active.Settings, Changed(edit)));
        foreach (var kind in new[] { EditorChangeKind.SoundAndAccentsOnly, EditorChangeKind.GlobalTempoOnly, EditorChangeKind.PlayStructure })
            Assert.IsTrue(LiveEditPolicy.IsLive(kind), kind.ToString());
    }

    [TestMethod]
    public void TheCountInTheTimerLimitsAndEverythingInStudioWaitForStopThenStart()
    {
        var active = LiveTestKit.Document();
        var requested = LiveTestKit.Requested(active) with { CountInBars = 3, PracticeSeconds = 60, PracticeBeats = 10, PracticeBars = 2 };
        // A running metronome takes none of them from an edit: the settings it plays carry on as they were.
        Assert.AreEqual(LiveChange.None, LiveSettingsEdits.Classify(active.Settings, requested));
        var merged = LiveSettingsEdits.Apply(active, requested).Settings;
        Assert.AreEqual((1, 0, 0, 0), (merged.CountInBars, merged.PracticeSeconds, merged.PracticeBeats, merged.PracticeBars));
        // And the edit is remembered as one that is not heard yet, so the window says so.
        Assert.IsTrue(LiveEditPolicy.LeavesPendingEdit(EditorChangeKind.PlayStructure, true, true, LiveEditOutcome.StructuralQueued, countInChanged: true));
        Assert.IsFalse(LiveEditPolicy.IsLive(EditorChangeKind.PracticeTimerOnly));
        // Studio: a field of a lane or a step, and adding, removing or moving one.
        Assert.IsFalse(LiveEditPolicy.IsLive(EditorChangeKind.InPlaceField));
        Assert.IsFalse(LiveEditPolicy.IsLive(EditorChangeKind.Document));
    }

    // ---- a practice program ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void AProjectWithAProgramTakesItsTempoAndTimeSignatureFromTheProgram()
    {
        var document = LiveTestKit.Document(tempo: 120);
        document.Program.Add(new ProgramStep { StartTempo = 60, EndTempo = 60, Bars = 1, BeatsPerBar = 3, BeatDenominator = 4 });
        // 60 BPM in 3/4 (a click a second, three to a bar), not the 120 BPM in 4/4 of the settings.
        CollectionAssert.AreEqual(new long[] { 0, 48000, 96000 }, LiveTestKit.PrimaryAttacks(ScheduleCompiler.Compile(document, Rate, 1, false)));
    }
}
