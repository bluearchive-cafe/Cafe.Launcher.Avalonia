namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>
/// 卸载始终删除整个游戏目录；额外选择决定是否删除受管兼容环境（ADR-046）。
/// </summary>
public enum UninstallScope
{
    /// <summary>默认删除整个游戏目录，包括清单外资源、状态文件和用户放入的文件。</summary>
    GameDirectory,

    /// <summary>删除整个游戏目录，并额外清除本启动器托管的兼容 Prefix 子树。</summary>
    GameDirectoryAndManagedCompatibility
}
