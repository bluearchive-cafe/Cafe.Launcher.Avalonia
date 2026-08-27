namespace Cafe.Launcher.Avalonia.Constants;

/// <summary>
/// Game file structure, folder names, and identifiers shared across
/// download, install, and local-state services.
/// </summary>
public static class GamePaths
{
    public const string GameTag = "BlueArchive_JP";
    public const string RootFolderName = "YostarGames";
    public const string GameFolderName = "BlueArchive_JP";

    /// <summary>
    /// The actual game client executable inside the game folder. The configured
    /// start entry (<c>game-launcher-config.json</c> "name") is Yostar's loader
    /// wrapper (xldr_*_loader.exe) without a game icon, so surfaces that present
    /// the game itself (desktop shortcuts) should take their icon from this file.
    /// </summary>
    public const string GameExecutableFileName = "BlueArchive.exe";

    public const string ManifestFileName = "manifest.json";
    public const string GameConfigFileName = "game-launcher-config.json";
    public const string LauncherSettingsFileName = "settings.json";
    public const string DownloadStateFileName = "download_state.json";
}
