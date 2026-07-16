namespace Cafe.Launcher.Avalonia.Constants;

/// <summary>
/// API endpoints, authentication, and GitHub release configuration.
/// </summary>
public static class ApiConfig
{
    public const string ApiBaseUrl = "https://api-launcher-jp.yo-star.com";
    public const string OfficialPackageHost = "launcher-pkg-ba-jp.yo-star.com";
    public const string OfficialPackageBaseUrl = "https://" + OfficialPackageHost;
    public const string ResourcePanelApiBaseUrl = "https://api.bluearchive.cafe";
    public const string AuthorizationSalt = "DE7108E9B2842FD460F4777702727869";
    public const string YostarAuthorizationVersion = "1.7.2";

    public const string GitHubReleaseRepositorySlug = "bluearchive-cafe/Cafe.Launcher.Avalonia_Release";
    /// <summary>
    /// Full repository URL. Constrained by <see cref="LauncherConstants.GitHubReleaseRepositoryUrl"/>
    /// to the same value. Defined here for API-config cohesion; prefer
    /// <see cref="LauncherConstants.GitHubReleaseRepositoryUrl"/> in non-API code.
    /// </summary>
    public const string GitHubReleaseRepositoryUrl =
        "https://github.com/" + GitHubReleaseRepositorySlug;
    public const string LauncherApiBaseUrl = "https://api-cafe-launcher.saibamidori.com/";
    public const string LauncherReleasesPath = "/api/launcher/releases";
}
