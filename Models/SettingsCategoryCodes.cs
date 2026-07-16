namespace Cafe.Launcher.Avalonia.Models;

public static class SettingsCategoryCodes
{
    public const string General = "general";
    public const string Game = "game";
    public const string DownloadNetwork = "download-network";
    public const string Appearance = "appearance";
    public const string Advanced = "advanced";
    public const string About = "about";

    public static string Normalize(string? code) =>
        code is General or Game or DownloadNetwork or Appearance or Advanced or About
            ? code
            : General;
}
