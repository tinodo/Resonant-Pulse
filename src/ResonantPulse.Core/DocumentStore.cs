using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResonantPulse.Core;

/// <summary>Strict schema-one JSON persistence. No network paths, implicit migrations, asset loading or automatic playback.</summary>
public static class DocumentStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        MaxDepth = 64,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static async Task SaveAsync(string path, PulseDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DocumentValidator.Validate(document);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > DocumentValidator.MaximumDocumentBytes)
            throw new ArgumentException("Document exceeds the 4 MiB JSON limit. Split the setlist into separate projects.");
        await AtomicFile.WriteAsync(path, async (stream, token) => { await stream.WriteAsync(bytes, token).ConfigureAwait(false); }, cancellationToken).ConfigureAwait(false);
    }
    public static async Task<PulseDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = AtomicFile.LocalPath(path);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > DocumentValidator.MaximumDocumentBytes)
            throw new InvalidDataException("Project must contain 1 byte..4 MiB of schema-one JSON.");
        // Read a bounded stream even if the file changes after its length was checked.
        using var memory = new MemoryStream();
        byte[] buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (memory.Length + read > DocumentValidator.MaximumDocumentBytes) throw new InvalidDataException("Project grew beyond 4 MiB while reading.");
            memory.Write(buffer, 0, read);
        }
        try
        {
            using var json = JsonDocument.Parse(memory.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
            RejectDuplicateProperties(json.RootElement);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("SchemaVersion", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out int schema) || schema != 1)
                throw new InvalidDataException("Project needs an explicit SchemaVersion of 1; legacy/future schemas require an explicit migration.");
            var document = json.Deserialize<PulseDocument>(JsonOptions) ?? throw new InvalidDataException("Project is null.");
            DocumentValidator.Validate(document);
            return document;
        }
        catch (JsonException e) { throw new InvalidDataException($"Invalid project JSON at {e.Path ?? "$"}: {e.Message}", e); }
        catch (ArgumentException e) { throw new InvalidDataException($"Invalid project: {e.Message}", e); }
    }
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate JSON property '{property.Name}'.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }
}

internal static class AtomicFile
{
    internal static string LocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a local file path.", nameof(path));
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) || path.IndexOfAny(['*', '?', '\0']) >= 0)
            throw new ArgumentException("Use a fully qualified local filesystem path, not a network, device, URI or wildcard path.", nameof(path));
        string full = Path.GetFullPath(path);
        string name = Path.GetFileName(full);
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.EndsWith(' ') || name.EndsWith('.') || full.AsSpan(Path.GetPathRoot(full)!.Length).Contains(':'))
            throw new ArgumentException("Choose a valid file name without alternate data streams or trailing dots/spaces.", nameof(path));
        string? directory = Path.GetDirectoryName(full);
        if (directory is null || !Directory.Exists(directory)) throw new DirectoryNotFoundException("The selected local destination directory does not exist.");
        if (Directory.Exists(full)) throw new ArgumentException("The selected path is a directory, not a file.", nameof(path));
        return full;
    }
    internal static async Task WriteAsync(string path, Func<FileStream, CancellationToken, Task> writer, CancellationToken token)
    {
        string full = LocalPath(path);
        string temp = Path.Combine(Path.GetDirectoryName(full)!, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await writer(stream, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            if (File.Exists(full)) File.Replace(temp, full, null);
            else File.Move(temp, full);
        }
        catch (Exception original)
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            { throw new IOException($"Operation failed and temporary file cleanup failed: '{temp}'.", new AggregateException(original, cleanup)); }
            throw;
        }
    }
}
