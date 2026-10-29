using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;
using ResonantPulse.Core;
using static ResonantPulse.App.Tests.LiveTestKit;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class LiveSettingsEditsTests
{
    [TestMethod]
    public void ApplyTakesEveryLiveSettingFromTheRequestAndKeepsTheRestOfTheActiveDocument()
    {
        var active = new PulseDocument
        {
            Name = "Active", Seed = 9, Lanes = [new RhythmLane { Name = "Clave" }],
            Settings = new() { CountInBars = 3, PracticeSeconds = 7, PracticeBeats = 5, PracticeBars = 2, MasterGain = 0.2f }
        };
        var requested = new PulseSettings
        {
            Tempo = 93, BeatsPerBar = 6, BeatDenominator = 8, BeatUnitQuarters = 1.5, Subdivision = 4, Swing = 0.6,
            Groups = [3, 3], Accents = [Accent.Soft, Accent.Strong], AccentOverridesGroups = true,
            ManualAccentIndices = [1], Sound = "Glass", AudibleBars = 2, SilentBars = 1, BlindGaps = false,
            CountInBars = 0, PracticeSeconds = 99, PracticeBeats = 99, PracticeBars = 99, MasterGain = 1
        };
        var merged = LiveSettingsEdits.Apply(active, requested);
        var settings = merged.Settings;
        Assert.AreEqual(93, settings.Tempo);
        Assert.AreEqual(6, settings.BeatsPerBar);
        Assert.AreEqual(8, settings.BeatDenominator);
        Assert.AreEqual(1.5, settings.BeatUnitQuarters);
        Assert.AreEqual(4, settings.Subdivision);
        Assert.AreEqual(0.6, settings.Swing);
        CollectionAssert.AreEqual(new[] { 3, 3 }, settings.Groups);
        CollectionAssert.AreEqual(new[] { Accent.Soft, Accent.Strong }, settings.Accents);
        Assert.IsTrue(settings.AccentOverridesGroups);
        CollectionAssert.AreEqual(new[] { 1 }, settings.ManualAccentIndices);
        Assert.AreEqual("Glass", settings.Sound);
        Assert.AreEqual(2, settings.AudibleBars);
        Assert.AreEqual(1, settings.SilentBars);
        Assert.IsFalse(settings.BlindGaps);
        Assert.AreEqual(3, settings.CountInBars);
        Assert.AreEqual(7, settings.PracticeSeconds);
        Assert.AreEqual(5, settings.PracticeBeats);
        Assert.AreEqual(2, settings.PracticeBars);
        Assert.AreEqual(0.2f, settings.MasterGain);
        Assert.AreEqual("Active", merged.Name);
        Assert.AreEqual(9, merged.Seed);
        Assert.AreEqual("Clave", merged.Lanes.Single().Name);
        Assert.AreEqual(120, active.Settings.Tempo, "The active document is never modified.");
    }

    [TestMethod]
    public void ApplyCopiesArraysSoLaterEditorChangesCannotReachTheMergedDocument()
    {
        var requested = new PulseSettings { Groups = [2, 2], Accents = [Accent.Strong], ManualAccentIndices = [0] };
        var merged = LiveSettingsEdits.Apply(new PulseDocument(), requested);
        requested.Groups[0] = 3;
        requested.Accents[0] = Accent.Silent;
        requested.ManualAccentIndices![0] = 5;
        CollectionAssert.AreEqual(new[] { 2, 2 }, merged.Settings.Groups);
        CollectionAssert.AreEqual(new[] { Accent.Strong }, merged.Settings.Accents);
        CollectionAssert.AreEqual(new[] { 0 }, merged.Settings.ManualAccentIndices);
    }

    [TestMethod]
    public void ClassifySeparatesSameClockSoundEditsFromStructuralEdits()
    {
        var baseline = Document().Settings;
        Assert.AreEqual(LiveChange.None, LiveSettingsEdits.Classify(baseline, baseline with { }));
        Assert.AreEqual(LiveChange.None, LiveSettingsEdits.Classify(baseline,
            baseline with { MasterGain = 0.1f, CountInBars = 4, PracticeSeconds = 9 }), "Non-live settings are ignored.");
        foreach (var sound in new Func<PulseSettings, PulseSettings>[]
        {
            s => s with { Sound = "Glass" },
            s => s with { Accents = [Accent.Strong, Accent.Soft] },
            s => s with { AccentOverridesGroups = true },
            s => s with { ManualAccentIndices = [2] },
            s => s with { BlindGaps = false }
        })
            Assert.AreEqual(LiveChange.Sound, LiveSettingsEdits.Classify(baseline, sound(baseline)));
        foreach (var structural in new Func<PulseSettings, PulseSettings>[]
        {
            s => s with { Tempo = 121 }, s => s with { BeatsPerBar = 3 }, s => s with { BeatDenominator = 8 },
            s => s with { BeatUnitQuarters = 0.5 }, s => s with { Subdivision = 2 }, s => s with { Swing = 0.6 },
            s => s with { Groups = [2, 2] }, s => s with { AudibleBars = 1, SilentBars = 1 },
            s => s with { Sound = "Glass", Tempo = 90 }
        })
            Assert.AreEqual(LiveChange.Structural, LiveSettingsEdits.Classify(baseline, structural(baseline)));
    }

    [TestMethod]
    public void BlindGapsOnlyDecidesWhatTheDisplayShowsSoItFollowsLiveEvenDuringAProgram()
    {
        var open = LiveSettingsEdits.Decide(Revision(Document(), 8000), null, Requested(Document(edit: s => s.BlindGaps = false)));
        Assert.AreEqual(LiveEditOutcome.SoundApplied, open.Outcome);
        Assert.IsFalse(open.Merged.Settings.BlindGaps);

        var program = Document();
        program.Program = [new ProgramStep { Bars = 2, StartTempo = 100, EndTempo = 100 }];
        var finite = LiveSettingsEdits.Decide(Revision(program, 8000), null, Requested(Document(edit: s => s.BlindGaps = false)));
        Assert.AreEqual(LiveEditOutcome.SoundApplied, finite.Outcome, "A display flag never needs a restart.");
        Assert.IsTrue(finite.Publish);
        Assert.IsFalse(finite.Merged.Settings.BlindGaps);
    }

    [TestMethod]
    public void SplitClickStrengthsFollowLiveLikeBeatStrengthsEvenDuringAProgramAndAreCopied()
    {
        var baseline = Document(edit: s => s.Subdivision = 3).Settings;
        Assert.AreEqual(LiveChange.Sound, LiveSettingsEdits.Classify(baseline, baseline with { SubdivisionAccents = [Accent.Strong] }));
        Assert.AreEqual(LiveChange.None, LiveSettingsEdits.Classify(baseline, baseline with { SubdivisionAccents = [] }),
            "null and empty are the same setting: every split click is Soft");

        var program = Document(edit: s => s.Subdivision = 3);
        program.Program = [new ProgramStep { Bars = 2, StartTempo = 100, EndTempo = 100 }];
        var decision = LiveSettingsEdits.Decide(Revision(program, 8000), null,
            Requested(Document(edit: s => { s.Subdivision = 3; s.SubdivisionAccents = [Accent.Normal, Accent.Silent]; })));
        Assert.AreEqual(LiveEditOutcome.SoundApplied, decision.Outcome, "a strength never needs a restart");
        Assert.IsTrue(decision.Publish);
        CollectionAssert.AreEqual(new[] { Accent.Normal, Accent.Silent }, decision.Merged.Settings.SubdivisionAccents);

        var requested = new PulseSettings { SubdivisionAccents = [Accent.Strong] };
        var merged = LiveSettingsEdits.Apply(new PulseDocument(), requested);
        requested.SubdivisionAccents[0] = Accent.Silent;
        CollectionAssert.AreEqual(new[] { Accent.Strong }, merged.Settings.SubdivisionAccents, "the arrays are copied");
        var soundOnly = LiveSoundEdits.Apply(new PulseDocument(), new PulseSettings { SubdivisionAccents = [Accent.Soft] });
        CollectionAssert.AreEqual(new[] { Accent.Soft }, soundOnly.Settings.SubdivisionAccents);
    }

    [TestMethod]
    public void NullAndEmptyManualAccentListsAreTheSameAudibleSetting()
    {
        var plain = Document().Settings;
        Assert.AreEqual(LiveChange.None, LiveSettingsEdits.Classify(plain, plain with { ManualAccentIndices = [] }));
        Assert.AreEqual(LiveChange.Sound, LiveSettingsEdits.Classify(plain, plain with { ManualAccentIndices = [0] }));
    }

    [TestMethod]
    public void DecideReportsTheOutcomeAndPublishesOnlyWhatTheTransportIsNotAlreadyConvergingTo()
    {
        var running = Revision(Document(), 8000);
        var faster = Document(tempo: 130);
        var fasterGlass = Document(tempo: 130, edit: s => s.Sound = "Glass");

        var unchanged = LiveSettingsEdits.Decide(running, null, Requested(Document()));
        Assert.AreEqual(LiveEditOutcome.NoChange, unchanged.Outcome);
        Assert.IsFalse(unchanged.Publish);

        var sound = LiveSettingsEdits.Decide(running, null, Requested(Document(edit: s => s.Sound = "Click")));
        Assert.AreEqual(LiveEditOutcome.SoundApplied, sound.Outcome);
        Assert.IsTrue(sound.Publish);
        Assert.AreEqual("Click", sound.Merged.Settings.Sound);

        var structural = LiveSettingsEdits.Decide(running, null, Requested(faster));
        Assert.AreEqual(LiveEditOutcome.StructuralQueued, structural.Outcome);
        Assert.IsTrue(structural.Publish);
        Assert.AreEqual(130, structural.Merged.Settings.Tempo);

        var repeated = LiveSettingsEdits.Decide(running, structural.Merged, Requested(faster));
        Assert.AreEqual(LiveEditOutcome.StructuralQueued, repeated.Outcome, "Still pending relative to the active revision.");
        Assert.IsFalse(repeated.Publish, "The identical request is already queued.");

        // A later sound edit carries the queued structural settings along: nothing is lost.
        var laterSound = LiveSettingsEdits.Decide(running, structural.Merged, Requested(fasterGlass));
        Assert.AreEqual(LiveEditOutcome.StructuralQueued, laterSound.Outcome);
        Assert.IsTrue(laterSound.Publish);
        Assert.AreEqual(130, laterSound.Merged.Settings.Tempo);
        Assert.AreEqual("Glass", laterSound.Merged.Settings.Sound);

        // Going back to what plays is still published so the callback can supersede the queued change.
        var revert = LiveSettingsEdits.Decide(running, structural.Merged, Requested(Document()));
        Assert.AreEqual(LiveEditOutcome.NoChange, revert.Outcome);
        Assert.IsTrue(revert.Publish);
    }

    [TestMethod]
    public void FiniteProgramsFollowSoundEditsLiveExactlyLikeTheFormerSoundOnlyUpdate()
    {
        var program = Document(120, edit: s => s.PracticeSeconds = 30);
        program.Program = [new ProgramStep { Bars = 2, StartTempo = 100, EndTempo = 100 }];
        var finite = Revision(program, 8000);

        var sound = LiveSettingsEdits.Decide(finite, null, Requested(Document(120, edit: s => { s.Sound = "Glass"; s.Accents = [Accent.Soft]; })));
        Assert.AreEqual(LiveEditOutcome.SoundApplied, sound.Outcome);
        Assert.IsTrue(sound.Publish);
        Assert.AreEqual("Glass", sound.Merged.Settings.Sound);
        CollectionAssert.AreEqual(new[] { Accent.Soft }, sound.Merged.Settings.Accents);
        Assert.AreEqual(1, sound.Merged.Program.Count, "The program itself is never replaced.");
        Assert.AreEqual(30, sound.Merged.Settings.PracticeSeconds);

        var unchanged = LiveSettingsEdits.Decide(finite, null, Requested(Document(120)));
        Assert.AreEqual(LiveEditOutcome.NoChange, unchanged.Outcome);
        Assert.IsFalse(unchanged.Publish);

        var repeated = LiveSettingsEdits.Decide(finite, sound.Merged, Requested(Document(120, edit: s => { s.Sound = "Glass"; s.Accents = [Accent.Soft]; })));
        Assert.AreEqual(LiveEditOutcome.SoundApplied, repeated.Outcome, "Still pending relative to the active revision.");
        Assert.IsFalse(repeated.Publish, "The identical sound edit is already queued.");

        var revert = LiveSettingsEdits.Decide(finite, sound.Merged, Requested(Document(120)));
        Assert.AreEqual(LiveEditOutcome.NoChange, revert.Outcome);
        Assert.IsTrue(revert.Publish, "Going back to what plays supersedes the queued sound edit.");
    }

    [TestMethod]
    public void StructuralChangesOfAFiniteProgramRequireARestartButNeverBlockTheSoundPartOfTheRequest()
    {
        var program = Document();
        program.Program = [new ProgramStep { Bars = 2, StartTempo = 100, EndTempo = 100 }];
        var finite = Revision(program, 8000);

        var structural = LiveSettingsEdits.Decide(finite, null, Requested(Document(tempo: 90)));
        Assert.AreEqual(LiveEditOutcome.RequiresRestart, structural.Outcome);
        Assert.IsFalse(structural.Publish, "Nothing about the sound changed, so nothing is compiled or queued.");
        Assert.AreEqual(120, structural.Merged.Settings.Tempo, "Structural settings never reach a finite program.");

        foreach (var edit in new Action<PulseSettings>[]
        {
            s => s.BeatsPerBar = 3, s => s.BeatDenominator = 8, s => s.BeatUnitQuarters = 0.5, s => s.Subdivision = 2,
            s => s.Swing = 0.6, s => s.Groups = [2, 2], s => { s.AudibleBars = 1; s.SilentBars = 1; }
        })
            Assert.AreEqual(LiveEditOutcome.RequiresRestart,
                LiveSettingsEdits.Decide(finite, null, Requested(Document(edit: edit))).Outcome);

        // A pending structural edit plus a new sound edit: the sound edit is still published and heard.
        var mixed = LiveSettingsEdits.Decide(finite, null, Requested(Document(tempo: 90, beats: 3, edit: s => s.Sound = "Click")));
        Assert.AreEqual(LiveEditOutcome.RequiresRestart, mixed.Outcome, "Something in the request still needs a restart.");
        Assert.IsTrue(mixed.Publish);
        Assert.AreEqual("Click", mixed.Merged.Settings.Sound);
        Assert.AreEqual(120, mixed.Merged.Settings.Tempo);
        Assert.AreEqual(4, mixed.Merged.Settings.BeatsPerBar);
        Assert.AreEqual(LiveChange.Sound, LiveSettingsEdits.Classify(finite.Document.Settings, mixed.Merged.Settings));
        var again = LiveSettingsEdits.Decide(finite, mixed.Merged, Requested(Document(tempo: 90, beats: 3, edit: s => s.Sound = "Click")));
        Assert.AreEqual(LiveEditOutcome.RequiresRestart, again.Outcome);
        Assert.IsFalse(again.Publish);
    }

    [TestMethod]
    public void InvalidMergedDocumentsSurfaceTheValidationErrorWhenCompiled()
    {
        var running = Revision(Document(), 8000);
        var invalid = Document(edit: s => { s.BeatsPerBar = 3; s.Groups = [2, 2]; });
        var decision = LiveSettingsEdits.Decide(running, null, Requested(invalid));
        Assert.IsTrue(decision.Publish);
        Assert.ThrowsExactly<ArgumentException>(() => Revision(decision.Merged, 8000));
    }
}
