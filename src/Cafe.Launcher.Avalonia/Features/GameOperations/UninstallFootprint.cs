namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// 彻底清除会删除的两个目录的实测大小（ADR-030），单位字节；展示层用
/// <c>FileSizeFormatter.Format</c> 格式化。测量与删除共用同一段目标计算，所以对话框里
/// 显示多少就是随后会删多少。
/// </summary>
public readonly record struct UninstallFootprint(long InstallDirectoryBytes, long PrefixBytes);
