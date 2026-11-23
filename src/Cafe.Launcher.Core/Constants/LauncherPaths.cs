namespace Cafe.Launcher.Core.Constants;

/// <summary>
/// 本启动器读写路径中的<b>游戏无关</b>文件名：Yostar 官方协议固定的两个安装状态文件，
/// 以及本启动器自有数据根内的文件。
/// </summary>
/// <remarks>
/// 随游戏变化的目录名与可执行名不在这里，见 <see cref="Models.YostarGameProfile"/>。
/// 这个类原先叫 <c>GamePaths</c>，同时装着游戏目录名与 <c>settings.json</c> 一类的启动器文件名——
/// 名字暗示的归属比内容窄，改名是为了让「放哪儿」不再需要判断。
/// </remarks>
public static class LauncherPaths
{
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

    /// <summary>
    /// Copy of the most recent <c>settings.json</c> that could not be read as written
    /// (malformed, wrong root shape, or a field whose type did not match). Kept beside the
    /// live file for diagnosis; overwritten on the next recovery, never read by the launcher.
    /// </summary>
    public const string LauncherSettingsBackupFileName = "settings.json.bak";

    /// <summary>
    /// Original bytes of an unversioned single-game <c>settings.json</c>, written once when the
    /// multi-game format is first committed.
    /// </summary>
    public const string SettingsMigrationBackupFileName = "settings.pre-multigame.json";

    /// <summary>
    /// Original bytes of a former version-2 document whose global background moved into game entries.
    /// </summary>
    public const string SettingsBackgroundMigrationBackupFileName = "settings.pre-backgrounds.json";

    /// <summary>
    /// Original bytes of a document that could not be parsed at all when a write was about to
    /// replace it. Kept so a "my settings were reset" report has the destroyed file to inspect.
    /// </summary>
    public const string CorruptSettingsBackupFileName = "settings.corrupt.json";

    public const string DownloadStateFileName = "download_state.json";

    /// <summary>旧单游戏检查点导入完成标记，避免停止新任务后再次复活旧备份。</summary>
    public const string DownloadMigrationMarkerFileName = "download_state.migrated-v1.json";

    public const string NoticeStateFileName = "shown_notices.json";

    /// <summary>
    /// The unified Serilog log file in the launcher data directory. Rotated
    /// siblings derive their entry names from this stem (unified_001.log …),
    /// so renaming this constant carries the rotation scheme with it.
    /// </summary>
    public const string UnifiedLogFileName = "unified.log";

    /// <summary>
    /// Bounded stdout/stderr capture of the most recently launched game runner
    /// (overwritten per launch). It is a diagnostic artifact, not user state.
    /// </summary>
    public const string RunnerOutputFileName = "runner_output.log";

    /// <summary>Latest compatibility-prefix environment precheck report (overwritten per launch).</summary>
    public const string CompatibilityEnvironmentFileName = "compatibility_environment.json";

    /// <summary>Metadata of the compatibility prefix used by the latest launch (overwritten per launch).</summary>
    public const string PrefixMetadataFileName = "prefix_metadata.json";
}
