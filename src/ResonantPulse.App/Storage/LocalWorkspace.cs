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

public sealed class LocalWorkspace
{
    private readonly SemaphoreSlim _writes = new(1);
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResonantPulse");
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
        var json = JsonSerializer.Serialize(preferences);
        await _writes.WaitAsync();
        try
        {
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, "preferences.json");
            await File.WriteAllTextAsync(path + ".tmp", json);
            File.Move(path + ".tmp", path, true);
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

    public async Task<IReadOnlyList<LibraryItem>> ListAsync()
    {
        if (!Directory.Exists(LibraryPath)) return [];
        var result = new List<LibraryItem>();
        // Only app-owned files, no recursive scans or external attachment fetching.
        foreach (var path in Directory.EnumerateFiles(LibraryPath, "*.pulse").Take(1000))
        {
            var document = await DocumentStore.LoadAsync(path);
            result.Add(new(path, document.Name));
        }
        return result.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static PulseDocument Clone(PulseDocument document) =>
        JsonSerializer.Deserialize<PulseDocument>(JsonSerializer.Serialize(document))
        ?? throw new InvalidDataException("Empty rhythm document.");
}
