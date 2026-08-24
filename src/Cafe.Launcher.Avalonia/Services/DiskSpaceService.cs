using System;
using System.Collections.Generic;
using System.IO;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class DiskSpaceService
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

        var root = Path.GetPathRoot(existingPath);
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

    public static long ResolveRequiredBytes(bool isFreshInstall, long plannedDownloadBytes, string? decompressionSize)
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
}
