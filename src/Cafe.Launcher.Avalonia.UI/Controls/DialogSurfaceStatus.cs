namespace Cafe.Launcher.Avalonia.Controls;

/// <summary>
/// 对话框表面的状态修饰。驱动徽章等强调元素的语调切换；
/// 无状态时徽章使用中性主色，不会给表面附加警示语义。
/// </summary>
public enum DialogSurfaceStatus
{
    None,
    Info,
    Warning,
    Danger,
}
