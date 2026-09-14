using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// 策略判定的结果。刻意不是 <see cref="bool"/>：调用方必须对「被拒绝」这一支表态，
/// 于是「拒绝之后什么都不做」只能是有意写出来的，而不是漏掉了 else。
/// </summary>
public enum GameOperationDecision
{
    /// <summary>当前状态下允许执行该操作。</summary>
    Allowed,

    /// <summary>当前状态下不允许执行该操作；调用方渲染（提示、失败结果或中止）。</summary>
    RejectedForCurrentState
}

/// <summary>
/// 安装生命周期语境下「哪种状态允许哪种操作」的唯一策略表。
/// 表示层预检（跳过哪个提示）、journey 路径闸与服务的执行边界都读这里；
/// 拒绝时「配哪句话」的文案职责留在各调用层——但「是否被拒绝」只有这一个来源，
/// 且用户已在确认框上点过确认之后仍被拒绝时，必须给出可见反馈（见 ADR-027）。
/// 新增 LauncherRuntimeState 或操作种类时，GameOperationPolicyTests 的全表测试
/// 会强制显式表态。
/// </summary>
public static class GameOperationPolicy
{
    /// <summary>策略自有的操作轴：与承载进度语义的 GameOperationKind（Idle/Download/Repair/Uninstall）刻意分开——它没有 Launch，权限轴也不该波及 UI 进度 switch。</summary>
    public enum Operation
    {
        Launch,
        InstallOrUpdate,
        Repair,
        Uninstall
    }

    /// <summary>
    /// 判定给定安装状态下是否允许执行该操作。对照 CONTEXT.md 安装生命周期
    /// 词汇：可启动才允许启动与卸载；损坏或可启动允许修复；未安装、
    /// 需要强制更新、有可用更新允许安装/更新。
    /// </summary>
    public static GameOperationDecision Decide(Operation operation, LauncherRuntimeState state) =>
        Allows(operation, state) ? GameOperationDecision.Allowed : GameOperationDecision.RejectedForCurrentState;

    private static bool Allows(Operation operation, LauncherRuntimeState state) => operation switch
    {
        Operation.Launch => state == LauncherRuntimeState.Ready,
        Operation.InstallOrUpdate => state is LauncherRuntimeState.NotInstalled
            or LauncherRuntimeState.BelowLowestVersion
            or LauncherRuntimeState.UpdateAvailable,
        Operation.Repair => state is LauncherRuntimeState.Corrupted or LauncherRuntimeState.Ready,
        Operation.Uninstall => state == LauncherRuntimeState.Ready,
        _ => false
    };
}
