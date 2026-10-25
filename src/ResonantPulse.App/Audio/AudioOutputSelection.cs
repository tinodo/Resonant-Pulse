namespace ResonantPulse.App.Audio;

/// <summary>An available Windows audio rendering endpoint.</summary>
/// <param name="Id">The endpoint identifier supplied by Windows.</param>
/// <param name="Name">The endpoint's display name.</param>
public sealed record OutputDevice(string Id, string Name)
{
    /// <summary>Returns the endpoint's display name.</summary>
    public override string ToString() => Name;
}

internal static class AudioOutputSelection
{
    internal static OutputDevice? Resolve(string savedId, string defaultId,
        IReadOnlyList<OutputDevice> available)
    {
        // First use follows Windows. A saved endpoint is pinned: its loss must
        // never send an existing session to a different speaker automatically.
        var requested = string.IsNullOrWhiteSpace(savedId) ? defaultId : savedId;
        return string.IsNullOrWhiteSpace(requested) ? null :
            available.FirstOrDefault(device =>
                string.Equals(device.Id, requested, StringComparison.OrdinalIgnoreCase));
    }
}
