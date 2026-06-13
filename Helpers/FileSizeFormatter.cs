using System;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// Shared file size formatting used by ViewModels and Services.
/// </summary>
public static class FileSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        if (bytes <= 0)
        {
            return "0KB";
        }

        var unit = (int)Math.Floor(Math.Log(bytes) / Math.Log(1024));
        if (unit >= Units.Length)
        {
            unit = Units.Length - 1;
        }

        var value = bytes / Math.Pow(1024, unit);
        return $"{value:0.##}{Units[unit]}";
    }
}
