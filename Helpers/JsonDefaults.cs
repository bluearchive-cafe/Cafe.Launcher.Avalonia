using System.Text.Json;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> instances to avoid duplication across services.
/// </summary>
public static class JsonDefaults
{
    /// <summary>
    /// Indented JSON for human-readable file storage
    /// (settings, download state, installation state).
    /// <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> defaults to <see langword="false"/>.
    /// </summary>
    public static JsonSerializerOptions Indented { get; } = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Case-sensitive JSON for machine-to-machine communication
    /// (API responses, internal round-trips).
    /// </summary>
    public static JsonSerializerOptions Strict { get; } = new()
    {
        PropertyNameCaseInsensitive = false
    };
}
