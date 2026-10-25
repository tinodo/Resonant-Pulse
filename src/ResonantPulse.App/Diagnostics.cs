namespace ResonantPulse.App;

internal static class Diagnostics
{
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ResonantPulse", "Logs", "app.log");

    public static void Write(string message, Exception? exception = null)
    {
        var entry = $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}{exception}";
        System.Diagnostics.Debug.WriteLine(entry);
        Console.Error.WriteLine(entry);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            // Local diagnostics only. Never uploaded automatically.
            File.AppendAllText(LogPath, entry + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
