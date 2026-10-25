using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class LocalWorkspaceTests
{
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
