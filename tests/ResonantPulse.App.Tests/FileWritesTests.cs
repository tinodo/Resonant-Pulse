using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

/// <summary>
/// The writes of files that the person asked for (Save, Save to library, Export WAV, Export MIDI, the export of separate tracks, the timing
/// CSV): when one is going, how closing the window stops what is being written, and what a close that is refused leaves alone.
/// </summary>
[TestClass]
public sealed class FileWritesTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private static FileWrite Begin(FileWrites writes, string? exclusiveName = null) =>
        exclusiveName is null ? writes.Begin() : writes.TryBeginExclusive(exclusiveName) ?? throw new AssertFailedException("The write was refused.");

    [TestMethod]
    public void AWriteIsGoingFromItsBeginUntilItsEnd()
    {
        var writes = new FileWrites();
        Assert.IsFalse(writes.AnyGoing);
        Assert.AreEqual(0, writes.Count);
        var write = Begin(writes);
        Assert.IsTrue(writes.AnyGoing);
        Assert.AreEqual(1, writes.Count);
        Assert.IsFalse(write.Token.IsCancellationRequested);
        write.End();
        Assert.IsFalse(writes.AnyGoing);
        Assert.AreEqual(0, writes.Count);
    }

    [TestMethod]
    public void WritesOfDifferentCommandsGoAtOnceAndEachEndsByItself()
    {
        // Save and Export WAV have always been allowed to overlap: the guard remembers them all and refuses none.
        var writes = new FileWrites();
        var first = Begin(writes);
        var second = Begin(writes);
        var third = Begin(writes);
        Assert.AreEqual(3, writes.Count);
        Assert.AreNotEqual(first.Token, second.Token, "Each write has a token of its own.");
        second.End();
        Assert.AreEqual(2, writes.Count);
        Assert.IsFalse(first.Token.IsCancellationRequested, "Ending one write does not touch the others.");
        first.End();
        third.End();
        Assert.IsFalse(writes.AnyGoing);
    }

    [TestMethod]
    public void AWriteThatMustBeTheOnlyOneOfItsNameIsRefusedWhileOneIsGoingAndOthersAreNot()
    {
        var writes = new FileWrites();
        var tracks = Begin(writes, "tracks");
        Assert.IsTrue(writes.IsGoing("tracks"));
        Assert.IsFalse(writes.IsGoing("other"));
        Assert.IsNull(writes.TryBeginExclusive("tracks"), "A second export of tracks cannot begin while one is going.");
        Assert.AreEqual(1, writes.Count, "Refusing it does not add a write, and does not end the first.");
        Assert.IsNotNull(writes.Begin(), "A write with no name is never refused.");
        Assert.IsNotNull(writes.TryBeginExclusive("other"), "Another name is another command.");
        tracks.End();
        Assert.IsFalse(writes.IsGoing("tracks"));
        var again = writes.TryBeginExclusive("tracks");
        Assert.IsNotNull(again, "Once the first has ended another can begin.");
        Assert.IsFalse(again.Token.IsCancellationRequested, "A new write has a token of its own, not the cancelled one of the write before.");
    }

    [TestMethod]
    public void EndingTwiceOrDisposingAfterEndingIsHarmlessAndLeavesNothingBehind()
    {
        var writes = new FileWrites();
        var write = Begin(writes);
        write.End();
        write.End();
        write.Dispose();
        Assert.IsFalse(writes.AnyGoing);
        using (Begin(writes)) Assert.AreEqual(1, writes.Count);
        Assert.AreEqual(0, writes.Count, "Disposing a write ends it.");
        var named = Begin(writes, "tracks");
        named.End();
        named.End();
        Assert.IsNotNull(writes.TryBeginExclusive("tracks"), "Ending twice did not leave a write behind.");
    }

    [TestMethod]
    public async Task ACloseThatGoesStopsEveryWriteAndWaitsUntilAllOfThemHaveEnded()
    {
        var writes = new FileWrites();
        var first = Begin(writes);
        var second = Begin(writes);
        var closing = writes.StopForCloseAsync(windowWillGo: true, TimeSpan.FromSeconds(30));
        Assert.IsTrue(first.Token.IsCancellationRequested, "Every write is told to stop at once.");
        Assert.IsTrue(second.Token.IsCancellationRequested);
        await Task.Delay(100);
        Assert.IsFalse(closing.IsCompleted, "Closing waits for the writes to clean up (their temporary files are removed as they stop).");
        first.End();
        await Task.Delay(100);
        Assert.IsFalse(closing.IsCompleted, "Closing waits for all of them, not for the first.");
        second.End();
        Assert.IsTrue(await closing.WaitAsync(Limit), "It reports that nothing is going any more.");
        Assert.IsFalse(writes.AnyGoing);
    }

    [TestMethod]
    public async Task AWriteThatStopsOnItsTokenCleansUpBeforeClosingGoesOn()
    {
        var writes = new FileWrites();
        var write = Begin(writes);
        var cleaned = false;
        var export = Task.Run(async () =>
        {
            try { await Task.Delay(Timeout.Infinite, write.Token); }
            catch (OperationCanceledException) { await Task.Delay(50); cleaned = true; }
            finally { write.End(); }
        });
        var closing = writes.StopForCloseAsync(windowWillGo: true, TimeSpan.FromSeconds(30));
        Assert.IsTrue(await closing.WaitAsync(Limit));
        Assert.IsTrue(cleaned, "The write was told to stop and had cleaned up when closing was told that it had ended.");
        await export.WaitAsync(Limit);
    }

    [TestMethod]
    public async Task ACloseThatGoesGivesUpWaitingAfterTheLimitSoClosingCannotHangAndDoesNotPretendTheyEnded()
    {
        var writes = new FileWrites();
        var stuck = Begin(writes);
        var fine = Begin(writes);
        fine.End();
        Assert.IsFalse(await writes.StopForCloseAsync(windowWillGo: true, TimeSpan.FromMilliseconds(100)).WaitAsync(Limit), "The window does not wait for ever.");
        Assert.IsTrue(stuck.Token.IsCancellationRequested);
        Assert.IsTrue(writes.AnyGoing, "Giving up does not pretend that the write has ended.");
        stuck.End();
    }

    [TestMethod]
    public async Task ACloseWithNothingGoingIsAtOnceAndCancelsNothingThatEndedBefore()
    {
        var writes = new FileWrites();
        Assert.IsTrue(await writes.StopForCloseAsync(windowWillGo: true, TimeSpan.FromSeconds(30)).WaitAsync(Limit));
        Assert.IsTrue(await writes.StopForCloseAsync(windowWillGo: false, TimeSpan.FromSeconds(30)).WaitAsync(Limit));
        var write = Begin(writes);
        var token = write.Token;
        write.End();
        Assert.IsTrue(await writes.StopForCloseAsync(windowWillGo: true, TimeSpan.FromSeconds(30)).WaitAsync(Limit), "A write that has ended is not waited for.");
        Assert.IsFalse(token.IsCancellationRequested, "A write that had ended was not cancelled afterwards.");
    }

    [TestMethod]
    public async Task ACloseThatIsRefusedLeavesAWriteThatIsWritingGoingAndSaysSo()
    {
        // The window stays when the project could not be saved: a file that is being written goes on, and closing is told it has not gone.
        var writes = new FileWrites();
        var write = Begin(writes);
        write.BeginWriting(@"C:\Music\click.wav");
        Assert.IsFalse(await writes.StopForCloseAsync(windowWillGo: false, TimeSpan.FromSeconds(30)).WaitAsync(Limit), "It is still going, and says so at once.");
        Assert.IsFalse(write.Token.IsCancellationRequested, "A refused close does not stop a write that is going.");
        Assert.IsFalse(write.PreparationInvalidated, "A write that is being written is not the preparation that a refused close voids.");
        Assert.IsTrue(write.StillWanted);
        write.End();
        Assert.IsTrue(await writes.StopForCloseAsync(windowWillGo: false, TimeSpan.FromSeconds(30)).WaitAsync(Limit), "Nothing is going: it says so.");
    }

    [TestMethod]
    public async Task ACloseThatIsRefusedBeforeTheFileIsWrittenVoidsWhatIsStillBeingPreparedButDoesNotCancelIt()
    {
        var writes = new FileWrites();
        var preparing = Begin(writes);
        var writing = Begin(writes);
        writing.BeginWriting();
        Assert.IsFalse(preparing.PreparationInvalidated, "A new write starts with its preparation valid.");
        Assert.IsFalse(await writes.StopForCloseAsync(windowWillGo: false, TimeSpan.FromSeconds(30)).WaitAsync(Limit));
        Assert.IsTrue(preparing.PreparationInvalidated, "A plan that was being compiled, or a question that was not yet asked, is not asked about afterwards.");
        Assert.IsFalse(preparing.StillWanted, "The page looks at this before it asks.");
        Assert.IsFalse(preparing.Token.IsCancellationRequested, "The write is not cancelled: it ends by itself when it looks at the mark, and the window stays usable.");
        Assert.IsFalse(writing.PreparationInvalidated);
        Assert.IsTrue(writing.StillWanted);
        preparing.End();
        writing.End();
        Assert.IsFalse(writes.AnyGoing);
    }

    [TestMethod]
    public void TheMarkOfOneWriteDoesNotReachTheNextOne()
    {
        var writes = new FileWrites();
        var first = Begin(writes);
        _ = writes.StopForCloseAsync(windowWillGo: false, TimeSpan.FromSeconds(30));
        Assert.IsTrue(first.PreparationInvalidated);
        first.End();
        var second = Begin(writes);
        Assert.IsFalse(second.PreparationInvalidated, "A write begun after the close was refused asks its question.");
        Assert.IsTrue(second.StillWanted);
        second.End();
        // A refused close with no write going leaves nothing behind for the write that follows.
        _ = writes.StopForCloseAsync(windowWillGo: false, TimeSpan.FromSeconds(30));
        var third = Begin(writes);
        Assert.IsFalse(third.PreparationInvalidated);
        third.End();
    }

    [TestMethod]
    public void OnlyAWriteThatIsStillBeingPreparedCanBeVoided()
    {
        var writes = new FileWrites();
        var writing = Begin(writes);
        writing.BeginWriting();
        writing.InvalidatePreparation();
        Assert.IsFalse(writing.PreparationInvalidated, "A write that is being written is not voided, whoever asks: its file is going to be written.");
        Assert.IsTrue(writing.StillWanted);
        var ended = Begin(writes);
        ended.End();
        ended.InvalidatePreparation();
        Assert.IsFalse(ended.PreparationInvalidated, "A write that has ended is not marked either.");
        writing.End();
    }

    [TestMethod]
    public void AWriteThatHasEndedStaysEndedWhateverIsDoneToItAfterwards()
    {
        var writes = new FileWrites();
        var write = Begin(writes);
        write.BeginWriting(@"C:\Music\click.wav");
        Assert.IsTrue(write.IsWriting);
        write.End();
        Assert.IsFalse(write.IsWriting, "An ended write is not writing.");
        write.BeginWriting(@"C:\Music\other.wav");
        Assert.IsFalse(write.IsWriting, "A late call does not bring an ended write back.");
        Assert.AreEqual(@"C:\Music\click.wav", write.Destination, "Nor does it give it another file.");
        Assert.IsFalse(writes.AnyGoing);
    }

    [TestMethod]
    public async Task ACloseThatWasRefusedAndThenGoesStopsTheWritesThatTheFirstLeftGoing()
    {
        var writes = new FileWrites();
        var write = Begin(writes);
        write.BeginWriting();
        Assert.IsFalse(await writes.StopForCloseAsync(windowWillGo: false, TimeSpan.FromSeconds(30)).WaitAsync(Limit));
        Assert.IsFalse(write.Token.IsCancellationRequested);
        var second = writes.StopForCloseAsync(windowWillGo: true, TimeSpan.FromSeconds(30));
        Assert.IsTrue(write.Token.IsCancellationRequested, "The second attempt closes the window, so the write is stopped now.");
        write.End();
        Assert.IsTrue(await second.WaitAsync(Limit));
    }

    [TestMethod]
    public void AWriteIsWritingFromTheMomentItBeginsWritingUntilItEnds()
    {
        var writes = new FileWrites();
        var tracks = Begin(writes, "tracks");
        Assert.IsFalse(tracks.IsWriting, "The compile, the question and the choice of the file come before the writing.");
        Assert.IsFalse(writes.IsWriting("tracks"));
        tracks.BeginWriting(@"C:\Music\song-tracks.zip");
        Assert.IsTrue(tracks.IsWriting);
        Assert.IsTrue(writes.IsWriting("tracks"));
        Assert.IsFalse(writes.IsWriting("other"));
        tracks.End();
        Assert.IsFalse(writes.IsWriting("tracks"));
        var next = Begin(writes, "tracks");
        Assert.IsFalse(next.IsWriting, "A new write is not writing yet.");
        next.End();
    }

    [TestMethod]
    public async Task TheDestinationsOfTheWritesThatAreGoingAreKnownSoThatTheLogCanNameTheFilesWhoseTemporaryFileMayStay()
    {
        var writes = new FileWrites();
        var preparing = Begin(writes);
        var wave = Begin(writes);
        wave.BeginWriting(@"C:\Music\click.wav");
        var tracks = Begin(writes, "tracks");
        tracks.BeginWriting(@"C:\Music\click-tracks.zip");
        var unnamed = Begin(writes);
        unnamed.BeginWriting();
        CollectionAssert.AreEquivalent(new[] { @"C:\Music\click.wav", @"C:\Music\click-tracks.zip" }, writes.Destinations.ToArray(),
            "Only a write that has a file is named: a preparation has no temporary file.");
        wave.End();
        CollectionAssert.AreEquivalent(new[] { @"C:\Music\click-tracks.zip" }, writes.Destinations.ToArray());
        preparing.End();
        tracks.End();
        unnamed.End();
        CollectionAssert.AreEqual(Array.Empty<string>(), writes.Destinations.ToArray());
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task APlanThatIsStillBeingCompiledWhenACloseIsRefusedIsNotAskedAboutAfterwardsAndOneThatIsStillWantedIs()
    {
        // The order the pages follow (MainWindow.Files.cs): begin, prepare, look at StillWanted, ask, write, end. The preparation is held
        // here until the close has been refused (or has gone), which is how a page can be at that point when the person closes.
        foreach (var close in new[] { "none", "refused", "goes" })
        {
            var writes = new FileWrites();
            var prepared = new TaskCompletionSource();
            var asked = false;
            var write = Begin(writes);
            var page = Task.Run(async () =>
            {
                try
                {
                    await prepared.Task;
                    if (!write.StillWanted) return;
                    asked = true;
                    write.BeginWriting();
                }
                finally { write.End(); }
            });
            var closing = close switch
            {
                "refused" => writes.StopForCloseAsync(windowWillGo: false, TimeSpan.FromSeconds(30)),
                "goes" => writes.StopForCloseAsync(windowWillGo: true, TimeSpan.FromSeconds(30)),
                _ => Task.FromResult(true)
            };
            if (close == "refused") Assert.IsFalse(await closing.WaitAsync(Limit));
            prepared.SetResult();
            await page.WaitAsync(Limit);
            Assert.AreEqual(close == "none", asked, close switch
            {
                "none" => "Without a close the question is asked.",
                "refused" => "The question was asked after the close had been refused.",
                _ => "The question was asked on a window that is closing."
            });
            Assert.IsFalse(writes.AnyGoing, "The write has ended, so its command is on again.");
            if (close == "goes") Assert.IsTrue(await closing.WaitAsync(Limit));
        }
    }
}
