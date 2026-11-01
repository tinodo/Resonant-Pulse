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

    [TestMethod]
    public void TheSentenceSaysWhatHappenedWhatIsUnchangedAndEndsWithThePathOfTheFile()
    {
        var parts = new Dictionary<string, string[]>
        {
            ["en"] = ["could not be saved", "temporary file", "could not be removed", "already there", "has not been changed", "delete the temporary file yourself"],
            ["nl"] = ["niet worden bewaard", "tijdelijk bestand", "niet worden verwijderd", "er al stond", "niet gewijzigd", "zelf verwijderen"],
            ["de"] = ["nicht gespeichert", "temporäre Datei", "nicht entfernt", "vorhandene Datei", "nicht verändert", "selbst löschen"],
            ["fr"] = ["Impossible d’enregistrer le fichier", "fichier temporaire", "pas pu être supprimé", "déjà présent", "pas été modifié", "supprimer vous-même"],
            ["es"] = ["No se pudo guardar el archivo", "archivo temporal", "no se pudo eliminar", "ya existía", "no se ha modificado", "eliminar usted mismo"]
        };
        foreach (var language in Languages)
        {
            var sentence = InLanguage(language, () => Text.Get("ErrorFileLeftover"));
            foreach (var part in parts[language]) StringAssert.Contains(sentence, part, language);
            // The path comes last, with nothing after it that could be read as part of the path, and there is only one place for it.
            Assert.IsTrue(sentence.EndsWith("{0}", StringComparison.Ordinal), $"{language}: the path is the last thing in the sentence: '{sentence}'");
            Assert.AreEqual(sentence.IndexOf("{0}", StringComparison.Ordinal), sentence.LastIndexOf("{0}", StringComparison.Ordinal), language);
            Assert.IsTrue(sentence.Length <= 300, $"{language}: {sentence.Length} characters is too long for the notice bar");
        }
        // A colon is kept with the word before it by a no-break space in French.
        StringAssert.Contains(InLanguage("fr", () => Text.Get("ErrorFileLeftover")), "temporaire\u00A0: {0}");
    }

    [TestMethod]
    public void AWavWhoseOwnStagingWriteLeftItsTemporaryFileAsWellNamesThatFileAmongTheOthers()
    {
        const string wav = @"C:\Music\export.wav";
        const string manifest = wav + ".manifest.json";
        string[] candidates = [@"C:\Music\.wave-export-a.wav.tmp", @"C:\Music\.wave-export-a.json.tmp", @"C:\Music\.wave-export-a.wav.bak", @"C:\Music\.wave-export-a.json.bak"];
        const string stays = @"C:\Music\..wave-export-a.json.tmp.0123456789abcdef0123456789abcdef.tmp";
        // What WaveExporter throws when the write of a staging file failed (the first cause) and removing a staging file failed too.
        var failure = new WaveExportException(WaveExportFailure.FailedCleanup, wav, manifest, candidates,
            "WAV export did not complete and temporary/backup cleanup also failed.",
            new AggregateException(CleanupFailed(stays), new IOException($"Could not remove export temporary/backup file '{candidates[0]}'.", new UnauthorizedAccessException())));
        foreach (var language in Languages)
        {
            var text = InLanguage(language, () => ErrorText.Explain(failure));
            foreach (var path in candidates.Append(stays).Append(wav).Append(manifest)) StringAssert.Contains(text, path, language);
            Assert.IsTrue(text.StartsWith(InLanguage(language, () => Text.Get("ErrorWaveFailedCleanup")), StringComparison.Ordinal), language);
            // A file that is among the candidates already is listed once.
            var alreadyListed = new WaveExportException(WaveExportFailure.FailedCleanup, wav, manifest, [.. candidates, stays], "x", new AggregateException(CleanupFailed(stays)));
            var listed = InLanguage(language, () => ErrorText.Explain(alreadyListed));
            Assert.AreEqual(listed.IndexOf(stays, StringComparison.Ordinal), listed.LastIndexOf(stays, StringComparison.Ordinal), language);
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
