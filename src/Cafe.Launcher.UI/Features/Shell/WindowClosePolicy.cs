using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Features.Shell;

/// <summary>关闭意图在窗口层落到的动作。</summary>
internal enum WindowCloseAction
{
    Ignore,
    HideToTray,
    MinimizeToTaskbar,
    Shutdown,
}

/// <summary>
/// 已保存关闭行为、卸载门禁与托盘可用性到窗口动作的纯映射。窗口只负责执行动作，
/// 判定可以在没有 Avalonia 宿主的单元测试里逐格验证。
/// </summary>
internal static class WindowClosePolicy
{
    /// <summary>
    /// 关闭按钮路径的判定：卸载执行中一律忽略；关闭行为为「最小化到托盘」时落到托盘或任务栏；
    /// 其余（退出）走关闭流程。
    /// </summary>
    public static WindowCloseAction Decide(
        string closeBehavior,
        bool isUninstallExecuting,
        bool hasTray)
    {
        if (IsCloseRefused(isUninstallExecuting))
        {
            return WindowCloseAction.Ignore;
        }

        return closeBehavior == CloseBehaviors.Minimize
            ? DecideMinimize(hasTray)
            : WindowCloseAction.Shutdown;
    }

    /// <summary>
    /// 卸载执行期间任何关闭入口都拒绝。自绘关闭（<see cref="Decide"/>）与操作系统发起的关闭
    /// 共用这一个闸门；关闭行为本身不解释系统关闭（CONTEXT「关闭行为」）。
    /// </summary>
    public static bool IsCloseRefused(bool isUninstallExecuting) => isUninstallExecuting;

    /// <summary>「最小化到托盘」类动作的落点：有托盘就隐藏，没有就退到任务栏。</summary>
    public static WindowCloseAction DecideMinimize(bool hasTray) =>
        hasTray ? WindowCloseAction.HideToTray : WindowCloseAction.MinimizeToTaskbar;
}
