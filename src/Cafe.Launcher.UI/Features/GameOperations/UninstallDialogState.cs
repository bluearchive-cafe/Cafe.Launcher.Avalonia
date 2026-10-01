namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>卸载专用表面的阶段；结果保持可见直到用户关闭。</summary>
internal enum UninstallDialogState
{
    Confirmation,
    Scanning,
    Deleting,
    Cleanup,
    Completed,
    Failed
}
