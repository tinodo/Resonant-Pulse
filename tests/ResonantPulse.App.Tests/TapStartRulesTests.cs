using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TapStartRulesTests
{
    private static readonly string[] Languages = ["en", "nl", "de", "fr", "es"];

    [TestMethod]
    [DataRow(int.MinValue, 2)]
    [DataRow(-3, 2)]
    [DataRow(0, 2)]
    [DataRow(1, 2)]
    [DataRow(2, 2)]
    [DataRow(4, 4)]
    [DataRow(16, 16)]
    [DataRow(17, 16)]
    [DataRow(int.MaxValue, 16)]
    public void ANumberOfTapsFromTheSettingsFileIsBroughtIntoRange(int stored, int expected) =>
        Assert.AreEqual(expected, TapStartRules.ClampTaps(stored));

    [TestMethod]
    public void TheDefaultIsOneBarOfFourAndTheRangeIsTwoToSixteen()
    {
        Assert.AreEqual(4, new TapInSession().RequiredTaps);
        Assert.AreEqual(4, new Storage.Preferences().TapInCount);
        Assert.AreEqual(2, TapStartRules.ClampTaps(int.MinValue));
        Assert.AreEqual(16, TapStartRules.ClampTaps(int.MaxValue));
        var session = new TapInSession();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => session.Arm(TapStartRules.ClampTaps(int.MinValue) - 1, "output"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => session.Arm(TapStartRules.ClampTaps(int.MaxValue) + 1, "output"));
        session.Arm(TapStartRules.ClampTaps(int.MinValue), "output");
        session.Arm(TapStartRules.ClampTaps(int.MaxValue), "output");
    }

    [TestMethod]
    public void TheSwitchCanGoOnOnlyWhileNothingPlaysAndAnOutputIsChosen()
    {
        Assert.AreEqual(TapStartBlock.None, TapStartRules.Block(transportIdle: true, busy: false, hasOutput: true));
        Assert.AreEqual(TapStartBlock.Playing, TapStartRules.Block(transportIdle: false, busy: false, hasOutput: true));
        Assert.AreEqual(TapStartBlock.Busy, TapStartRules.Block(transportIdle: true, busy: true, hasOutput: true));
        Assert.AreEqual(TapStartBlock.NoOutput, TapStartRules.Block(transportIdle: true, busy: false, hasOutput: false));
    }

    [TestMethod]
    public void WhenSeveralThingsPreventItTheFirstOneThePersonCanActOnIsNamed()
    {
        // Stop the metronome first, then wait for what is running, then choose an output.
        Assert.AreEqual(TapStartBlock.Playing, TapStartRules.Block(transportIdle: false, busy: true, hasOutput: false));
        Assert.AreEqual(TapStartBlock.Busy, TapStartRules.Block(transportIdle: true, busy: true, hasOutput: false));
    }

    [TestMethod]
    public void WithAProgramTheTapButtonIsOnlyAvailableWhileTapStartListens()
    {
        // A program decides the tempo, so a plain tap would change nothing; only tapping to start (which scales the program) is meaningful.
        Assert.IsTrue(TapStartRules.TapEnabled(followsProgram: false, listening: false));
        Assert.IsTrue(TapStartRules.TapEnabled(followsProgram: false, listening: true));
        Assert.IsFalse(TapStartRules.TapEnabled(followsProgram: true, listening: false));
        Assert.IsTrue(TapStartRules.TapEnabled(followsProgram: true, listening: true));
    }

    [TestMethod]
    public void ThePhaseFollowsTheSession()
    {
        var session = new TapInSession();
        Assert.AreEqual(TapStartPhase.Off, TapStartRules.PhaseOf(session));
        session.Arm(3, "output");
        Assert.AreEqual(TapStartPhase.Counting, TapStartRules.PhaseOf(session));
        session.Tap(0); session.Tap(0.5);
        Assert.AreEqual(TapStartPhase.Counting, TapStartRules.PhaseOf(session));
        session.Tap(0.5);
        Assert.AreEqual(TapStartPhase.Uneven, TapStartRules.PhaseOf(session), "a tap that is not after the last one is uneven");
        session.Tap(1);
        Assert.AreEqual(TapStartPhase.Counting, TapStartRules.PhaseOf(session), "the next good tap clears it");
        session.Tap(1.5); session.Tap(2);
        Assert.IsTrue(session.IsPending);
        Assert.AreEqual(TapStartPhase.Starting, TapStartRules.PhaseOf(session));
        session.Cancel();
        Assert.AreEqual(TapStartPhase.Off, TapStartRules.PhaseOf(session));
    }

    [TestMethod]
    public void EveryPhaseHasAMachineReadableStatusForAssistiveTechnologyAndTheVerifier()
    {
        Assert.AreEqual("idle", TapStartRules.Status(TapStartPhase.Off, 0, 4));
        Assert.AreEqual("armed:0:4:0", TapStartRules.Status(TapStartPhase.Counting, 0, 4));
        Assert.AreEqual("armed:2:4:0", TapStartRules.Status(TapStartPhase.Counting, 2, 4));
        Assert.AreEqual("armed:0:4:1", TapStartRules.Status(TapStartPhase.Uneven, 0, 4));
        Assert.AreEqual("preparing", TapStartRules.Status(TapStartPhase.Starting, 4, 4));
    }

    [TestMethod]
    public void EveryPhaseButOffHasACaptionWithItsNumbers()
    {
        Assert.IsNull(TapStartRules.Caption(TapStartPhase.Off, 0, 4));
        var counting = TapStartRules.Caption(TapStartPhase.Counting, 2, 4)!.Value;
        Assert.AreEqual("TapStartCount", counting.Key);
        CollectionAssert.AreEqual(new object[] { 2, 4 }, counting.Args);
        var uneven = TapStartRules.Caption(TapStartPhase.Uneven, 0, 4)!.Value;
        Assert.AreEqual("TapStartRejected", uneven.Key);
        var starting = TapStartRules.Caption(TapStartPhase.Starting, 4, 4)!.Value;
        Assert.AreEqual("TapStartStarting", starting.Key);
    }

    [TestMethod]
    public void WhatIsSpokenAfterATapNamesTheCountAndOnlyATempoThatTheRunHas()
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in Languages)
            {
                Text.Select(language);
                var session = new TapInSession();
                session.Arm(4, "output");
                session.Tap(0);
                var first = TapStartRules.Announcement(session);
                StringAssert.Contains(first, "1", language);
                Assert.IsFalse(first.Contains("BPM"), $"{language}: one tap makes no tempo: {first}");
                session.Tap(0.5);
                var second = TapStartRules.Announcement(session);
                StringAssert.Contains(second, "120", language);
                StringAssert.Contains(second, "BPM", language);
                // An uneven tap ends the run, so the estimate that is left over is not a tempo the person is about to start at: it is
                // not spoken after "Tap again", nor after the first tap of the new run (the dial does not show it either).
                session.Tap(3.5);
                var uneven = TapStartRules.Announcement(session);
                Assert.IsFalse(uneven.Contains("120") || uneven.Contains("BPM"), $"{language}: {uneven}");
                Assert.AreEqual(Text.Get("TapStartRejected"), uneven, language);
                session.Tap(4);
                var restarted = TapStartRules.Announcement(session);
                Assert.IsFalse(restarted.Contains("120") || restarted.Contains("BPM"), $"{language}: {restarted}");
                session.Tap(4.5);
                StringAssert.Contains(TapStartRules.Announcement(session), "BPM", language);
            }
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [TestMethod]
    public void TheHelpOfTheSwitchSaysWhyItIsOffAndWhatToDoWhileItIsOn()
    {
        Assert.AreEqual("TapStartHelp", TapStartRules.HelpKey(TapStartPhase.Off, TapStartBlock.None, followsProgram: false));
        Assert.AreEqual("TapStartProgramHelp", TapStartRules.HelpKey(TapStartPhase.Off, TapStartBlock.None, followsProgram: true));
        Assert.AreEqual("TapStartStopFirst", TapStartRules.HelpKey(TapStartPhase.Off, TapStartBlock.Playing, followsProgram: false));
        Assert.AreEqual("ChooseOutput", TapStartRules.HelpKey(TapStartPhase.Off, TapStartBlock.NoOutput, followsProgram: false));
        Assert.AreEqual("TapStartHelp", TapStartRules.HelpKey(TapStartPhase.Off, TapStartBlock.Busy, followsProgram: false));
        foreach (var phase in new[] { TapStartPhase.Counting, TapStartPhase.Uneven, TapStartPhase.Starting })
        foreach (var block in Enum.GetValues<TapStartBlock>())
            Assert.AreEqual("TapStartActive", TapStartRules.HelpKey(phase, block, followsProgram: true), $"{phase}/{block}");
    }

    [TestMethod]
    public void EveryCaptionAndHelpTextIsDefinedAndFillsItsPlaceholdersInEveryLanguage()
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in Languages)
            {
                Text.Select(language);
                foreach (var phase in new[] { TapStartPhase.Counting, TapStartPhase.Uneven, TapStartPhase.Starting })
                {
                    // The widest the numbers get: 12 of 16 taps.
                    var (key, args) = TapStartRules.Caption(phase, 12, 16)!.Value;
                    var caption = Text.Format(key, args);
                    Assert.AreNotEqual(key, caption, $"{language}/{key}: not defined");
                    Assert.IsFalse(caption.Contains('{') || caption.Contains('}'), $"{language}/{key}: {caption}");
                    // The caption sits in a pill that is 132 DIP wide at its narrowest, with 16 DIP of padding each side: the longest
                    // that stays inside, and so keeps the row from changing width while taps come in, is about 16 characters.
                    Assert.IsTrue(caption.Length <= 16, $"{language}/{key}: {caption.Length} characters: {caption}");
                }
                var counting = Text.Format("TapStartCount", 3, 16);
                StringAssert.Contains(counting, "3", language);
                StringAssert.Contains(counting, "16", language);
                foreach (var phase in Enum.GetValues<TapStartPhase>())
                foreach (var block in Enum.GetValues<TapStartBlock>())
                foreach (var program in new[] { false, true })
                {
                    var key = TapStartRules.HelpKey(phase, block, program);
                    var help = Text.Format(key, 4);
                    Assert.AreNotEqual(key, help, $"{language}/{key}: not defined");
                    Assert.IsFalse(help.Contains('{') || help.Contains('}'), $"{language}/{key}: {help}");
                }
                StringAssert.Contains(Text.Format("TapStartHelp", 7), "7", language);
                StringAssert.Contains(Text.Format("TapStartProgramHelp", 7), "7", language);
                StringAssert.Contains(Text.Format("TapStartScaled", "96.4"), "96.4", language);
            }
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
