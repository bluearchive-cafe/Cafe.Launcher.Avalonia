using System;
using System.Collections.Generic;
using System.Linq;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// 判定依据，供诊断与测试分辨「为什么认了」。强信号（标记 / prefix / 映射）回答的是「归不归这次
/// 游戏会话」，弱信号（截断的 comm）只是与 Windows 判据兼容的回退。
/// </summary>
internal enum UnixProcessMatchSignal
{
    /// <summary>进程环境含启动器写入的所有权标记，值等于本次游戏 id。</summary>
    OwnershipMarker,

    /// <summary>进程环境的 <c>WINEPREFIX</c> 等于启动器为该游戏选择的兼容前缀。</summary>
    WinePrefix,

    /// <summary>进程映射了安装目录下的文件（ADR-032 留的门，Linux 上可重开）。</summary>
    MappedInstallFile,

    /// <summary>comm 落在名字家族内（弱：内核截断到 15 字符，且依赖命名约定）。</summary>
    TruncatedNameFamily,
}

internal sealed record UnixProcessMatch(string DisplayName, UnixProcessMatchSignal Signal);

/// <summary>
/// 这次扫描要回答的问题（Linux）：已知名字家族之外，再带上启动器自己掌握的两个归属键——写进游戏
/// 进程环境的所有权标记值（gameId）与为该游戏选择的 <c>WINEPREFIX</c>；安装目录用于只认「占着待删
/// 目录」的映射。名字只作回退。
/// </summary>
internal sealed record UnixGameProcessQuery(
    IReadOnlyList<string> KnownNames,
    string? GameId,
    string? PrefixPath,
    string? InstallDirectory);

/// <summary>
/// Linux 上「这条进程记录归不归游戏」的纯判定（设计稿 §3 信号阶梯）。它只吃
/// <see cref="UnixProcessRecord"/>，不碰 <c>/proc</c>，因此可以用合成记录做表驱动测试；平台读取层
/// （薄、按 <see cref="OperatingSystem.IsLinux"/> 门控）留待接线时补。
/// </summary>
internal static class UnixGameProcessMatcher
{
    /// <summary>
    /// 启动器写进游戏进程环境的归属标记名。用稳定的 gameId 而不是每次随机会话令牌，因为闸门要能跨
    /// 「启动器重启」认出上一次启动的游戏；标记由启动路径在 <c>GameRuntime.BuildStartInfo</c> 里补写
    /// （接线时落地，见设计稿 §8）。
    /// </summary>
    internal const string OwnershipMarkerKey = "CAFE_LAUNCHER_GAME_ID";

    private const string WinePrefixKey = "WINEPREFIX";

    /// <summary>
    /// 命中返回带依据的匹配，未命中返回 null。顺序即信号强弱：先认归属，再认映射，最后才用截断的
    /// comm 回退。任何一条信号都不命中时返回 null，由调用方保持 fail-open（见设计稿 §4）。
    /// </summary>
    public static UnixProcessMatch? Match(UnixProcessRecord record, UnixGameProcessQuery query)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(query);

        if (HasOwnershipMarker(record, query))
        {
            return new UnixProcessMatch(DisplayName(record, query), UnixProcessMatchSignal.OwnershipMarker);
        }

        if (HasWinePrefix(record, query))
        {
            return new UnixProcessMatch(DisplayName(record, query), UnixProcessMatchSignal.WinePrefix);
        }

        if (HasMappedInstallFile(record, query))
        {
            return new UnixProcessMatch(DisplayName(record, query), UnixProcessMatchSignal.MappedInstallFile);
        }

        if (GameProcessNames.BelongsToFamily(record.Comm, query.KnownNames))
        {
            return new UnixProcessMatch(
                GameProcessNames.WithoutExtension(record.Comm),
                UnixProcessMatchSignal.TruncatedNameFamily);
        }

        return null;
    }

    private static bool HasOwnershipMarker(UnixProcessRecord record, UnixGameProcessQuery query) =>
        record.Environment.TryGetValue(OwnershipMarkerKey, out var marker)
        && marker.Length > 0
        && (string.IsNullOrWhiteSpace(query.GameId)
            || string.Equals(marker, query.GameId, StringComparison.Ordinal));

    private static bool HasWinePrefix(UnixProcessRecord record, UnixGameProcessQuery query) =>
        !string.IsNullOrWhiteSpace(query.PrefixPath)
        && record.Environment.TryGetValue(WinePrefixKey, out var prefix)
        && PathEquals(prefix, query.PrefixPath);

    private static bool HasMappedInstallFile(UnixProcessRecord record, UnixGameProcessQuery query) =>
        !string.IsNullOrWhiteSpace(query.InstallDirectory)
        && record.MappedFiles.Any(file => IsUnder(file, query.InstallDirectory));

    /// <summary>
    /// 给用户看进程名时优先用不截断的来源：argv 里的游戏可执行文件名 → maps 里安装目录下的文件名
    /// → comm。返回值仍是不含扩展名的进程名，最终由 <see cref="GameProcessNames.DescribeForDisplay"/>
    /// 补 <c>.exe</c>。
    /// </summary>
    private static string DisplayName(UnixProcessRecord record, UnixGameProcessQuery query)
    {
        foreach (var argument in record.Arguments)
        {
            if (GameProcessNames.BelongsToFamily(argument, query.KnownNames))
            {
                return GameProcessNames.WithoutExtension(argument);
            }
        }

        if (!string.IsNullOrWhiteSpace(query.InstallDirectory))
        {
            var mapped = record.MappedFiles.FirstOrDefault(file => IsUnder(file, query.InstallDirectory));
            if (mapped is not null)
            {
                return GameProcessNames.WithoutExtension(mapped);
            }
        }

        return GameProcessNames.WithoutExtension(record.Comm);
    }

    /// <summary>
    /// prefix 比较按 Unix 约定：区分大小写，只归一末尾的 <c>/</c>（启动器写入 env 的值与实际值在同一
    /// 台机器上，差异只可能是末尾斜杠）。不做解析、不做 realpath——一旦放宽成前缀比较就会把相邻目录
    /// 也认进来。
    /// </summary>
    private static bool PathEquals(string candidate, string expected) =>
        string.Equals(TrimTrailingSlash(candidate), TrimTrailingSlash(expected), StringComparison.Ordinal);

    private static string TrimTrailingSlash(string path) =>
        path.Length > 1 ? path.TrimEnd(UnixSeparator) : path;

    /// <summary>映射文件是否落在安装目录下（要求分隔符边界，避免 <c>BlueArchiveBackup</c> 被 <c>BlueArchive</c> 认领）。</summary>
    private static bool IsUnder(string file, string directory)
    {
        var root = TrimTrailingSlash(directory);
        return file.Length > root.Length
            && file.StartsWith(root, StringComparison.Ordinal)
            && file[root.Length] == UnixSeparator;
    }

    private const char UnixSeparator = '/';
}
