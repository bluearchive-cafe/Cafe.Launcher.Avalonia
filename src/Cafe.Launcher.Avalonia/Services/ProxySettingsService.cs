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

internal sealed record SystemProxySettings(string ProxyUrl, IReadOnlyList<string> NoProxy, string? AutoConfigUrl = null);

/// <summary>
/// The proxy domain in one place. Normalization of raw system-proxy URLs
/// (bare hosts, <c>socks://</c>, legacy IE <c>socks=host:port</c> assignments,
/// shell-style bypass wildcards) happens exactly once — at settings ingestion —
/// and both the constructed <see cref="IWebProxy"/> and the cache fingerprint
/// consume the normalized form, so <c>socks://h</c> and <c>socks5://h</c> share
/// one cached handler.
/// </summary>
/// <remarks>
/// <para>
/// The cached-handler invariant is structural: the only way to obtain a proxy
/// handler is <see cref="GetOrCreateHandlerAsync"/>, which pairs fingerprint
/// computation and handler construction internally — a changed registry
/// snapshot always replaces the cached handler, an unchanged one always reuses
/// it, and no caller can hold a fingerprint without its handler or vice versa.
/// </para>
/// <para>
/// The fingerprint also covers the PAC script URL (<c>AutoConfigURL</c>) even
/// when no manual proxy is configured, mirroring WinINet's settings-changed
/// refresh: without it, a PAC reconfiguration would leave stale handlers cached
/// until restart, because the manual-proxy identity they were keyed on is empty
/// before and after the change.
/// </para>
/// </remarks>
public sealed class ProxySettingsService : IDisposable
{
    private readonly Func<SystemProxySettings?> systemProxySettingsProvider;
    private readonly Dictionary<string, CachedProxyHandler> proxyHandlers = new(StringComparer.Ordinal);
    private readonly object proxyHandlerLock = new();
    private bool disposed;

    public ProxySettingsService() : this(WindowsRegistrySystemProxySettingsProvider.GetSettings)
    {
    }

    internal ProxySettingsService(Func<SystemProxySettings?> systemProxySettingsProvider)
    {
        this.systemProxySettingsProvider = systemProxySettingsProvider;
    }

    private sealed record CachedProxyHandler(string Fingerprint, SocketsHttpHandler Handler);

    /// <summary>
    /// Returns a proxy-configured handler for the mode, reusing the cached one
    /// while the effective settings' fingerprint is unchanged. Direct mode is
    /// answered fresh and uncached — production routes it to the factory's
    /// shared direct handler before ever reaching this method; the branch only
    /// exists for completeness and tests.
    /// </summary>
    public async Task<SocketsHttpHandler> GetOrCreateHandlerAsync(
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (proxyMode == ProxyModes.Direct)
        {
            var directHandler = new SocketsHttpHandler();
            HttpClientFactory.ConfigureConnectionDefaults(directHandler);
            directHandler.UseProxy = false;
            return directHandler;
        }

        var fingerprint = GetFingerprint(proxyMode);
        lock (proxyHandlerLock)
        {
            if (proxyHandlers.TryGetValue(proxyMode, out var cached)
                && cached.Fingerprint == fingerprint)
            {
                return cached.Handler;
            }
        }

        // Handler creation is async (system-proxy resolution), so it happens
        // outside the lock; a concurrent lease may cache an equivalent handler
        // first, in which case the freshly created one is disposed unused.
        var created = await CreateHandlerAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        lock (proxyHandlerLock)
        {
            if (proxyHandlers.TryGetValue(proxyMode, out var existing)
                && existing.Fingerprint == fingerprint)
            {
                created.Dispose();
                return existing.Handler;
            }

            if (proxyHandlers.TryGetValue(proxyMode, out var stale))
            {
                stale.Handler.Dispose();
            }

            proxyHandlers[proxyMode] = new CachedProxyHandler(fingerprint, created);
            return created;
        }
    }

    /// <summary>Only for use by test projects (see <c>InternalsVisibleTo</c>).</summary>
    internal int CachedHandlerCount
    {
        get { lock (proxyHandlerLock) return proxyHandlers.Count; }
    }

    private async Task<SocketsHttpHandler> CreateHandlerAsync(
        string proxyMode,
        CancellationToken cancellationToken)
    {
        var handler = new SocketsHttpHandler();
        HttpClientFactory.ConfigureConnectionDefaults(handler);
        handler.UseProxy = true;
        handler.Proxy = proxyMode == ProxyModes.Auto
            ? WebRequest.GetSystemWebProxy()
            : BuildConfiguredProxy();
        await Task.CompletedTask.ConfigureAwait(false);
        return handler;
    }

    /// <summary>
    /// Auto mode always uses live default detection; System mode uses the
    /// configured proxy and only falls back to default detection when the
    /// snapshot is empty or PAC-only — the distinction the cache-prefix keeps
    /// separate. A PAC-only snapshot also routes to default detection: WinINet
    /// resolves "auto-detect → PAC → manual" and executes the script itself.
    /// </summary>
    private IWebProxy BuildConfiguredProxy()
    {
        var settings = GetNormalizedSettings();
        if (settings is null || string.IsNullOrEmpty(settings.ProxyUrl))
        {
            return WebRequest.GetSystemWebProxy();
        }

        // WinINet answers proxy authentication challenges (407) silently with
        // the current user's credentials; .NET's own system proxy object does
        // the same. The hand-built WebProxy must opt into default credentials,
        // or authenticated corporate proxies fail every proxied request.
        return new WebProxy(settings.ProxyUrl)
        {
            UseDefaultCredentials = true,
            BypassProxyOnLocal = settings.NoProxy.Any(IsLocalBypassToken),
            BypassList = BuildBypassRegexList(settings.NoProxy)
        };
    }

    private string GetFingerprint(string proxyMode)
    {
        var settings = GetNormalizedSettings();
        var identity = settings is null
            ? "system-default"
            : $"{settings.ProxyUrl}|{settings.AutoConfigUrl}|{string.Join(",", settings.NoProxy)}";
        return $"{proxyMode}:{identity}";
    }

    /// <summary>
    /// The single normalization point: every consumer of system-proxy settings —
    /// the constructed proxy, the cache fingerprint — sees the normalized URL,
    /// so a provider that forgets to normalize can never resurface the
    /// "socks://" NotSupportedException at request time.
    /// </summary>
    private SystemProxySettings? GetNormalizedSettings()
    {
        var settings = systemProxySettingsProvider();
        if (settings is null
            || (string.IsNullOrWhiteSpace(settings.ProxyUrl)
                && string.IsNullOrWhiteSpace(settings.AutoConfigUrl)))
        {
            return null;
        }

        return new SystemProxySettings(
            string.IsNullOrWhiteSpace(settings.ProxyUrl) ? string.Empty : ResolveProxyUrl(settings.ProxyUrl),
            settings.NoProxy,
            string.IsNullOrWhiteSpace(settings.AutoConfigUrl) ? null : settings.AutoConfigUrl.Trim());
    }

    private static bool IsLocalBypassToken(string entry) =>
        string.Equals(entry.Trim(), "<local>", StringComparison.OrdinalIgnoreCase);

    private static string[] BuildBypassRegexList(IEnumerable<string> entries) =>
        entries
            .Select(ConvertBypassEntryToRegex)
            .Where(pattern => pattern is not null)
            .Select(pattern => pattern!)
            .ToArray();

    private static string? ConvertBypassEntryToRegex(string entry)
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

    private static string ResolveProxyUrl(string value)
    {
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        // SocketsHttpHandler only accepts versioned SOCKS schemes (socks4/socks4a/socks5);
        // a proxy addressed as bare "socks://" throws NotSupportedException on the first
        // proxied request, so both the bare scheme and the legacy IE "socks=host:port"
        // registry entry normalize to "socks5://".
        if (value.StartsWith("socks://", StringComparison.OrdinalIgnoreCase))
        {
            return $"socks5://{value["socks://".Length..]}";
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
                return $"socks5://{socks}";
            }

            if (pairs.TryGetValue("https", out var https))
            {
                return $"http://{https}";
            }
        }

        return $"http://{value}";
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lock (proxyHandlerLock)
        {
            foreach (var cached in proxyHandlers.Values)
            {
                cached.Handler.Dispose();
            }

            proxyHandlers.Clear();
        }

        GC.SuppressFinalize(this);
    }
}
