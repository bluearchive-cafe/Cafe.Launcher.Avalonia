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

    /// <summary>
    /// The start script shipped in the game folder. Running the game executable
    /// directly does not start the game — the entry the Yostar distribution itself
    /// uses is this script, so out-of-launcher start points (desktop shortcuts)
    /// must target it with the game folder as the working directory.
    /// </summary>
    public const string GameStartScriptFileName = "run.bat";

    public const string ManifestFileName = "manifest.json";
    public const string GameConfigFileName = "game-launcher-config.json";

    /// <summary>
    /// The two launcher-managed installation state files (<see cref="ManifestFileName"/>
    /// and <see cref="GameConfigFileName"/>) that uninstall removes in addition to the
    /// manifest-listed files. Affected-file counts are produced and consumed as
    /// manifest count plus this constant, so the round-trip between them never
    /// relies on a bare literal (D7).
    /// </summary>
    public const int InstallationStateFileCount = 2;

    public const string LauncherSettingsFileName = "settings.json";
    public const string DownloadStateFileName = "download_state.json";
    public const string NoticeStateFileName = "shown_notices.json";

    /// <summary>
    /// The unified Serilog log file in the launcher data directory. Rotated
    /// siblings derive their entry names from this stem (unified_001.log …),
    /// so renaming this constant carries the rotation scheme with it.
    /// </summary>
    public const string UnifiedLogFileName = "unified.log";
}
