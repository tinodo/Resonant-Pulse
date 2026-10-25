using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class DocumentPublicationTests
{
    [TestMethod]
    public async Task RecoveryFailureAfterPublicationRendersNewDocumentThenPropagatesOriginalError()
    {
        using var edits = new SemaphoreSlim(1);
        var oldDocument = new PulseDocument { Name = "Previous project" };
        var newDocument = new PulseDocument { Name = "Imported project" };
        var document = oldDocument;
        var renderedDocument = oldDocument;
        var failure = new IOException("Recovery write failed");
        Exception? observed = null;
        var renderedOutsideEditLock = false;
        var renderCount = 0;
        try
        {
            await DocumentPublication.RunAsync(edits, async published =>
            {
                document = newDocument;
                published();
                await Task.Yield();
                throw failure;
            }, succeeded =>
            {
                Assert.IsFalse(succeeded);
                renderedOutsideEditLock = edits.Wait(0);
                if (renderedOutsideEditLock) edits.Release();
                renderedDocument = document;
                renderCount++;
            });
        }
        catch (Exception exception) { observed = exception; }

        Assert.AreSame(failure, observed);
        Assert.AreSame(newDocument, document);
        Assert.AreSame(newDocument, renderedDocument);
        Assert.IsTrue(renderedOutsideEditLock);
        Assert.AreEqual(1, renderCount);
        Assert.AreEqual(1, edits.CurrentCount);
    }

    [TestMethod]
    public async Task FailureBeforePublicationKeepsPreviousPageAndReleasesEditLock()
    {
        using var edits = new SemaphoreSlim(1);
        var failure = new IOException("Backup failed before replacement");
        Exception? observed = null;
        var renderCount = 0;
        try
        {
            await DocumentPublication.RunAsync(edits, _ => Task.FromException(failure), _ => renderCount++);
        }
        catch (Exception exception) { observed = exception; }

        Assert.AreSame(failure, observed);
        Assert.AreEqual(0, renderCount);
        Assert.AreEqual(1, edits.CurrentCount);
    }

    [TestMethod]
    public async Task SuccessfulPublicationRendersAfterRecoveryAndAfterReleasingEditLock()
    {
        using var edits = new SemaphoreSlim(1);
        var order = new List<string>();
        await DocumentPublication.RunAsync(edits, async published =>
        {
            order.Add("publish");
            published();
            await Task.Yield();
            order.Add("recovery");
        }, succeeded =>
        {
            Assert.IsTrue(succeeded);
            Assert.AreEqual(1, edits.CurrentCount);
            order.Add("render");
        });
        CollectionAssert.AreEqual(new[] { "publish", "recovery", "render" }, order);
    }

    [TestMethod]
    public async Task RenderingFailureCannotHideTheOriginalPersistenceFailure()
    {
        using var edits = new SemaphoreSlim(1);
        var persistenceFailure = new IOException("Recovery failed");
        var renderingFailure = new InvalidOperationException("Rendering failed");
        AggregateException? observed = null;
        try
        {
            await DocumentPublication.RunAsync(edits, published =>
            {
                published();
                return Task.FromException(persistenceFailure);
            }, _ => throw renderingFailure);
        }
        catch (AggregateException exception) { observed = exception; }

        Assert.IsNotNull(observed);
        Assert.AreSame(persistenceFailure, observed.InnerExceptions[0]);
        Assert.AreSame(renderingFailure, observed.InnerExceptions[1]);
        Assert.AreEqual(1, edits.CurrentCount);
    }
}
