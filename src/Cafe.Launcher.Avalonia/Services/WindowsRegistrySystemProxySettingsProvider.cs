using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

internal static class WindowsRegistrySystemProxySettingsProvider
{
    /// <summary>
    /// Reads Windows Internet Settings proxy configuration directly from the registry
    /// instead of shelling out to reg.exe (avoids PATH-hijacking risk and is faster).
    /// </summary>
    public static SystemProxySettings? GetSettings()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

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
}
