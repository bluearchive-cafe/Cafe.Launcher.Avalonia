namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// 卸载范围（ADR-030）：默认只删清单内文件与两个状态文件，调用方可以要求连整个安装目录
/// 与受管的兼容 Prefix 子树一起删。词汇按 ADR-026 的先例留在游戏操作域内，由域翻译一次。
/// </summary>
public enum UninstallScope
{
    /// <summary>只删清单内文件——与官方启动器一致的那条路径。</summary>
    ManifestFilesOnly,

    /// <summary>彻底清除：整个安装目录树，加上本启动器托管的兼容 Prefix 子树。</summary>
    ThoroughCleanup
}
