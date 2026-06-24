using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

internal sealed record SystemProxySettings(string ProxyUrl, IReadOnlyList<string> NoProxy);

public sealed class ProxySettingsService
{
    private readonly Func<SystemProxySettings?> systemProxySettingsProvider;

    public ProxySettingsService() : this(WindowsRegistrySystemProxySettingsProvider.GetSettings)
    {
    }

    internal ProxySettingsService(Func<SystemProxySettings?> systemProxySettingsProvider)
    {
        this.systemProxySettingsProvider = systemProxySettingsProvider;
    }

    public Task<IWebProxy?> CreateProxyAsync(string proxyMode, CancellationToken cancellationToken = default)
    {
        if (proxyMode != ProxyModes.System)
        {
            return Task.FromResult<IWebProxy?>(null);
        }

        var settings = systemProxySettingsProvider();
        if (settings is null || string.IsNullOrWhiteSpace(settings.ProxyUrl))
        {
            return Task.FromResult<IWebProxy?>(WebRequest.GetSystemWebProxy());
        }

        return Task.FromResult<IWebProxy?>(new WebProxy(settings.ProxyUrl)
        {
            BypassProxyOnLocal = settings.NoProxy.Any(IsLocalBypassToken),
            BypassList = BuildBypassRegexList(settings.NoProxy)
        });
    }

    private static bool IsLocalBypassToken(string entry) =>
        string.Equals(entry.Trim(), "<local>", StringComparison.OrdinalIgnoreCase);

    internal static string[] BuildBypassRegexList(IEnumerable<string> entries) =>
        entries
            .Select(ConvertBypassEntryToRegex)
            .Where(pattern => pattern is not null)
            .Select(pattern => pattern!)
            .ToArray();

    internal static string? ConvertBypassEntryToRegex(string entry)
    {
        var trimmed = entry.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        // Bracketed tokens such as <local> or <-loopback> are not host patterns: <local> is
        // mapped to BypassProxyOnLocal by the caller, and any others are ignored.
        if (trimmed.StartsWith('<') && trimmed.EndsWith('>'))
        {
            return null;
        }

        // Windows ProxyOverride / NO_PROXY entries use shell-style wildcards (e.g. "*.zhihu.com",
        // "192.168.*"), but WebProxy.BypassList compiles each entry as a .NET regex. Passing a raw
        // wildcard like "*zhihu.com" throws RegexParseException ("Quantifier '*' following nothing")
        // the first time the proxy is used, which breaks every proxied request. Escape regex
        // metacharacters, then translate the two shell wildcards.
        return Regex.Escape(trimmed)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);
    }

    public async Task<SocketsHttpHandler> CreateHttpHandlerAsync(
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        var proxy = await CreateProxyAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = proxyMode == ProxyModes.System,
            Proxy = proxy,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
    }

    internal static string ResolveProxyUrl(string value)
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
}
