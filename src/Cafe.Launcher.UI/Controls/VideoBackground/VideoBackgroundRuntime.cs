using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Cafe.Launcher.UI.Controls.VideoBackground;

/// <summary>视频背景格式和受信任位置中的运行库；不扫描用户目录或下载可执行代码。</summary>
internal static class VideoBackgroundRuntime
{
    internal static bool IsVideoFile(string? path) =>
        Path.GetExtension(path)?.ToLowerInvariant() is ".mp4" or ".mkv" or ".webm";

    internal static string? FindLibraryPath()
    {
        foreach (var path in GetCurrentLibraryPaths())
            if (File.Exists(path)) return path;
        return null;
    }

    /// <summary>Falls back to another trusted runtime only when the native library itself cannot load.</summary>
    internal static MpvNative OpenLibrary(string preferredPath) =>
        OpenLibraryCandidates([preferredPath, .. GetCurrentLibraryPaths()]);

    internal static MpvNative OpenLibraryCandidates(IEnumerable<string> paths)
    {
        var attempted = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Exception? lastFailure = null;
        foreach (var path in paths)
        {
            if (!Path.IsPathFullyQualified(path) || !attempted.Add(path) || !File.Exists(path)) continue;
            try { return new MpvNative(path); }
            catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {
                lastFailure = exception;
            }
        }
        throw new DllNotFoundException("No compatible video background runtime could be loaded.", lastFailure);
    }

    private static string[] GetCurrentLibraryPaths()
    {
        var platform = OperatingSystem.IsWindows() ? OSPlatform.Windows :
            OperatingSystem.IsLinux() ? OSPlatform.Linux :
            OperatingSystem.IsMacOS() ? OSPlatform.OSX : default;
        return GetLibraryPaths(platform, RuntimeInformation.ProcessArchitecture, AppContext.BaseDirectory);
    }

    /// <summary>Only fixed, architecture-specific system locations are considered after the application bundle.</summary>
    internal static string[] GetLibraryPaths(OSPlatform platform, Architecture architecture, string baseDirectory)
    {
        if (platform == OSPlatform.Windows)
        {
            var rid = architecture switch { Architecture.X64 => "win-x64", Architecture.Arm64 => "win-arm64", _ => null };
            return rid is null ? [] : [Path.Combine(baseDirectory, "libmpv", rid, "libmpv-2.dll")];
        }
        if (platform == OSPlatform.Linux)
        {
            var rid = architecture switch { Architecture.X64 => "linux-x64", Architecture.Arm64 => "linux-arm64", _ => null };
            if (rid is null) return [];
            return [Path.Combine(baseDirectory, "libmpv", rid, "libmpv.so.2"),
                .. GetLinuxSystemLibraryPaths(architecture, "libmpv.so.2")];
        }
        if (platform == OSPlatform.OSX)
        {
            var rid = architecture switch { Architecture.X64 => "osx-x64", Architecture.Arm64 => "osx-arm64", _ => null };
            if (rid is null) return [];
            var prefix = architecture == Architecture.Arm64 ? "/opt/homebrew" : "/usr/local";
            return [Path.Combine(baseDirectory, "libmpv", rid, "libmpv.2.dylib"), $"{prefix}/lib/libmpv.2.dylib"];
        }
        return [];
    }

    /// <summary>Fixed system locations used for Linux native libraries, without consulting process search paths.</summary>
    internal static string[] GetLinuxSystemLibraryPaths(Architecture architecture, string name)
    {
        var triple = architecture switch { Architecture.X64 => "x86_64-linux-gnu", Architecture.Arm64 => "aarch64-linux-gnu", _ => null };
        if (triple is null) return [];
        return [$"/usr/lib/{triple}/{name}", $"/lib/{triple}/{name}",
            $"/usr/lib64/{name}", $"/usr/lib/{name}", $"/usr/local/lib/{name}"];
    }
}
