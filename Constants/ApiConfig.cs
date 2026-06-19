namespace Cafe.Launcher.Avalonia.Constants;

/// <summary>
/// API endpoints, authentication, and GitHub release configuration.
/// </summary>
public static class ApiConfig
{
    public const string ApiBaseUrl = "https://api-launcher-jp.yo-star.com";
    public const string ResourcePanelApiBaseUrl = "https://api.bluearchive.cafe";
    public const string AuthorizationSalt = "DE7108E9B2842FD460F4777702727869";
    public const string YostarAuthorizationVersion = "1.7.2";

    public const string GitHubReleaseRepositorySlug = "bluearchive-cafe/Cafe.Launcher.Avalonia_Release";
    public const string GitHubReleaseRepositoryUrl = "https://github.com/" + GitHubReleaseRepositorySlug;
    public const string GitHubApiBaseUrl = "https://api.github.com";
    public const string GitHubReleasesPath = "/repos/" + GitHubReleaseRepositorySlug + "/releases?per_page=20";
}
