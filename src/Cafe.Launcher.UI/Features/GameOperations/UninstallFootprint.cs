namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>
/// 游戏目录与可选受管兼容环境的实测大小（ADR-046），单位字节；展示层用
/// <c>FileSizeFormatter.Format</c> 格式化。测量与删除共用同一段目标计算，所以对话框里
/// 显示的是同一组目标；目录内容在测量后变化时，执行结果仍以实际删除为准。
/// </summary>
internal readonly record struct UninstallFootprint(long InstallDirectoryBytes, long PrefixBytes);
