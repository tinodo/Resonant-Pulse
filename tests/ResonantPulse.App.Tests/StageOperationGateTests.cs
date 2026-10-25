using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class StageOperationGateTests
{
    [TestMethod]
    public async Task HeldValidationRejectsSecondMoveSelectionAndNextWithoutExecutingTheirCallbacks()
    {
        var gate = new StageOperationGate();
        var validating = Signal();
        var finishValidation = Signal();
        var armedIndex = 2;
        var publishedSongs = new[] { "A", "B", "C" };
        var rejectedCallbacks = 0;

        // This is the production gate linked by the test project. Hold its
        // actual async ownership across validation and selection publication.
        var firstMove = gate.TryRunAsync(async () =>
        {
            var revised = new[] { "A", "C", "B" };
            validating.SetResult(true);
            await finishValidation.Task;
            publishedSongs = revised;
            armedIndex = 1;
        });
        await validating.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var wasBusy = gate.IsBusy;
        var acceptedRequests = new List<bool>();
        foreach (var request in new[] { "MoveUp", "MoveDown", "Selection", "Next", "Remove", "Notes", "Add", "Lock" })
        {
            acceptedRequests.Add(await gate.TryRunAsync(() =>
            {
                rejectedCallbacks++;
                armedIndex = 0;
                return Task.CompletedTask;
            }));
        }
        var selectionWhileValidating = armedIndex;
        finishValidation.SetResult(true);
        Assert.IsTrue(await firstMove.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.IsTrue(wasBusy);
        Assert.IsTrue(acceptedRequests.All(accepted => !accepted));
        Assert.AreEqual(0, rejectedCallbacks);
        Assert.AreEqual(2, selectionWhileValidating);
        Assert.AreEqual(1, armedIndex);
        CollectionAssert.AreEqual(new[] { "A", "C", "B" }, publishedSongs);
        Assert.IsFalse(gate.IsBusy);

        string? startedSong = null;
        Assert.IsTrue(await gate.TryRunAsync(() =>
        {
            startedSong = publishedSongs[armedIndex];
            return Task.CompletedTask;
        }));
        Assert.AreEqual("C", startedSong);
    }

    [TestMethod]
    public async Task PublicationDoesNotReleaseOwnershipBeforeBookkeepingAndStartFinish()
    {
        var gate = new StageOperationGate();
        var published = Signal();
        var finishBookkeeping = Signal();
        var starting = Signal();
        var finishStart = Signal();
        var armedIndex = 2;
        var songs = new[] { "A", "B", "C" };
        var staleNextCalls = 0;

        var operation = gate.TryRunAsync(async () =>
        {
            songs = ["A", "C", "B"];
            published.SetResult(true);
            await finishBookkeeping.Task;
            armedIndex = 1;
            starting.SetResult(true);
            await finishStart.Task;
        });
        await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<bool> TryNextAsync() => gate.TryRunAsync(() =>
        {
            staleNextCalls++;
            return Task.CompletedTask;
        });
        var nextDuringBookkeeping = await TryNextAsync();
        finishBookkeeping.SetResult(true);
        await starting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var nextDuringStart = await TryNextAsync();
        var busyDuringStart = gate.IsBusy;
        finishStart.SetResult(true);
        Assert.IsTrue(await operation.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.IsFalse(nextDuringBookkeeping);
        Assert.IsFalse(nextDuringStart);
        Assert.IsTrue(busyDuringStart);
        Assert.AreEqual(0, staleNextCalls);
        Assert.AreEqual("C", songs[armedIndex]);
        Assert.IsFalse(gate.IsBusy);
    }

    [TestMethod]
    public async Task ThrownOperationReleasesOwnershipAndAllowsNextRequest()
    {
        var gate = new StageOperationGate();
        var failure = new InvalidOperationException("Validation failed");
        Exception? observed = null;
        try
        {
            await gate.TryRunAsync(async () =>
            {
                await Task.Yield();
                throw failure;
            });
        }
        catch (InvalidOperationException exception) { observed = exception; }

        Assert.AreSame(failure, observed);
        Assert.IsFalse(gate.IsBusy);
        var ran = false;
        Assert.IsTrue(await gate.TryRunAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        }));
        Assert.IsTrue(ran);
    }

    [TestMethod]
    public async Task CancelledOperationReleasesOwnership()
    {
        var gate = new StageOperationGate();
        var cancelled = false;
        try
        {
            await gate.TryRunAsync(() => Task.FromCanceled(new CancellationToken(canceled: true)));
        }
        catch (OperationCanceledException) { cancelled = true; }

        Assert.IsTrue(cancelled);
        Assert.IsFalse(gate.IsBusy);
        Assert.IsTrue(await gate.TryRunAsync(() => Task.CompletedTask));
    }

    private static TaskCompletionSource<bool> Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
