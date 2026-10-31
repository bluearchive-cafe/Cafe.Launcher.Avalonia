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

    /// <summary>设置覆盖层的层级；位于主内容之上、对话框之下。</summary>
    public const int ZIndexSettings = 100;

    /// <summary>对话框覆盖层的层级；设置之上、向导之下。</summary>
    public const int ZIndexDialog = 200;

    /// <summary>设置向导覆盖层的层级；对话框之上、Toast 之下。</summary>
    public const int ZIndexSetupWizard = 500;

    /// <summary>向导自身的退出确认；必须高于向导本体，仍在 Toast 之下。</summary>
    public const int ZIndexSetupWizardConfirmation = 501;

    /// <summary>
    /// Z-index used by the toast notification overlay (MainWindow.axaml).
    /// Toast renders above all other UI layers: base content, settings, and dialogs.
    /// </summary>
    public const int ZIndexToast = 1000;

    /// <summary>第二实例转发「直接启动游戏」的 CLI 参数（宿主与快捷方式生成共用一处字面量）。</summary>
    public const string LaunchGameArgument = "--launch-game";
}
