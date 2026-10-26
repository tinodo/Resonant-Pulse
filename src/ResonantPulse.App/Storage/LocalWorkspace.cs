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
    public string LastView { get; set; } = "Play";
    public int ExportBars { get; set; } = 16;
}

public sealed record LibraryItem(string Path, string Name);
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

    public async Task SavePreferencesAsync(Preferences preferences)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(preferences);
        await _writes.WaitAsync();
        try
        {
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, "preferences.json");
            await AtomicFile.WriteAsync(path,
                (stream, token) => stream.WriteAsync(json, token).AsTask(), CancellationToken.None);
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

    public async Task SaveLibraryAsync(PulseDocument document)
    {
        Directory.CreateDirectory(LibraryPath);
        await DocumentStore.SaveAsync(Path.Combine(LibraryPath, $"{Guid.NewGuid():N}.pulse"), document);
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
                result.Add(new(path, document.Name));
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
