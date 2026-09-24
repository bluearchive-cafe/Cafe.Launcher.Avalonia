using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// gsettings CLI 的唯一进程接缝：超时回收 + 输出捕获。GNOME 家族的设置存在 dconf，
/// 没有可读的稳定落盘文件，只能经 gsettings 读取；子进程生成不可在每次调用路径上
/// 重复，缓存策略由调用方自持（如 SystemAnimationSettingsProvider / SystemProxySettingsProvider）。
/// </summary>
internal static class GSettingsCli
{
    public const int DefaultTimeoutMilliseconds = 750;

    /// <summary>读取一个键的原始输出；gsettings 不存在、超时或失败时返回 null。</summary>
    public static string? Read(string schemaId, string key, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "gsettings",
                ArgumentList = { "get", schemaId, key },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return null;
            }

            // 输出固定为单个 GVariant 文本，不存在管道填满死锁；超时兜底回收，
            // 避免调用线程被挂住。
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return null;
            }

            return process.StandardOutput.ReadToEnd();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            // gsettings 不存在（非 GLib 桌面）或管道异常：按不可用处理。
            return null;
        }
    }
}
