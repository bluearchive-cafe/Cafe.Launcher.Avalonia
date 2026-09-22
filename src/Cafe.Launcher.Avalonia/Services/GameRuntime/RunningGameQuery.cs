using System.Collections.Generic;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// 「游戏是不是在跑」一次扫描的输入（P0-B）：已知家族名之外再带上安装目录，让 Linux 侧能用
/// <c>/proc/&lt;pid&gt;/maps</c> 认出「映射着安装目录内文件」的进程。这覆盖了运行时标记不在场
/// 的外部启动（官方启动器 / 桌面脚本）。
/// </summary>
public sealed record RunningGameQuery(
    IReadOnlyList<string> KnownExeNames,
    string? InstallDirectory = null);
