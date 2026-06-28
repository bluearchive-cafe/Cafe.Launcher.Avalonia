using System;
using System.IO;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class DiskSpaceService
{
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

        try
        {
            var root = Path.GetPathRoot(existingPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public bool HasEnoughSpace(string path, long requiredBytes)
    {
        if (requiredBytes <= 0)
        {
            return true;
        }

        var availableBytes = GetAvailableBytes(path);
        return availableBytes.HasValue && availableBytes.Value >= requiredBytes;
    }

    private static string? FindExistingDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var current = Path.GetFullPath(path);
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
