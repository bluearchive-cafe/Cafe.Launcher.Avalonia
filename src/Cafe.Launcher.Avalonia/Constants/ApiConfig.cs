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

    /// <summary>
    /// Value sent as the signed <c>head.version</c> field of every yo-star API request
    /// (see <see cref="Services.LauncherApiClient"/>). The official launcher fills this field
    /// with its own running version, so this constant is how this launcher presents itself as
    /// that official build. It is part of the signed payload, not a decorative field: changing
    /// it changes the signature on the wire. Re-check it against the official launcher's product
    /// version when that launcher is updated, and read a server-side rejection of yo-star
    /// requests as the signal that the field has started being validated.
    /// </summary>
    public const string YostarAuthorizationVersion = "1.7.2";

    public const string GitHubReleaseRepositorySlug = "bluearchive-cafe/Cafe.Launcher.Avalonia_Release";
    public const string GitHubReleaseDownloadPathPrefix =
        "/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/";
    /// <summary>
    /// Full repository URL. Constrained by <see cref="LauncherConstants.GitHubReleaseRepositoryUrl"/>
    /// to the same value. Defined here for API-config cohesion; prefer
    /// <see cref="LauncherConstants.GitHubReleaseRepositoryUrl"/> in non-API code.
    /// </summary>
    public const string GitHubReleaseRepositoryUrl =
        "https://github.com/" + GitHubReleaseRepositorySlug;
    public const string LauncherApiBaseUrl = "https://api-cafe-launcher.saibamidori.com/";
    public const string LauncherReleasesPath = "/api/launcher/releases";
    public const string GitHubReleasesApiUrl =
        "https://api.github.com/repos/" + GitHubReleaseRepositorySlug + "/releases";
}
