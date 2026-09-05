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
            if (proxyEnable != 1)
            {
                return null;
            }

            var proxyServer = internetSettings?.GetValue("ProxyServer") as string;
            if (string.IsNullOrWhiteSpace(proxyServer))
            {
                return null;
            }

            var proxyOverride = internetSettings?.GetValue("ProxyOverride") as string;
            var noProxy = string.IsNullOrWhiteSpace(proxyOverride)
                ? new List<string>()
                : proxyOverride.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            noProxy.AddRange(["localhost", "127.0.0.1", "::1"]);

            return new SystemProxySettings(
                ProxySettingsService.ResolveProxyUrl(proxyServer),
                noProxy);
        }
        catch (Exception ex)
        {
            LocalDiagnostics.LogSync(LogEntrySeverity.Warn, "Proxy", $"failed to read system proxy settings from registry: {ex.Message}");
            return null;
        }
    }
}
