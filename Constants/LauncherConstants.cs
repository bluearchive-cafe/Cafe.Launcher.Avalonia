using System;
using System.Linq;
using System.Reflection;

namespace Cafe.Launcher.Avalonia.Constants;

public static class LauncherConstants
{
    public const string ProductName = "Cafe Launcher";
    public static readonly string LauncherVersion =
        typeof(LauncherConstants).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "1.0.0";
    public static readonly string CommitSha =
        typeof(LauncherConstants).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "CommitSha")
            ?.Value
        ?? "unknown";
    public const string YostarAuthorizationVersion = "1.7.2";
    public const string GameTag = "BlueArchive_JP";
    public const string RootFolderName = "YostarGames";
    public const string GameFolderName = "BlueArchive_JP";
    public const string ManifestFileName = "manifest.json";
    public const string GameConfigFileName = "game-launcher-config.json";
    public const string LauncherSettingsFileName = "settings.json";
    public const string ApiBaseUrl = "https://api-launcher-jp.yo-star.com";
    public const string ResourcePanelApiBaseUrl = "https://api.bluearchive.cafe";
    public const string OfficialWebsiteUrl = "https://bluearchive.cafe/";
    public const string GitHubReleaseRepositorySlug = "bluearchive-cafe/Cafe.Launcher.Avalonia_Release";
    public const string GitHubReleaseRepositoryUrl = "https://github.com/" + GitHubReleaseRepositorySlug;
    public const string GitHubApiBaseUrl = "https://api.github.com";
    public const string GitHubReleasesPath = "/repos/" + GitHubReleaseRepositorySlug + "/releases?per_page=20";
    public const string AuthorizationSalt = "DE7108E9B2842FD460F4777702727869";
    /// <summary>
    /// Fine-grained GitHub PAT with read-only access to the distribution release repository.
    /// Raises the API rate limit from 60/hr (unauthenticated) to 5,000/hr.
    /// Leave empty to use unauthenticated requests.
    /// Set via CAFE_LAUNCHER_GITHUB_TOKEN environment variable; do NOT hardcode tokens.
    /// </summary>
    public static readonly string GitHubToken =
        Environment.GetEnvironmentVariable("CAFE_LAUNCHER_GITHUB_TOKEN") ?? "";
    public const string DefaultThemeColor = "#FF2E7DF6";

    public const string OldLauncherAppName = "BlueArchive_JP_Gamelauncher";

    // Keep in sync with .csproj PackageReference for Avalonia
    public const string AvaloniaVersion = "12.0.4";

#if DEBUG
    public const string BuildConfiguration = "Debug";
#else
    public const string BuildConfiguration = "Release";
#endif

    /// <summary>
    /// Z-index used by the toast notification overlay (MainWindowToastOverlay.axaml).
    /// Toast renders above all other UI layers: base content, settings, and dialogs.
    /// </summary>
    public const int ZIndexToast = 1000;
}
