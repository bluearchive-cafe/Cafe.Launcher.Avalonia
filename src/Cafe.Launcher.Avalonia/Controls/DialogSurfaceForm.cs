namespace Cafe.Launcher.Avalonia.Controls;

/// <summary>
/// 对话框家族的两种合法形态（ADR-015）：Basic 服务聚焦决策，
/// Panel 服务特性面板。形态决定解剖：Basic 无头带与发丝底带；
/// Panel 具备 56px 头带、固定发丝 footer 与左侧辅助动作槽。
/// </summary>
public enum DialogSurfaceForm
{
    Basic,
    Panel,
}
