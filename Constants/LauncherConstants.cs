namespace Cafe.Launcher.Avalonia.Constants;

/// <summary>
/// Cross-cutting constants used by both the UI and service layers.
/// Domain-specific constants live in <see cref="GamePaths"/>,
/// build metadata in <see cref="BuildInfo"/>,
/// and API/auth configuration in <see cref="ApiConfig"/>.
/// </summary>
public static class LauncherConstants
{
    public const string ProductName = "Cafe Launcher";
    public const string DefaultThemeColor = "#FF2E7DF6";

    /// <summary>
    /// Z-index used by the toast notification overlay (MainWindowToastOverlay.axaml).
    /// Toast renders above all other UI layers: base content, settings, and dialogs.
    /// </summary>
    public const int ZIndexToast = 1000;

    public const string OfficialWebsiteUrl = "https://bluearchive.cafe/";
    public const string GitHubReleaseRepositoryUrl = "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release";
}
