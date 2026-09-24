using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// 读取操作系统的「动画已启用」偏好，供 MotionSettingsResolver 在 System 档使用。
/// Windows 读 SPI_GETCLIENTAREAANIMATION；Linux 依序探测 KDE kdeglobals、GTK settings.ini
/// 与 gsettings（GNOME 家族的 dconf，经共享的 GSettingsCli 进程接缝），
/// 全部不可读时返回 null，由调用方按「未知 → 减少动效」处理。
/// gsettings 需要拉起子进程，而窗口每次激活（MainWindow.OnActivated）都会重新读取本偏好，
/// 因此其结果按进程缓存：GNOME 侧修改系统动画需重启启动器后生效；
/// KDE/GTK 走文件读取，足够便宜，每次调用重读，系统设置变更即时生效。
/// </summary>
public sealed class SystemAnimationSettingsProvider
{
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private static readonly object gSettingsGate = new();
    private static bool gSettingsResolved;
    private static bool? gSettingsResult;
    private readonly Func<bool?> readAnimationsEnabled;

    public SystemAnimationSettingsProvider()
        : this(ReadAnimationsEnabled)
    {
    }

    internal SystemAnimationSettingsProvider(Func<bool?> readAnimationsEnabled)
    {
        this.readAnimationsEnabled = readAnimationsEnabled;
    }

    public bool? GetSystemAnimationsEnabled() => readAnimationsEnabled();

    private static bool? ReadAnimationsEnabled()
    {
        if (OperatingSystem.IsWindows())
        {
            var success = SystemParametersInfoW(SpiGetClientAreaAnimation, 0, out var enabled, 0);
            return success && enabled;
        }

        if (OperatingSystem.IsLinux())
        {
            return ReadLinuxAnimationsEnabled();
        }

        return null;
    }

    private static bool? ReadLinuxAnimationsEnabled()
    {
        // SpecialFolder.ApplicationData 在 Linux 上映射 XDG_CONFIG_HOME（缺省 ~/.config）。
        var configDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // Plasma 把动画时长系数落在 kdeglobals（0 = 关闭动画）；纯 Plasma 会话的 dconf
        // 通常没有对应条目，gsettings 只会答出 schema 默认值，因此文件探测先于 gsettings。
        var kde = TryReadKdeAnimationEnabled(Path.Combine(configDirectory, "kdeglobals"));
        if (kde.HasValue)
        {
            return kde;
        }

        // GTK 显式写下的 gtk-enable-animations（XFCE 等桌面或用户手写）也是可读的权威值。
        var gtk = TryReadGtkAnimationEnabled(Path.Combine(configDirectory, "gtk-4.0", "settings.ini"))
            ?? TryReadGtkAnimationEnabled(Path.Combine(configDirectory, "gtk-3.0", "settings.ini"));
        if (gtk.HasValue)
        {
            return gtk;
        }

        return GetGSettingsEnabledCached();
    }

    /// <summary>解析 kdeglobals：<c>[KDE]</c> 段的 <c>AnimationDurationFactor</c>，0 表示关闭动画。</summary>
    internal static bool? TryReadKdeAnimationEnabled(string kdeglobalsPath)
    {
        if (!TryReadAllLines(kdeglobalsPath, out var lines))
        {
            return null;
        }

        var inKdeSection = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inKdeSection = line.Equals("[KDE]", StringComparison.Ordinal);
                continue;
            }

            if (!inKdeSection)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0
                || !line[..separator].Trim().Equals(
                    "AnimationDurationFactor",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!double.TryParse(
                    line[(separator + 1)..].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var factor))
            {
                return null;
            }

            return factor != 0d;
        }

        return null;
    }

    /// <summary>解析 GTK settings.ini：<c>[Settings]</c> 段的 <c>gtk-enable-animations</c>，兼容 0/1 写法。</summary>
    internal static bool? TryReadGtkAnimationEnabled(string settingsIniPath)
    {
        if (!TryReadAllLines(settingsIniPath, out var lines))
        {
            return null;
        }

        var inSettingsSection = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inSettingsSection = line.Equals("[Settings]", StringComparison.Ordinal);
                continue;
            }

            if (!inSettingsSection)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0
                || !line[..separator].Trim().Equals(
                    "gtk-enable-animations",
                    StringComparison.Ordinal))
            {
                continue;
            }

            return line[(separator + 1)..].Trim() switch
            {
                "true" or "1" => true,
                "false" or "0" => false,
                _ => null
            };
        }

        return null;
    }

    internal static bool? ParseGSettingsOutput(string output) => output.Trim() switch
    {
        "true" => true,
        "false" => false,
        _ => null
    };

    private static bool? GetGSettingsEnabledCached()
    {
        lock (gSettingsGate)
        {
            if (!gSettingsResolved)
            {
                gSettingsResult = RunGSettingsProbe();
                gSettingsResolved = true;
            }

            return gSettingsResult;
        }
    }

    private static bool? RunGSettingsProbe() =>
        ParseGSettingsOutput(
            GSettingsCli.Read("org.gnome.desktop.interface", "enable-animations") ?? string.Empty);

    private static bool TryReadAllLines(string path, out string[] lines)
    {
        try
        {
            lines = File.ReadAllLines(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lines = [];
            return false;
        }
    }

    // 经典 DllImport：bool 封送不需要 unsafe，允许项目保持 AllowUnsafeBlocks=false。
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(
        uint uiAction,
        uint uiParam,
        [MarshalAs(UnmanagedType.Bool)] out bool pvParam,
        uint fWinIni);
}
