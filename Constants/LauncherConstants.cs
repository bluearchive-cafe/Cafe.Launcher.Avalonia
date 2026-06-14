using System.Reflection;

namespace Cafe.Launcher.Avalonia.Constants;

public static class LauncherConstants
{
    public const string ProductName = "Cafe Launcher";
    public static readonly string LauncherVersion =
        typeof(LauncherConstants).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "1.0.0";
    public const string YostarAuthorizationVersion = "1.7.2";
    public const string GameTag = "BlueArchive_JP";
    public const string RootFolderName = "YostarGames";
    public const string GameFolderName = "BlueArchive_JP";
    public const string ManifestFileName = "manifest.json";
    public const string GameConfigFileName = "game-launcher-config.json";
    public const string LauncherSettingsFileName = "settings.json";
    public const string ApiBaseUrl = "https://api-launcher-jp.yo-star.com";
    public const string OfficialWebsiteUrl = "https://bluearchive.cafe/";
    public const string GitHubRepositoryUrl = "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release";
    public const string AuthorizationSalt = "DE7108E9B2842FD460F4777702727869";
    public const string UpdateProvider = "generic";
    public const bool UpdateUseMultipleRangeRequest = false;
    public const string UpdatePackageUrl = "https://launcher-pkg-ba-jp.yo-star.com/install_pkg/game_launcher/BlueArchive_JP/";
    public const string UpdaterCacheDirName = "cafe_launcher-updater";
    public const string DefaultThemeColor = "#FF2E7DF6";

    // Keep in sync with .csproj PackageReference for Avalonia
    public const string AvaloniaVersion = "12.0.4";

#if DEBUG
    public const string BuildConfiguration = "Debug";
#else
    public const string BuildConfiguration = "Release";
#endif
}
