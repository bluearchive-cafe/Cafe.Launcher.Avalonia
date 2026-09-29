namespace Cafe.Launcher.Core.Constants;

/// <summary>
/// 跨层共用、且与具体游戏和产品都无关的常量。
/// </summary>
/// <remarks>
/// 游戏身份在 <see cref="Models.YostarGameProfile"/> 里，产品身份（产品名、发行仓库、对外链接）
/// 在 <see cref="Models.LauncherProductProfile"/> 里，两者由 <see cref="LauncherProfiles"/> 声明。
/// 这个类只留「拿到哪款游戏、哪个产品都成立」的值。
/// </remarks>
public static class LauncherConstants
{
    public const string LogExportFolderName = "log-exports";
    public const string DefaultThemeColor = "#FF2E7DF6";

    /// <summary>
    /// Z-index used by the toast notification overlay (MainWindowToastOverlay.axaml).
    /// Toast renders above all other UI layers: base content, settings, and dialogs.
    /// </summary>
    public const int ZIndexToast = 1000;

    /// <summary>第二实例转发「直接启动游戏」的 CLI 参数（宿主与快捷方式生成共用一处字面量）。</summary>
    public const string LaunchGameArgument = "--launch-game";
}
