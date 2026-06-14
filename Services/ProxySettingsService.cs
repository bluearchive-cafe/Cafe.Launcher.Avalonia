using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Microsoft.Win32;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class ProxySettingsService
{
    public Task<IWebProxy?> CreateProxyAsync(string proxyMode, CancellationToken cancellationToken = default)
    {
        if (proxyMode != ProxyModes.System)
        {
            return Task.FromResult<IWebProxy?>(null);
        }

        var settings = GetWindowsSystemProxy();
        if (settings is null || string.IsNullOrWhiteSpace(settings.ProxyUrl))
        {
            return Task.FromResult<IWebProxy?>(WebRequest.GetSystemWebProxy());
        }

        return Task.FromResult<IWebProxy?>(new WebProxy(settings.ProxyUrl)
        {
            BypassList = settings.NoProxy.ToArray()
        });
    }

    public async Task<SocketsHttpHandler> CreateHttpHandlerAsync(
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        var proxy = await CreateProxyAsync(proxyMode, cancellationToken);
        return new SocketsHttpHandler
        {
            UseProxy = proxyMode == ProxyModes.System,
            Proxy = proxy,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
    }

    /// <summary>
    /// Reads Windows Internet Settings proxy configuration directly from the registry
    /// instead of shelling out to reg.exe (avoids PATH-hijacking risk and is faster).
    /// </summary>
    private static WindowsProxySettings? GetWindowsSystemProxy()
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

            return new WindowsProxySettings
            {
                ProxyUrl = ResolveProxyUrl(proxyServer),
                NoProxy = noProxy
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveProxyUrl(string value)
    {
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("socks://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (value.Contains('=', StringComparison.Ordinal))
        {
            var pairs = value
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Split('=', 2))
                .Where(item => item.Length == 2)
                .ToDictionary(item => item[0], item => item[1], StringComparer.OrdinalIgnoreCase);

            if (pairs.TryGetValue("http", out var http))
            {
                return $"http://{http}";
            }

            if (pairs.TryGetValue("socks", out var socks))
            {
                return $"socks://{socks}";
            }

            if (pairs.TryGetValue("https", out var https))
            {
                return $"http://{https}";
            }
        }

        return $"http://{value}";
    }

    private sealed class WindowsProxySettings
    {
        public string ProxyUrl { get; set; } = "";

        public List<string> NoProxy { get; set; } = [];
    }
}
