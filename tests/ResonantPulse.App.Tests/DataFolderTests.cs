using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Storage;

namespace ResonantPulse.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DataFolderTests
{
    [TestMethod]
    public void TheDataFolderCanBeIsolatedForTestInstancesAndDefaultsToLocalAppData()
    {
        var previous = Environment.GetEnvironmentVariable(LocalWorkspace.DataFolderVariable);
        try
        {
            Environment.SetEnvironmentVariable(LocalWorkspace.DataFolderVariable, null);
            var normal = LocalWorkspace.DefaultRoot();
            Assert.IsTrue(normal.EndsWith("ResonantPulse", StringComparison.Ordinal), normal);
            Environment.SetEnvironmentVariable(LocalWorkspace.DataFolderVariable, "   ");
            Assert.AreEqual(normal, LocalWorkspace.DefaultRoot());
            var isolated = Path.Combine(Path.GetTempPath(), "rp-isolated-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(LocalWorkspace.DataFolderVariable, isolated);
            Assert.AreEqual(isolated, LocalWorkspace.DefaultRoot());
            Assert.AreEqual(Path.Combine(Path.GetFullPath(isolated), "recovery.pulse"), new LocalWorkspace().RecoveryPath);
        }
        finally { Environment.SetEnvironmentVariable(LocalWorkspace.DataFolderVariable, previous); }
    }
}
