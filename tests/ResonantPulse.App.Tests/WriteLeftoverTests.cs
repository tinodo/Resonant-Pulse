using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

/// <summary>
/// What a person is told when a file could not be saved and the temporary file beside the destination could not be removed either: which
/// file stays, whichever file was being written (Save, Save to library, Export WAV, Export MIDI, the separate tracks, the timing result's
/// CSV). The sentence is the page's, from <see cref="ErrorText.Explain"/>. The first two tests hold the sentence in every language; the
/// others run each kind of write for real, on Windows, where a read-only file cannot be deleted: the destination is held open, so that the
/// move over it fails again and again for more than a second (it is tried ten times), and the temporary file is made read-only meanwhile.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WriteLeftoverTests
{
    private static readonly string[] Languages = ["en", "nl", "de", "fr", "es"];

    private const string Leftover = @"C:\Music\Projects\.Take 1.pulse.0123456789abcdef0123456789abcdef.tmp";

    private static T InLanguage<T>(string language, Func<T> read)
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            Text.Select(language);
            return read();
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static TemporaryFileCleanupException CleanupFailed(string temporary = Leftover) =>
        new(temporary, $"Operation failed and temporary file cleanup failed: '{temporary}'.",
            new AggregateException(new IOException("There is not enough space on the disk."), new UnauthorizedAccessException("Access to the path is denied.")));

    [TestMethod]
    public void ATemporaryFileThatStaysIsNamedInEveryLanguageWhateverWasBeingWritten()
    {
        foreach (var language in Languages)
        {
            var generic = InLanguage(language, () => Text.Get("ErrorFile"));
            var text = InLanguage(language, () => ErrorText.Explain(CleanupFailed()));
            StringAssert.Contains(text, Leftover, language);
            Assert.AreNotEqual(generic, text, $"{language}: the general sentence does not say which file stays");
            Assert.AreEqual(InLanguage(language, () => Text.Format("ErrorFileLeftover", Leftover)), text, language);
            Assert.IsFalse(text.Contains("{0}"), $"{language}: the path was not put in: {text}");
            // A path is put in as it is, even with braces in it (a project or folder name may have them).
            const string braces = @"C:\Music\{0} {name}\.a.pulse.0123456789abcdef0123456789abcdef.tmp";
            StringAssert.Contains(InLanguage(language, () => ErrorText.Explain(CleanupFailed(braces))), braces, language);
            // Wrapped as the first of the causes, as a task that was waited for together with others reports it.
            Assert.AreEqual(text, InLanguage(language, () => ErrorText.Explain(new AggregateException(CleanupFailed()))), language);
        }
    }

    [TestMethod]
    public void EveryOtherFailureOfAWriteIsExplainedAsItWasAndTheTracksKeepTheirOwnSentence()
    {
        foreach (var language in Languages)
        {
            Assert.AreEqual(InLanguage(language, () => Text.Get("ErrorFile")),
                InLanguage(language, () => ErrorText.Explain(new IOException("There is not enough space on the disk."))), language);
            Assert.AreEqual(InLanguage(language, () => Text.Get("ErrorAccess")),
                InLanguage(language, () => ErrorText.Explain(new UnauthorizedAccessException())), language);
            // The export of separate tracks says it in its own words (the tracks) and is shown as it is, with no pointer to the log.
            var tracks = new UserMessageException(InLanguage(language, () => StemExport.NotSavedText(CleanupFailed())));
            Assert.AreEqual(tracks.Message, InLanguage(language, () => ErrorText.Explain(tracks)), language);
            Assert.AreNotEqual(InLanguage(language, () => ErrorText.Explain(CleanupFailed())), tracks.Message, language);
        }
    }

    private static readonly DateTimeOffset Done = new(2026, 11, 2, 0, 46, 32, TimeSpan.Zero);

    private static PulseDocument Document() => new() { Settings = new() { CountInBars = 0 } };

    [TestMethod]
    public async Task SaveNamesTheTemporaryFileThatStays()
    {
        using var folder = new Folder();
        var path = folder.PathOf("project.pulse");
        using var held = Hold(path);
        var failure = await Fails(folder.Root, () => DocumentStore.SaveAsync(path, Document()));
        AssertNamed(failure, folder.Root);
    }

    [TestMethod]
    public async Task SaveToLibraryNamesTheTemporaryFileThatStays()
    {
        using var folder = new Folder();
        var workspace = new LocalWorkspace(folder.Root);
        FileStream? held = null;
        try
        {
            // The library names its file just before it writes it: that is the moment to hold it.
            var failure = await Fails(workspace.LibraryPath,
                () => workspace.SaveLibraryAsync(Document(), CancellationToken.None, file => held = Hold(file)));
            Assert.IsNotNull(held, "the library told which file it writes");
            AssertNamed(failure, workspace.LibraryPath);
        }
        finally { held?.Dispose(); }
    }

    [TestMethod]
    public async Task ExportMidiNamesTheTemporaryFileThatStays()
    {
        using var folder = new Folder();
        var path = folder.PathOf("rhythm.mid");
        using var held = Hold(path);
        var failure = await Fails(folder.Root, () => MidiExporter.ExportAsync(path, Document(), 1));
        AssertNamed(failure, folder.Root);
    }

    [TestMethod]
    public async Task ExportOfTheTimingResultNamesTheTemporaryFileThatStays()
    {
        using var folder = new Folder();
        var path = folder.PathOf("check.csv");
        var session = new TimingSession(new TimingSessionContext("Practice", TimingSession.MainBeatName, -1, 8000, 0, 40000, true),
            [0, 1, 2, 3], [0.01, 1.02, 2.03, 3.04], 0.15, Done);
        using var held = Hold(path);
        var failure = await Fails(folder.Root, () => TimingCsvExporter.ExportAsync(path, session));
        AssertNamed(failure, folder.Root);
    }

    [TestMethod]
    public async Task ExportOfSeparateTracksNamesTheTemporaryFileThatStays()
    {
        using var folder = new Folder();
        var path = folder.PathOf("tracks.zip");
        var plan = ScheduleCompiler.Compile(Document(), 8000, 1, false);
        using var held = Hold(path);
        var failure = await Fails(folder.Root, () => WaveStemExporter.ExportAsync(path, plan, []));
        AssertNamed(failure, folder.Root);
    }

    [TestMethod]
    public async Task ExportWavNamesTheTemporaryFileThatStaysWhenItsOwnWriteFailed()
    {
        // The WAV is written to staging files that are moved into place together, and only those moves (the commit) were held open by
        // the other kinds' trick; here the write of the first staging file is what fails: it is stopped while it writes (a long project),
        // after its temporary file was made read-only. The typed outcomes of the commit (WaveExportException) are held by ErrorTextTests.
        using var folder = new Folder();
        var path = folder.PathOf("export.wav");
        var plan = ScheduleCompiler.Compile(Document(), 8000, 1024, false);
        using var stop = new CancellationTokenSource();
        var failure = await Fails(folder.Root, () => WaveExporter.ExportAsync(path, plan, stop.Token), stop);
        AssertNamed(failure, folder.Root);
    }

    /// <summary>A destination that is open elsewhere (no sharing of deletes) cannot be replaced: the move fails with a sharing violation.</summary>
    private static FileStream Hold(string path)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows: its read-only attribute blocks deleting the temporary file and its sharing rules block the move.");
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    /// <summary>
    /// Runs a write that must fail with its temporary file left behind: every temporary file that appears in <paramref name="folder"/>
    /// is made read-only at once (so that removing it fails), and <paramref name="cancelWhenLocked"/>, if given, is cancelled then.
    /// </summary>
    private static async Task<Exception> Fails(string folder, Func<Task> write, CancellationTokenSource? cancelWhenLocked = null)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows: its read-only attribute blocks deleting the temporary file.");
        using var stop = new CancellationTokenSource();
        var guard = Task.Run(() =>
        {
            var cancelled = false;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(folder, ".*.tmp"))
                    {
                        File.SetAttributes(file, FileAttributes.ReadOnly);
                        if (!cancelled) { cancelled = true; cancelWhenLocked?.Cancel(); }
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                Thread.Sleep(5);
            }
        });
        try
        {
            try { await write(); }
            catch (Exception error) { return error; }
            throw new AssertFailedException("The write was expected to fail.");
        }
        finally
        {
            await stop.CancelAsync();
            await guard;
        }
    }

    private static void AssertNamed(Exception thrown, string folder)
    {
        // Not wrapped and not flattened on the way from the write to the page: the page gets the typed exception itself.
        var failure = Assert.IsInstanceOfType<TemporaryFileCleanupException>(thrown, thrown.ToString());
        Assert.IsTrue(File.Exists(failure.TemporaryPath), "The temporary file stays, which is what the exception says.");
        Assert.AreEqual(folder, Path.GetDirectoryName(failure.TemporaryPath), "It is beside the destination.");
        foreach (var language in Languages)
        {
            var text = InLanguage(language, () => ErrorText.Explain(failure));
            StringAssert.Contains(text, failure.TemporaryPath, $"{language}: the page names the file that stays");
            Assert.AreNotEqual(InLanguage(language, () => Text.Get("ErrorFile")), text, language);
        }
    }

    private sealed class Folder : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ResonantPulse-LeftoverTests-" + Guid.NewGuid().ToString("N"));

        internal Folder() => Directory.CreateDirectory(Root);

        internal string PathOf(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            // A leftover is read-only: that is how the tests make its removal fail.
            if (!Directory.Exists(Root)) return;
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
