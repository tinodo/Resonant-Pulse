using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class TempoEditsTests
{
    [TestMethod]
    public async Task DelayedTapInCleanupDoesNotOverwriteANewerQueuedManualInput()
    {
        var input = 120d;
        var inputVersion = 0L;
        var captured = inputVersion;
        var published = 132d;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task PublishManual()
        {
            await release.Task;
            published = 144;
        }
        input = 144;
        inputVersion++;
        var pending = PublishManual();
        if (TempoEdits.ShouldSynchronizeTapIn(captured, inputVersion)) input = published;
        release.SetResult();
        await pending;
        Assert.AreEqual(144d, input);
        Assert.AreEqual(144d, published);
        Assert.IsTrue(TempoEdits.ShouldSynchronizeTapIn(inputVersion, inputVersion));
        // Returning to the original visible value is still an intervening edit.
        input = 120;
        inputVersion++;
        Assert.IsFalse(TempoEdits.ShouldSynchronizeTapIn(captured, inputVersion));
        Assert.AreEqual(120d, input);
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
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TempoEdits.ApplyTapIn(document, tempo));
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
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TempoEdits.ApplyTapIn(document, tempo));
        Assert.AreEqual(120d, document.Settings.Tempo);
        Assert.AreEqual(start, document.Program[0].StartTempo);
        Assert.AreEqual(end, document.Program[0].EndTempo);
    }

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
}
