using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// 读取操作系统的系统代理快照，供 ProxySettingsService 的 System 档消费
/// （Auto 档始终走 WebRequest.GetSystemWebProxy 的平台默认检测，Unix 上即
/// http_proxy/https_proxy/all_proxy/no_proxy 环境变量）。
/// Windows 读注册表 Internet Settings；Linux 读 GNOME GSettings 代理（gsettings
/// 子进程，结果按进程缓存——指纹每租约都会读取，子进程生成不可重复，GNOME 侧
/// 修改系统代理需重启启动器后生效）。Plasma 5/6 已移除代理设置界面，KDE 侧不再
/// 落盘代理配置，环境变量路径由默认检测兜底。
/// </summary>
internal static class SystemProxySettingsProvider
{
    private static readonly object gnomeGate = new();
    private static bool gnomeResolved;
    private static SystemProxySettings? gnomeResult;

    public static SystemProxySettings? GetSettings()
    {
        if (OperatingSystem.IsWindows())
        {
            return ReadWindowsSettings();
        }

        if (OperatingSystem.IsLinux())
        {
            return GetGnomeSettingsCached();
        }

        return null;
    }

    /// <summary>
    /// Reads Windows Internet Settings proxy configuration directly from the registry
    /// instead of shelling out to reg.exe (avoids PATH-hijacking risk and is faster).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static SystemProxySettings? ReadWindowsSettings()
    {
        try
        {
            using var internetSettings = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");

            var proxyEnable = internetSettings?.GetValue("ProxyEnable") as int?;
            var proxyServer = internetSettings?.GetValue("ProxyServer") as string ?? string.Empty;
            var autoConfigUrl = internetSettings?.GetValue("AutoConfigURL") as string ?? string.Empty;
            var manualConfigured = proxyEnable == 1 && !string.IsNullOrWhiteSpace(proxyServer);
            var pacConfigured = !string.IsNullOrWhiteSpace(autoConfigUrl);

            // PAC 脚本（AutoConfigURL）是系统代理配置的一部分：WinINet 按
            // "自动检测 → PAC → 手动"顺序解析，因此即使 ProxyEnable=0，
            // AutoConfigURL 也构成有效的系统代理快照。快照必须携带它，指纹
            // 才能在 PAC 变更时刷新处理器——对应 WinINet 的
            // INTERNET_OPTION_SETTINGS_CHANGED + REFRESH 通知机制。
            if (!manualConfigured && !pacConfigured)
            {
                return null;
            }

            var proxyOverride = internetSettings?.GetValue("ProxyOverride") as string;
            var noProxy = string.IsNullOrWhiteSpace(proxyOverride)
                ? new List<string>()
                : proxyOverride.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            noProxy.AddRange(["localhost", "127.0.0.1", "::1"]);

            // 原始值原样交付：socks 等格式规范化由 ProxySettingsService 在
            // 设置摄入点统一完成（全库唯一一处）。手动代理未启用时 ProxyUrl
            // 交付空串（PAC-only 快照），由消费方退回系统默认检测。
            return new SystemProxySettings(
                manualConfigured ? proxyServer : string.Empty,
                noProxy,
                pacConfigured ? autoConfigUrl : null);
        }
        catch (Exception ex)
        {
            LocalDiagnostics.LogSync(LogEntrySeverity.Warn, "Proxy", $"failed to read system proxy settings from registry: {ex.Message}");
            return null;
        }
    }

    private static SystemProxySettings? GetGnomeSettingsCached()
    {
        lock (gnomeGate)
        {
            if (!gnomeResolved)
            {
                gnomeResult = RunGnomeProbe();
                gnomeResolved = true;
            }

            return gnomeResult;
        }
    }

    private static SystemProxySettings? RunGnomeProbe()
    {
        var mode = ParseGSettingsString(
            GSettingsCli.Read("org.gnome.system.proxy", "mode") ?? string.Empty);
        if (mode.Equals("auto", StringComparison.Ordinal))
        {
            var autoConfigUrl = ParseGSettingsString(
                GSettingsCli.Read("org.gnome.system.proxy", "autoconfig-url") ?? string.Empty);
            if (string.IsNullOrWhiteSpace(autoConfigUrl))
            {
                return null;
            }

            // PAC-only 快照：消费方退回平台默认检测，与 Windows 快照同语义。
            return new SystemProxySettings(string.Empty, [], autoConfigUrl);
        }

        if (!mode.Equals("manual", StringComparison.Ordinal))
        {
            return null;
        }

        // 快照只承载一个代理 URL（消费方对全部 scheme 共用）：
        // https 优先、退 http（GNOME 客户端对 https 也回落 http 代理）、再退 socks。
        var proxyUrl = ReadManualSchemeProxy("https")
            ?? ReadManualSchemeProxy("http")
            ?? ReadSocksProxy();
        if (proxyUrl is null)
        {
            return null;
        }

        return new SystemProxySettings(
            proxyUrl,
            ParseGSettingsStringArray(
                GSettingsCli.Read("org.gnome.system.proxy", "ignore-hosts") ?? string.Empty),
            null);
    }

    private static string? ReadManualSchemeProxy(string scheme)
    {
        var host = ParseGSettingsString(
            GSettingsCli.Read($"org.gnome.system.proxy.{scheme}", "host") ?? string.Empty);
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        return $"http://{host}{FormatPortSuffix(scheme)}";
    }

    private static string? ReadSocksProxy()
    {
        var host = ParseGSettingsString(
            GSettingsCli.Read("org.gnome.system.proxy.socks", "host") ?? string.Empty);
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        // SocketsHttpHandler 只接受带版本的 socks 方案，直接交付规范化形态。
        return $"socks5://{host}{FormatPortSuffix("socks")}";
    }

    private static string FormatPortSuffix(string scheme)
    {
        return TryParseGSettingsPort(
                GSettingsCli.Read($"org.gnome.system.proxy.{scheme}", "port") ?? string.Empty,
                out var port)
            && port > 0
            ? $":{port.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;
    }

    /// <summary>
    /// 解析 gsettings 的字符串输出：裸字符串或 <c>'value'</c>，空值可能是
    /// <c>@s ''</c>（GVariant 类型标注形态）。
    /// </summary>
    internal static string ParseGSettingsString(string output)
    {
        var value = output.Trim();
        if (value.StartsWith("@s ", StringComparison.Ordinal))
        {
            value = value[3..].Trim();
        }

        if (value.Length >= 2 && value.StartsWith('\'') && value.EndsWith('\''))
        {
            value = value[1..^1];
        }

        return value;
    }

    /// <summary>解析 gsettings 的端口输出：<c>uint32 8080</c> 或裸 <c>8080</c>。</summary>
    internal static bool TryParseGSettingsPort(string output, out int port)
    {
        port = 0;
        var tokens = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        return int.TryParse(
            tokens[^1],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out port)
            && port >= 0;
    }

    /// <summary>
    /// 解析 gsettings 的字符串数组输出：<c>@as []</c> 或 <c>['a', 'b']</c>。
    /// 条目按 GNOME 语义是主机名/域后缀/CIDR；无法识别的形态整体按空数组处理。
    /// </summary>
    internal static IReadOnlyList<string> ParseGSettingsStringArray(string output)
    {
        var value = output.Trim();
        if (value.StartsWith("@as ", StringComparison.Ordinal))
        {
            value = value[4..].Trim();
        }

        if (!value.StartsWith('[') || !value.EndsWith(']'))
        {
            return [];
        }

        var inner = value[1..^1];
        if (string.IsNullOrWhiteSpace(inner))
        {
            return [];
        }

        return inner
            .Split(',')
            .Select(entry => entry.Trim())
            .Select(entry => entry.Length >= 2 && entry.StartsWith('\'') && entry.EndsWith('\'')
                ? entry[1..^1]
                : entry)
            .Where(entry => entry.Length > 0)
            .ToList();
    }
}
