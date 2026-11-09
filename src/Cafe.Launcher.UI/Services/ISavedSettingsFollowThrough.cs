using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Services;

/// <summary>
/// 「已保存设置 → 派生效果」的窄接口。设置页只交出刚落盘的值，由实现决定语言、
/// 主题、动效、远端可见性与壳刷新；触发特有的呈现（toast、隐藏向导、打开面板）
/// 留在调用方。
/// </summary>
internal interface ISavedSettingsFollowThrough
{
    /// <summary>
    /// 应用必须先于调用方反馈生效的效果，并返回 <see cref="CompleteAsync"/> 需要的交接单。
    /// </summary>
    SavedSettingsHandoff Apply(LauncherSettings savedSettings);

    /// <summary>
    /// 完成跟进：刷新宿主状态（下载进行中改为校正待用快照），并在下载源变化时提示修复。
    /// </summary>
    Task CompleteAsync(SavedSettingsHandoff handoff);
}

/// <summary>
/// 一次保存的交接单：落盘值与保存前壳持有的下载源。对设置功能不透明。
/// </summary>
internal sealed record SavedSettingsHandoff(
    LauncherSettings SavedSettings,
    string? PreviousPatchUrlGroup);
