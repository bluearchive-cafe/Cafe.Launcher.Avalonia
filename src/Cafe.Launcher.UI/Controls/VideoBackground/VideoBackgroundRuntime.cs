using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Cafe.Launcher.UI.Controls.VideoBackground;

/// <summary>视频背景格式和随应用分发的运行库位置；不扫描用户目录或下载可执行代码。</summary>
internal static class VideoBackgroundRuntime
{
    internal static bool IsVideoFile(string? path) =>
        Path.GetExtension(path)?.ToLowerInvariant() is ".mp4" or ".mkv" or ".webm";

    internal static string? FindLibraryPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var rid = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => null,
        };
        if (rid is null) return null;
        var path = Path.Combine(AppContext.BaseDirectory, "libmpv", rid, "libmpv-2.dll");
        return File.Exists(path) ? path : null;
    }
}
