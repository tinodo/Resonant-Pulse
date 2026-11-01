using System.Text.Json;
using ResonantPulse.Core;

namespace ResonantPulse.App.Storage;

public sealed class Preferences
{
    public string Language { get; set; } = "";
    public string Theme { get; set; } = "Dark";
    public string OutputId { get; set; } = "";
    public string OutputName { get; set; } = "";
    public bool IsReducedMotion { get; set; }
    /// <summary>No longer used: the app always opens on Play. Kept so that preferences saved by earlier previews still load.</summary>
    public string LastView { get; set; } = "Play";
    public int ExportBars { get; set; } = 16;
    /// <summary>
    /// How many steady taps start the metronome when "Tap to start" is on. Whether it is on is never saved: the metronome starts
    /// stopped. The file is untrusted input, so a value outside 2 to 16 is brought into that range where it is used.
    /// </summary>
    public int TapInCount { get; set; } = TapStartRules.DefaultTaps;
}

public sealed record LibraryItem(string Path, string Name, DateTime? Modified = null);
public sealed record LibraryReadFailure(string Path, Exception Error);
public sealed record LibraryReadResult(IReadOnlyList<LibraryItem> Items, IReadOnlyList<LibraryReadFailure> Failures,
    bool HasMore);

public sealed class LocalWorkspace
{
    internal const int LibraryScanLimit = 1000;
    private readonly SemaphoreSlim _writes = new(1);
    private readonly string _root;

    /// <summary>Optional private data folder (recovery, preferences, library, logs) for isolated test runs.</summary>
    internal const string DataFolderVariable = "RESONANT_PULSE_HOME";

    internal static string DefaultRoot()
    {
        var overridden = Environment.GetEnvironmentVariable(DataFolderVariable);
        return string.IsNullOrWhiteSpace(overridden) ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResonantPulse") : overridden;
    }

    public LocalWorkspace() : this(DefaultRoot()) { }

    internal LocalWorkspace(string root) => _root = Path.GetFullPath(root);
    public string RecoveryPath => Path.Combine(_root, "recovery.pulse");
    public string LibraryPath => Path.Combine(_root, "Library");

    public async Task<Preferences> LoadPreferencesAsync()
    {
        var path = Path.Combine(_root, "preferences.json");
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<Preferences>(await File.ReadAllTextAsync(path))
            ?? throw new InvalidDataException("Empty preference document.");
    }

    private int _preferencesAsked;
    private int _preferencesWritten;

    /// <summary>
    /// True when saving the preferences was asked for and no write has succeeded since, so the file is behind the settings.
    /// A write covers every save asked for before it started (it reads the values once it holds the write lock), so a later
    /// failure cannot hide behind an earlier success, nor an earlier slow write clear a later failure.
    /// </summary>
    public bool PreferencesBehind => Volatile.Read(ref _preferencesAsked) != Volatile.Read(ref _preferencesWritten);

    public async Task SavePreferencesAsync(Preferences preferences)
    {
        Interlocked.Increment(ref _preferencesAsked);
        await _writes.WaitAsync();
        try
        {
            // The values are read once the write lock is held, not when the save was asked for: the file then ends with the
            // latest value of every setting, whatever order several saves were started in and however long one of them took.
            var covered = Volatile.Read(ref _preferencesAsked);
            var json = JsonSerializer.SerializeToUtf8Bytes(preferences);
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, "preferences.json");
            await AtomicFile.WriteAsync(path,
                (stream, token) => stream.WriteAsync(json, token).AsTask(), CancellationToken.None);
            Volatile.Write(ref _preferencesWritten, covered);
        }
        finally { _writes.Release(); }
    }

    public async Task SaveRecoveryAsync(PulseDocument document)
    {
        await _writes.WaitAsync();
        try
        {
            Directory.CreateDirectory(_root);
            await DocumentStore.SaveAsync(RecoveryPath, document);
        }
        finally { _writes.Release(); }
    }

    /// <summary>
    /// Saves a copy of the project to the library under a new name. <paramref name="fileChosen"/> is told the file just before it is
    /// written, so that a write that is stopped by the window closing (or that does not stop) can name it.
    /// </summary>
    public async Task SaveLibraryAsync(PulseDocument document, CancellationToken cancellationToken = default, Action<string>? fileChosen = null)
    {
        Directory.CreateDirectory(LibraryPath);
        var file = Path.Combine(LibraryPath, $"{Guid.NewGuid():N}.pulse");
        fileChosen?.Invoke(file);
        await DocumentStore.SaveAsync(file, document, cancellationToken);
    }

    public async Task<LibraryReadResult> ListAsync()
    {
        string[] paths;
        try
        {
            paths = await Task.Run(() =>
            {
                // Directory enumeration is synchronous; keep it off the UI thread.
                // Retain limit+1 ordinal paths to bound memory and detect overflow.
                var candidates = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var path in Directory.EnumerateFiles(LibraryPath, "*.pulse"))
                {
                    candidates.Add(path);
                    if (candidates.Count > LibraryScanLimit + 1) candidates.Remove(candidates.Max!);
                }
                return candidates.ToArray();
            });
        }
        catch (DirectoryNotFoundException) when (!File.Exists(LibraryPath))
        {
            // A not-yet-created Library is normal on first launch. Permission,
            // I/O and other enumeration failures are intentionally not swallowed.
            return new([], [], false);
        }
        var result = new List<LibraryItem>();
        var failures = new List<LibraryReadFailure>();
        // Only app-owned files, no recursive scans or external attachment fetching.
        foreach (var path in paths.Take(LibraryScanLimit))
        {
            try
            {
                var document = await DocumentStore.LoadAsync(path);
                result.Add(new(path, document.Name, File.GetLastWriteTime(path)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or JsonException or ArgumentException)
            {
                failures.Add(new(path, exception));
            }
        }
        return new(result.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            failures.ToArray(), paths.Length > LibraryScanLimit);
    }

    public static PulseDocument Clone(PulseDocument document) =>
        JsonSerializer.Deserialize<PulseDocument>(JsonSerializer.Serialize(document))
        ?? throw new InvalidDataException("Empty rhythm document.");
}
