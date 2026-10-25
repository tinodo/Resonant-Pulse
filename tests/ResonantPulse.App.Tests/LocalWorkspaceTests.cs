using Microsoft.VisualStudio.TestTools.UnitTesting;
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
}
