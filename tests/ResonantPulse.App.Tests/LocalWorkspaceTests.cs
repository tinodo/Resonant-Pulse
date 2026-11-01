using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class LocalWorkspaceTests
{
    [TestMethod]
    public async Task ConcurrentPreferenceWritersAcrossInstancesPublishCompleteSnapshots()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResonantPulse-AppTests-" + Guid.NewGuid().ToString("N"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task SaveAsync(int index)
        {
            await release.Task;
            var id = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await new LocalWorkspace(root).SavePreferencesAsync(new Preferences
            {
                Language = id, OutputId = $"output-{id}", OutputName = new string('x', 16384) + id
            });
        }
        var saves = Enumerable.Range(0, 32).Select(SaveAsync).ToArray();
        try
        {
            release.SetResult();
            await Task.WhenAll(saves);
            var preferences = await new LocalWorkspace(root).LoadPreferencesAsync();
            Assert.IsTrue(Enumerable.Range(0, 32).Any(index =>
                preferences.Language == index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.AreEqual($"output-{preferences.Language}", preferences.OutputId);
            Assert.AreEqual(new string('x', 16384) + preferences.Language, preferences.OutputName);
            Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp").Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task PreferenceSaveFailureDoesNotLeaveTemporaryFilesAndAllowsRetry()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResonantPulse-AppTests-" + Guid.NewGuid().ToString("N"));
        var workspace = new LocalWorkspace(root);
        var path = Path.Combine(root, "preferences.json");
        Directory.CreateDirectory(path);
        try
        {
            ArgumentException? failure = null;
            try { await workspace.SavePreferencesAsync(new Preferences { Language = "nl" }); }
            catch (ArgumentException exception) { failure = exception; }
            Assert.IsNotNull(failure);
            Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp").Length);
            Directory.Delete(path);
            await workspace.SavePreferencesAsync(new Preferences { Language = "de" });
            Assert.AreEqual("de", (await workspace.LoadPreferencesAsync()).Language);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ThePreferencesFileIsBehindOnlyBetweenAFailedWriteAndTheNextSuccessfulOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResonantPulse-AppTests-" + Guid.NewGuid().ToString("N"));
        var workspace = new LocalWorkspace(root);
        var path = Path.Combine(root, "preferences.json");
        try
        {
            Assert.IsFalse(workspace.PreferencesBehind, "nothing was asked for");
            await workspace.SavePreferencesAsync(new Preferences { Language = "nl" });
            Assert.IsFalse(workspace.PreferencesBehind);

            File.Delete(path);
            Directory.CreateDirectory(path);
            try { await workspace.SavePreferencesAsync(new Preferences { Language = "de" }); }
            catch (ArgumentException) { /* the write is meant to fail */ }
            Assert.IsTrue(workspace.PreferencesBehind, "the write failed, so the file is behind the settings");
            Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp").Length);

            Directory.Delete(path);
            await workspace.SavePreferencesAsync(new Preferences { Language = "de" });
            Assert.IsFalse(workspace.PreferencesBehind, "a successful write catches the file up");
            Assert.AreEqual("de", (await workspace.LoadPreferencesAsync()).Language);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task PreferencesSavedByEarlierPreviewsLoadWithTheDefaultNumberOfTaps()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResonantPulse-AppTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "preferences.json"), """{"Language":"nl","LastView":"Settings","ExportBars":8}""");
            var preferences = await new LocalWorkspace(root).LoadPreferencesAsync();
            Assert.AreEqual("nl", preferences.Language);
            Assert.AreEqual(8, preferences.ExportBars);
            Assert.AreEqual(TapStartRules.DefaultTaps, preferences.TapInCount);
            Assert.AreEqual(4, new Preferences().TapInCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task TheNumberOfTapsSurvivesARestartAndAFileWithAWildValueIsBroughtIntoRangeWhenUsed()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResonantPulse-AppTests-" + Guid.NewGuid().ToString("N"));
        var workspace = new LocalWorkspace(root);
        try
        {
            await workspace.SavePreferencesAsync(new Preferences { TapInCount = 7 });
            Assert.AreEqual(7, (await new LocalWorkspace(root).LoadPreferencesAsync()).TapInCount);
            // A settings file is untrusted input: whatever it holds, the number of taps in use is between 2 and 16.
            foreach (var (stored, used) in new[] { (0, 2), (1, 2), (500, 16), (-9, 2), (16, 16) })
            {
                await File.WriteAllTextAsync(Path.Combine(root, "preferences.json"), $$"""{"TapInCount":{{stored}}}""");
                var loaded = await new LocalWorkspace(root).LoadPreferencesAsync();
                Assert.AreEqual(stored, loaded.TapInCount, "the file is read as it is");
                Assert.AreEqual(used, TapStartRules.ClampTaps(loaded.TapInCount), $"{stored}");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task MixedValidAndCorruptLibraryRetainsGoodProjectsAndReportsExactFailedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResonantPulse-AppTests-" + Guid.NewGuid().ToString("N"));
        var workspace = new LocalWorkspace(root);
        Directory.CreateDirectory(workspace.LibraryPath);
        try
        {
            var alpha = Path.Combine(workspace.LibraryPath, "alpha.pulse");
            var beta = Path.Combine(workspace.LibraryPath, "beta.pulse");
            var corrupt = Path.Combine(workspace.LibraryPath, "corrupt.pulse");
            var empty = Path.Combine(workspace.LibraryPath, "empty.pulse");
            await DocumentStore.SaveAsync(beta, new PulseDocument { Name = "Beta" });
            await File.WriteAllTextAsync(corrupt, "{ this is not valid JSON");
            await DocumentStore.SaveAsync(alpha, new PulseDocument { Name = "Alpha" });
            await File.WriteAllTextAsync(empty, "");

            var result = await workspace.ListAsync();

            CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, result.Items.Select(item => item.Name).ToArray());
            CollectionAssert.AreEquivalent(new[] { alpha, beta }, result.Items.Select(item => item.Path).ToArray());
            CollectionAssert.AreEquivalent(new[] { corrupt, empty }, result.Failures.Select(item => item.Path).ToArray());
            Assert.IsTrue(result.Failures.All(failure => failure.Error is InvalidDataException));
            Assert.IsFalse(result.HasMore);
            Assert.IsTrue(File.Exists(corrupt));
            Assert.IsTrue(File.Exists(empty));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task SavingToTheLibraryStopsWhenItsTokenIsCancelledAndLeavesNothingBehind()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResonantPulse-AppTests-" + Guid.NewGuid().ToString("N"));
        var workspace = new LocalWorkspace(root);
        try
        {
            using var source = new CancellationTokenSource();
            await source.CancelAsync();
            OperationCanceledException? stopped = null;
            try { await workspace.SaveLibraryAsync(new PulseDocument { Name = "Cancelled" }, source.Token); }
            catch (OperationCanceledException exception) { stopped = exception; }
            Assert.IsNotNull(stopped, "A save whose token was cancelled went on to the end.");
            Assert.AreEqual(0, Directory.GetFiles(workspace.LibraryPath).Length, "A file was left in the library folder.");

            await workspace.SaveLibraryAsync(new PulseDocument { Name = "Kept" });
            Assert.AreEqual(1, Directory.GetFiles(workspace.LibraryPath, "*.pulse").Length);
            Assert.AreEqual(0, Directory.GetFiles(workspace.LibraryPath, "*.tmp").Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task MissingFirstLaunchLibraryIsEmptyRatherThanAnError()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResonantPulse-AppTests-" + Guid.NewGuid().ToString("N"));
        var result = await new LocalWorkspace(root).ListAsync();
        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(0, result.Failures.Count);
        Assert.IsFalse(result.HasMore);
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public async Task DirectoryEnumerationFailureIsNotReportedAsAnEmptySuccessfulLibrary()
    {
        var root = Path.Combine(Path.GetTempPath(), "ResonantPulse-AppTests-" + Guid.NewGuid().ToString("N"));
        var workspace = new LocalWorkspace(root);
        Directory.CreateDirectory(root);
        try
        {
            // A file occupying the Library directory path is an enumeration
            // failure, not a corrupt entry and not an absent first-run library.
            await File.WriteAllTextAsync(workspace.LibraryPath, "not a directory");
            IOException? failure = null;
            try { await workspace.ListAsync(); }
            catch (IOException exception) { failure = exception; }
            Assert.IsNotNull(failure);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    [DataRow(1000, false)]
    [DataRow(1002, false)]
    [DataRow(1002, true)]
    public async Task LibraryScanReportsOverflowIncludingCorruptEntriesWithoutDeletingFiles(int fileCount, bool allCorrupt)
    {
        var root = Path.Combine(Path.GetTempPath(), "ResonantPulse-AppTests-" + Guid.NewGuid().ToString("N"));
        var workspace = new LocalWorkspace(root);
        Directory.CreateDirectory(workspace.LibraryPath);
        try
        {
            var validJson = JsonSerializer.Serialize(new PulseDocument { Name = "Valid project" });
            // Reverse creation order proves that selection is based on path,
            // not the platform-dependent order of directory enumeration.
            for (var index = fileCount - 1; index >= 0; index--)
                await File.WriteAllTextAsync(Path.Combine(workspace.LibraryPath, $"{index:D4}.pulse"),
                    allCorrupt || index == 0 ? "invalid JSON" : validJson);

            var result = await workspace.ListAsync();

            Assert.AreEqual(fileCount > LocalWorkspace.LibraryScanLimit, result.HasMore);
            Assert.AreEqual(allCorrupt ? 0 : 999, result.Items.Count);
            Assert.AreEqual(allCorrupt ? 1000 : 1, result.Failures.Count);
            Assert.AreEqual(1000, result.Items.Count + result.Failures.Count);
            Assert.IsTrue(result.Items.Concat(result.Failures.Select(failure => new LibraryItem(failure.Path, "")))
                .All(item => int.Parse(Path.GetFileNameWithoutExtension(item.Path)) < 1000));
            if (!allCorrupt)
                Assert.IsTrue(result.Items.Any(item => Path.GetFileName(item.Path) == "0999.pulse"));
            Assert.AreEqual(fileCount, Directory.EnumerateFiles(workspace.LibraryPath, "*.pulse").Count());
        }
        finally { Directory.Delete(root, true); }
    }
}
