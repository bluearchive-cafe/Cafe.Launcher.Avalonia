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
    public const string DownloadStateFileName = "download_state.json";
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
