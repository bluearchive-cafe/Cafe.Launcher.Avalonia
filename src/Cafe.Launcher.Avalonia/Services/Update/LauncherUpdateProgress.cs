using System;

namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>Progress of a launcher update package download.</summary>
/// <param name="DownloadedBytes">Bytes written so far.</param>
/// <param name="TotalBytes">Declared total size, or null when the server did not declare one.</param>
public readonly record struct LauncherUpdateProgress(long DownloadedBytes, long? TotalBytes)
{
    /// <summary>Completed fraction in [0, 1]; 0 while the total size is unknown.</summary>
    public double Fraction => TotalBytes is > 0
        ? Math.Clamp((double)DownloadedBytes / TotalBytes.Value, 0d, 1d)
        : 0d;
}
