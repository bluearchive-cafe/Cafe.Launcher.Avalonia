namespace Cafe.Launcher.UI.Models;

internal static class SettingsCategoryCodes
{
    public const string General = "general";
    public const string Game = "game";
    public const string GameLaunch = "game-launch";
    public const string GameDownload = "game-download";
    public const string GameBackground = "game-background";
    public const string DownloadNetwork = "download-network";
    public const string DefaultBackground = "default-background";
    public const string Appearance = "appearance";
    public const string Advanced = "advanced";
    public const string About = "about";

    public static string Normalize(string? code) =>
        code is General or Game or GameLaunch or GameDownload or GameBackground
            or DownloadNetwork or DefaultBackground or Appearance or Advanced or About
            ? code
            : General;
}
