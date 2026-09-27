using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// 「当前状态不允许该操作」的呈现。
/// </summary>
/// <remarks>
/// <para>策略判定本身仍在 <see cref="GameOperationPolicy.Decide"/>，每个调用点也仍要自己表态
/// （ADR-027：布尔判定被刻意关在公共面之外，因为「被拒绝意味着什么」随调用点而变）。这里只统一
/// 表态<b>之后怎么写出来</b>——同一条文案与同一个警告级别此前在旅程、展示 VM 与两个服务里各写
/// 一遍。</para>
/// <para>两种出口而不是一种：有界面的路径就地警告用户，无界面的路径（下载/卸载服务）把拒绝表达
/// 成失败结果交给上层渲染。两者的文案必须一致，否则同一次拒绝在两条路径上说法不同。</para>
/// </remarks>
internal static class GameOperationRejections
{
    /// <summary>就地把拒绝报给用户。</summary>
    public static void WarnUnavailable(LocalizationService localizer, ToastService toastService) =>
        toastService.ShowWarning(localizer.T(LocalizationKeys.OperationUnavailableForCurrentState));

    /// <summary>把拒绝表达成操作失败结果，供无界面的调用路径向上返回。</summary>
    public static GameOperationResult UnavailableResult(LocalizationService localizer) =>
        GameOperationOutcomes.Failed(
            localizer.T(LocalizationKeys.OperationUnavailableForCurrentState),
            GameOperationErrorCode.InvalidState);
}
