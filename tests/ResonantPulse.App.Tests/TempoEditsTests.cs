using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class TempoEditsTests
{
    [TestMethod]
    public async Task RapidReturnUsesUiEventPairWhilePublishedDocumentIsStillDelayed()
    {
        var publishedTempo = 120d;
        var commits = new List<double>();
        var validatingFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishValidation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var edits = new SemaphoreSlim(1);

        async Task QueueValueChangedAsync(double oldUiValue, double newUiValue)
        {
            // Same actual guard called by NumberBox.ValueChanged. The published
            // document intentionally remains at 120 while the first edit waits.
            if (!TempoEdits.IsChange(oldUiValue, newUiValue)) return;
            await edits.WaitAsync();
            try
            {
                if (newUiValue == 132)
                {
                    validatingFirst.SetResult(true);
                    await finishValidation.Task;
                }
                publishedTempo = newUiValue;
                commits.Add(newUiValue);
            }
            finally { edits.Release(); }
        }

        var outward = QueueValueChangedAsync(120, 132);
        await validatingFirst.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var returning = QueueValueChangedAsync(132, 120);
        var valueBeforeValidationCompletes = publishedTempo;
        finishValidation.SetResult(true);
        await Task.WhenAll(outward, returning).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(120d, valueBeforeValidationCompletes);
        CollectionAssert.AreEqual(new[] { 132d, 120d }, commits.ToArray());
        Assert.AreEqual(120d, publishedTempo);
    }

    [TestMethod]
    public void ChangedTapProducesOneNumberBoxValueChange()
    {
        var value = 120d;
        var valueChanges = 0;
        foreach (var estimate in new double?[] { 132, 132, 132 })
        {
            if (!TempoEdits.TryApplyTap(value, estimate, out var proposed)) continue;
            value = proposed;
            valueChanges++;
        }
        Assert.AreEqual(132d, value);
        Assert.AreEqual(1, valueChanges);
        Assert.IsTrue(TempoEdits.IsChange(120, 132));
        Assert.IsFalse(TempoEdits.IsChange(132, 132));
    }

    [TestMethod]
    public void UnchangedAndClampedEstimatesDoNotRequestAnotherCommit()
    {
        Assert.IsFalse(TempoEdits.TryApplyTap(120, 120, out _));
        Assert.IsFalse(TempoEdits.TryApplyTap(1000, 1400, out _));
        Assert.IsFalse(TempoEdits.TryApplyTap(1, 0.5, out _));
        Assert.IsTrue(TempoEdits.TryApplyTap(120, 1400, out var maximum));
        Assert.AreEqual(1000d, maximum);
    }

    [TestMethod]
    public void MissingAndNonfiniteEstimatesCannotEditTempo()
    {
        foreach (var estimate in new double?[] { null, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.IsFalse(TempoEdits.TryApplyTap(120, estimate, out _));
        Assert.IsFalse(TempoEdits.IsChange(120, double.NaN));
    }

    [TestMethod]
    [DataRow(29.0, 1000.0)]
    [DataRow(58.0, 1.0)]
    [DataRow(58.0, 2.0)]
    public void ExactScalePreservesInclusiveBoundsWithoutRatioRoundoff(double original, double tempo)
    {
        var endpoint = tempo == 1 ? original : original / 2;
        var document = new PulseDocument { Program = [
            new() { StartTempo = original, EndTempo = original },
            new() { StartTempo = endpoint, EndTempo = endpoint }
        ] };
        TempoEdits.ApplyTapIn(document, tempo);
        Assert.AreEqual(tempo, document.Program[0].StartTempo);
        Assert.AreEqual(tempo, document.Program[0].EndTempo);
        Assert.AreEqual(tempo == 1 ? 1d : tempo / 2, document.Program[1].EndTempo);
    }

    [TestMethod]
    public void ExactScaleRejectsEvenOneUlpOutsideEitherBoundary()
    {
        foreach (var (tempo, original, endpoint) in new[] {
            (1000d, 29d, Math.BitIncrement(29d)),
            (1d, 58d, Math.BitDecrement(58d))
        })
        {
            var document = new PulseDocument { Program = [
                new() { StartTempo = original, EndTempo = original },
                new() { StartTempo = endpoint, EndTempo = endpoint }
            ] };
            Assert.ThrowsExactly<UserMessageException>(() => TempoEdits.ApplyTapIn(document, tempo));
            Assert.AreEqual(original, document.Program[0].StartTempo);
            Assert.AreEqual(endpoint, document.Program[1].EndTempo);
            Assert.AreEqual(120d, document.Settings.Tempo);
        }
    }

    [TestMethod]
    [DataRow(0.5)]
    [DataRow(1.0)]
    [DataRow(1.5)]
    [DataRow(3.0)]
    public void TapInUsesSelectedUnitAndScalesWholeProgramWithoutRewritingRhythm(double unit)
    {
        var document = new PulseDocument
        {
            Settings = new() { BeatUnitQuarters = unit, CountInBars = 2, PracticeBeats = 12 },
            Program = [
                new() { StartTempo = 80, EndTempo = 120 },
                new() { StartTempo = 120, EndTempo = 160 }
            ]
        };
        TempoEdits.ApplyTapIn(document, 120);
        Assert.AreEqual(120d, document.Settings.Tempo);
        Assert.AreEqual(unit, document.Settings.BeatUnitQuarters);
        Assert.AreEqual(2, document.Settings.CountInBars);
        Assert.AreEqual(12, document.Settings.PracticeBeats);
        Assert.AreEqual(120d, document.Program[0].StartTempo);
        Assert.AreEqual(180d, document.Program[0].EndTempo);
        Assert.AreEqual(180d, document.Program[1].StartTempo);
        Assert.AreEqual(240d, document.Program[1].EndTempo);
    }

    [TestMethod]
    [DataRow(1000.0, 100.0, 200.0)]
    [DataRow(1.0, 100.0, 50.0)]
    [DataRow(double.NaN, 100.0, 100.0)]
    [DataRow(double.PositiveInfinity, 100.0, 100.0)]
    [DataRow(0.5, 100.0, 100.0)]
    public void InvalidTapInScaleRejectsBeforeAnyDocumentMutation(double tempo, double start, double end)
    {
        var document = new PulseDocument { Program = [new() { StartTempo = start, EndTempo = end }] };
        Assert.ThrowsExactly<UserMessageException>(() => TempoEdits.ApplyTapIn(document, tempo));
        Assert.AreEqual(120d, document.Settings.Tempo);
        Assert.AreEqual(start, document.Program[0].StartTempo);
        Assert.AreEqual(end, document.Program[0].EndTempo);
    }

    [TestMethod]
    public void AProjectWithoutAProgramOnlyGetsTheTappedTempo()
    {
        var document = new PulseDocument
        {
            Name = "Groove",
            Settings = new() { BeatsPerBar = 6, BeatDenominator = 8, BeatUnitQuarters = 1.5, Subdivision = 3, Swing = 0.6, CountInBars = 2, Sound = "Glass" },
            Lanes = [new() { Name = "Lane 1" }]
        };
        var before = document.Settings with { };
        TempoEdits.ApplyTapIn(document, 96.4);
        Assert.AreEqual(before with { Tempo = 96.4 }, document.Settings);
        Assert.AreEqual(1, document.Lanes.Count);
        Assert.AreEqual("Groove", document.Name);
        Assert.AreEqual(0, document.Program.Count);
    }

    [TestMethod]
    public void EveryStepKeepsItsOwnMeterRampRepeatsAndSilentBars()
    {
        var document = new PulseDocument
        {
            Program = [
                new() { Name = "Warm up", StartTempo = 60, EndTempo = 90, Bars = 6, Repeats = 2, Ramp = "LinearTime", BeatsPerBar = 3, BeatDenominator = 4 },
                new() { Name = "Hold", StartTempo = 90, EndTempo = 90, Bars = 4, AudibleBars = 2, SilentBars = 1, BeatsPerBar = 6, BeatDenominator = 8 }
            ]
        };
        TempoEdits.ApplyTapIn(document, 120);
        var first = document.Program[0];
        Assert.AreEqual((120d, 180d), (first.StartTempo, first.EndTempo));
        Assert.AreEqual(("Warm up", 6, 2, "LinearTime", 3, 4), (first.Name, first.Bars, first.Repeats, first.Ramp, first.BeatsPerBar, first.BeatDenominator));
        var second = document.Program[1];
        Assert.AreEqual((180d, 180d), (second.StartTempo, second.EndTempo));
        Assert.AreEqual(("Hold", 4, 1, "Constant", 2, 1, 6, 8),
            (second.Name, second.Bars, second.Repeats, second.Ramp, second.AudibleBars, second.SilentBars, second.BeatsPerBar, second.BeatDenominator));
    }

    [TestMethod]
    public void ASteadyStepStaysSteadyAndTheScaledProgramStillPassesTheProjectChecks()
    {
        // The project insists that a constant step has one tempo (start equals end), so scaling must give both the same number
        // for any ratio, and everything it writes must be a tempo the project accepts.
        foreach (var tapped in new[] { 40d, 61.3, 96.4, 133.7, 250d, 900d })
        {
            var document = new PulseDocument
            {
                Program = [
                    new() { StartTempo = 73, EndTempo = 73 },
                    new() { StartTempo = 73, EndTempo = 73.5, Ramp = "LinearBeat" },
                    new() { StartTempo = 61, EndTempo = 61 }
                ]
            };
            TempoEdits.ApplyTapIn(document, tapped);
            Assert.AreEqual(document.Program[0].StartTempo, document.Program[0].EndTempo, $"{tapped}");
            Assert.AreEqual(tapped, document.Program[0].StartTempo, $"{tapped}");
            Assert.AreEqual(document.Program[2].StartTempo, document.Program[2].EndTempo, $"{tapped}");
            DocumentValidator.Validate(document);
        }
    }

    [TestMethod]
    public void TheTempoPanelWritesOnlyWhatWasChangedInIt()
    {
        // A field that still shows what it showed when the panel opened must not put that value over a newer one: the tempo that a
        // Tap to start has saved since, or a nudge that was still on its way. Only the number of taps changes nothing of the project.
        Assert.AreEqual((false, false), TempoEdits.PanelEdits(120, 1, 120, 1));
        Assert.AreEqual((true, false), TempoEdits.PanelEdits(120, 1, 90, 1));
        Assert.AreEqual((false, true), TempoEdits.PanelEdits(120, 1, 120, 0.5));
        Assert.AreEqual((true, true), TempoEdits.PanelEdits(120, 1, 90, 1.5));
        // A tempo kept to more than a tenth shows as typed to the tenth: that is not an edit of it.
        Assert.AreEqual((false, false), TempoEdits.PanelEdits(120.04, 1, 120, 1));
        Assert.AreEqual((true, false), TempoEdits.PanelEdits(120.04, 1, 120.1, 1));
        Assert.AreEqual((false, false), TempoEdits.PanelEdits(120, 1, double.NaN, 1));
    }

    [TestMethod]
    public void TheSetlistIsLeftAlone()
    {
        var song = new SongEntry { Name = "Song", Document = new PulseDocument { Program = [new() { StartTempo = 90, EndTempo = 90 }] } };
        var document = new PulseDocument { Setlist = [song], Program = [new() { StartTempo = 60, EndTempo = 60 }] };
        TempoEdits.ApplyTapIn(document, 120);
        Assert.AreEqual(90d, document.Setlist[0].Document.Program[0].StartTempo);
        Assert.AreEqual(120d, document.Setlist[0].Document.Settings.Tempo);
        Assert.AreEqual(120d, document.Program[0].StartTempo);
    }

    [TestMethod]
    public async Task ASecondPressOfDoneWhileTheFirstIsBeingAppliedDoesNothingAndLeavesThePanelOpen()
    {
        var applied = 0;
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var apply = TempoEdits.OneAtATime(async () =>
        {
            applied++;
            return await finish.Task;
        }, () => 1);

        var first = apply();
        var second = await apply().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsFalse(second, "false leaves the panel open: the first press will close it");
        Assert.AreEqual(1, applied, "the tempo is not written a second time");
        Assert.IsFalse(first.IsCompleted);
        finish.SetResult(true);
        Assert.IsTrue(await first.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [TestMethod]
    public async Task APressOfDoneAfterTheFirstHasBeenAppliedAppliesAgain()
    {
        var applied = 0;
        var apply = TempoEdits.OneAtATime(() =>
        {
            applied++;
            return Task.FromResult(true);
        }, () => 1);

        Assert.IsTrue(await apply());
        Assert.IsTrue(await apply());

        Assert.AreEqual(2, applied);
    }

    [TestMethod]
    public async Task AnApplyThatEndsAfterThePanelWasOpenedAgainDoesNotAskForTheNewOpeningToBeClosed()
    {
        // Done was pressed, the panel was dismissed and opened again (with a draft of its own, on which Done has not been pressed), and the
        // first apply ends: its change is applied, but it must not close what is open now.
        var opening = 1;
        var applied = 0;
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var apply = TempoEdits.OneAtATime(async () =>
        {
            var result = await gate.Task;
            applied++;
            return result;
        }, () => opening);

        var first = apply();
        opening = 2;
        gate.SetResult(true);

        Assert.IsFalse(await first.WaitAsync(TimeSpan.FromSeconds(10)), "false leaves the panel of the new opening open, with its draft");
        Assert.AreEqual(1, applied, "the apply ran to its end, so the change was made");
    }

    [TestMethod]
    public async Task APressOfDoneInANewOpeningOfThePanelIsAppliedWhileAnEarlierOneIsStillBeingApplied()
    {
        // Done was pressed, the panel was dismissed and opened again while it waited, and a tempo was typed and Done pressed again:
        // that press is not the first one's repeat, and is neither lost nor let through twice.
        var opening = 1;
        var applied = new List<int>();
        var gates = new List<TaskCompletionSource<bool>>();
        var apply = TempoEdits.OneAtATime(async () =>
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            gates.Add(gate);
            applied.Add(opening);
            return await gate.Task;
        }, () => opening);

        var first = apply();
        opening = 2;
        var second = apply();
        var repeat = await apply().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsFalse(repeat, "a repeat of the press of the same opening is ignored");
        CollectionAssert.AreEqual(new[] { 1, 2 }, applied, "the press of the new opening was applied");
        gates[0].SetResult(true);
        Assert.IsFalse(await first.WaitAsync(TimeSpan.FromSeconds(10)), "the first is applied but does not close the panel of the new opening");
        Assert.IsFalse(await apply().WaitAsync(TimeSpan.FromSeconds(10)), "the first press ending does not release the second's");
        Assert.AreEqual(2, applied.Count);
        gates[1].SetResult(true);
        Assert.IsTrue(await second.WaitAsync(TimeSpan.FromSeconds(10)));
        gates.Clear();
        var next = apply();
        Assert.AreEqual(3, applied.Count, "once the second has ended a new press is applied");
        gates[0].SetResult(true);
        Assert.IsTrue(await next.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [TestMethod]
    public async Task APressOfDoneAfterAnApplyThatFailedOrWasRefusedAppliesAgain()
    {
        // A change that could not be applied keeps the panel open with its message, and Done can be pressed again.
        var applied = 0;
        var apply = TempoEdits.OneAtATime(() =>
        {
            applied++;
            return applied == 1 ? Task.FromException<bool>(new IOException("The recovery file cannot be written.")) : Task.FromResult(applied != 2);
        }, () => 1);

        await Assert.ThrowsExactlyAsync<IOException>(() => apply());
        Assert.IsFalse(await apply(), "refused");
        Assert.IsTrue(await apply());
        Assert.AreEqual(3, applied);
    }
}
