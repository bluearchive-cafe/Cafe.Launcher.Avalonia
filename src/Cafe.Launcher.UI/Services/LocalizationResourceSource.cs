namespace Cafe.Launcher.UI.Services;

/// <summary>
/// Resolves one localized string for an effective launcher language. Injected so each
/// service instance owns its resources; tests no longer install a process-wide table.
/// </summary>
internal interface ILocalizationResourceSource
{
    /// <summary>Returns the text for <paramref name="key"/>, or null when the language has no entry.</summary>
    string? GetString(string effectiveLanguage, string key);
}

/// <summary>Reads the embedded LauncherStrings resources through the generated manager.</summary>
internal sealed class ResourceManagerLocalizationSource : ILocalizationResourceSource
{
    public string? GetString(string effectiveLanguage, string key) =>
        Resources.LauncherStrings.ResourceManager.GetString(
            key, LauncherCultureResolver.GetCultureFor(effectiveLanguage));
}
