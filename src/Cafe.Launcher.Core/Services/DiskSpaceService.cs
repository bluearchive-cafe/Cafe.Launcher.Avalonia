using System;
using System.Collections.Generic;
using System.IO;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.Core.Services;

internal sealed class DiskSpaceService : IDiskSpaceService
{
    private readonly record struct CacheEntry(long AvailableBytes, long Timestamp);

    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object cacheLock = new();

    internal Func<string, long?>? GetAvailableBytesOverride { get; set; }

    public long? GetAvailableBytes(string path)
    {
        if (GetAvailableBytesOverride is not null)
        {
            return GetAvailableBytesOverride(path);
        }

        var existingPath = FindExistingDirectory(path);
        if (string.IsNullOrWhiteSpace(existingPath))
        {
            return null;
        }

        var root = ResolveVolumeRoot(existingPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var now = Environment.TickCount64;
        lock (cacheLock)
        {
            if (_cache.TryGetValue(root, out var entry) && now - entry.Timestamp < CacheDurationMs)
            {
                return entry.AvailableBytes;
            }
        }

        try
        {
            // The official launcher reads Win32_LogicalDisk.FreeSpace.  TotalFreeSpace
            // has the same volume-wide meaning; AvailableFreeSpace can instead be
            // restricted by a per-user disk quota.
            var available = new DriveInfo(root).TotalFreeSpace;
            lock (cacheLock)
            {
                _cache[root] = new CacheEntry(available, now);
            }

            return available;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private const long CacheDurationMs = 30_000;

    public bool HasEnoughSpace(string path, long requiredBytes)
    {
        if (requiredBytes <= 0)
        {
            return true;
        }

        return Check(path, requiredBytes).HasEnoughSpace;
    }

    public DiskSpaceCheckResult Check(string path, long requiredBytes)
    {
        var normalizedRequiredBytes = Math.Max(0, requiredBytes);
        var availableBytes = GetAvailableBytes(path);
        return new DiskSpaceCheckResult(normalizedRequiredBytes, availableBytes);
    }

    /// <summary>
    /// 新装时需要为解压峰值预留的空间：已知解压体积时取「计划下载量」与它之中的较大者。
    /// 与其它成员一样按接口隐式实现——本类已 internal，显式接口实现只会多出一层转发。
    /// </summary>
    public long ResolveRequiredBytes(bool isFreshInstall, long plannedDownloadBytes, string? decompressionSize)
    {
        var normalizedPlannedDownloadBytes = Math.Max(0, plannedDownloadBytes);
        if (!isFreshInstall
            || string.IsNullOrWhiteSpace(decompressionSize)
            || !FileSizeFormatter.TryParseHumanReadable(decompressionSize, out var decompressionBytes))
        {
            return normalizedPlannedDownloadBytes;
        }

        return Math.Max(normalizedPlannedDownloadBytes, decompressionBytes);
    }

    private static string? FindExistingDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string current;
        try
        {
            current = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        while (!string.IsNullOrWhiteSpace(current))
        {
            try
            {
                if (Directory.Exists(current))
                {
                    return current;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }

            var parent = Directory.GetParent(current);
            if (parent is null || string.Equals(parent.FullName, current, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            current = parent.FullName;
        }

        return null;
    }

    /// <summary>
    /// 把已存在的目录解析到它所在的卷根，同时作为缓存键：Windows 保持盘符语义；
    /// Unix 上任意绝对路径的根都是 <c>/</c>，改为在已挂载卷中取最长前缀，
    /// 取不到时才退回路径根。
    /// </summary>
    internal static string? ResolveVolumeRoot(string existingPath)
    {
        var pathRoot = Path.GetPathRoot(existingPath);
        if (string.IsNullOrWhiteSpace(pathRoot) || OperatingSystem.IsWindows())
        {
            return pathRoot;
        }

        return SelectLongestVolumeRoot(existingPath, GetMountedVolumeRoots()) ?? pathRoot;
    }

    /// <summary>
    /// 选出包含目标路径的最长卷根。按路径段边界比较，避免 <c>/mnt</c> 误配 <c>/mnt2</c>；
    /// 候选根末尾的分隔符先裁掉，返回规范化后的根。没有卷包含目标路径时返回 <c>null</c>，
    /// 由调用方决定回退。
    /// </summary>
    internal static string? SelectLongestVolumeRoot(string path, IReadOnlyList<string> volumeRoots)
    {
        string? selected = null;
        var selectedLength = -1;

        foreach (var candidate in volumeRoots)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var normalized = Path.TrimEndingDirectorySeparator(candidate);
            if (normalized.Length <= selectedLength || !IsWithinVolumeRoot(path, normalized))
            {
                continue;
            }

            selected = normalized;
            selectedLength = normalized.Length;
        }

        return selected;
    }

    private static bool IsWithinVolumeRoot(string path, string volumeRoot)
    {
        if (volumeRoot.Length == 0 || !path.StartsWith(volumeRoot, StringComparison.Ordinal))
        {
            return false;
        }

        if (path.Length == volumeRoot.Length)
        {
            return true;
        }

        // 以分隔符结尾的根（<c>/</c>）本身就是段边界，任何以它开头的绝对路径都属于该卷。
        return IsDirectorySeparator(volumeRoot[^1]) || IsDirectorySeparator(path[volumeRoot.Length]);
    }

    private static bool IsDirectorySeparator(char value) =>
        value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;

    private static IReadOnlyList<string> GetMountedVolumeRoots()
    {
        try
        {
            var drives = DriveInfo.GetDrives();
            var roots = new List<string>(drives.Length);
            foreach (var drive in drives)
            {
                try
                {
                    roots.Add(drive.RootDirectory.FullName);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // 单个卷元数据不可读时跳过它，不影响其它卷的匹配。
                }
            }

            return roots;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Array.Empty<string>();
        }
    }
}
