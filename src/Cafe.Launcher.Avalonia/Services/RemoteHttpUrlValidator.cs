using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class RemoteHttpUrlValidator
{
    /// <summary>
    /// How long a successful all-public DNS resolution may be reused across
    /// requests. This is an IO-saving cache, not a rebinding defense: because
    /// the actual dial re-resolves independently (no connection pinning), a
    /// host whose record flips to a private address can still be dialed until
    /// the cached answer expires — the TTL caps the width of that
    /// validate-then-dial window (AUD-SEC-001), it does not close it. Blocked,
    /// private, empty, or failed resolutions are never cached and re-resolve
    /// on every request.
    /// </summary>
    internal static readonly TimeSpan DefaultCacheLifetime = TimeSpan.FromSeconds(30);

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolveHostAsync;
    private readonly TimeSpan cacheLifetime;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly ConcurrentDictionary<string, CacheEntry> resolutionCache =
        new(StringComparer.Ordinal);

    public RemoteHttpUrlValidator()
        : this(static (host, cancellationToken) =>
            Dns.GetHostAddressesAsync(host, cancellationToken))
    {
    }

    internal RemoteHttpUrlValidator(
        Func<string, CancellationToken, Task<IPAddress[]>> resolveHostAsync,
        TimeSpan? cacheLifetime = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.resolveHostAsync = resolveHostAsync;
        this.cacheLifetime = cacheLifetime ?? DefaultCacheLifetime;
        this.utcNow = utcNow ?? GetUtcNow;
    }

    private static DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;

    public Task<Uri> ValidateAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Remote URL must be absolute.");
        }

        return ValidateAsync(uri, cancellationToken);
    }

    public Task<Uri> ValidateAsync(
        Uri uri,
        CancellationToken cancellationToken = default) =>
        ValidateAsync(uri, connectionUsesProxy: false, cancellationToken);

    public async Task<Uri> ValidateAsync(
        Uri uri,
        bool connectionUsesProxy,
        CancellationToken cancellationToken = default)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Remote URL must use HTTP or HTTPS.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("Remote URL must not contain user information.");
        }

        if (!uri.IsDefaultPort && uri.Port is not (80 or 443))
        {
            throw new InvalidOperationException("Remote URL uses a blocked port.");
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Remote URL resolves to a blocked host.");
        }

        if (IPAddress.TryParse(uri.Host, out var literalAddress))
        {
            if (!IsPublicAddress(literalAddress))
            {
                throw new InvalidOperationException("Remote URL resolves to a blocked network address.");
            }

            return uri;
        }

        // When the request egresses through a user-configured proxy, the launcher never opens a
        // socket to a locally-resolved address — the proxy performs DNS resolution and makes the
        // connection. Resolving DNS locally here would be meaningless (we never dial that IP) and
        // actively harmful: a proxy is enabled precisely in networks where local DNS for the
        // target host is blocked or poisoned, so this SSRF guard would reject requests the proxy
        // can service. The scheme/port/userinfo/localhost-name/literal-IP checks above still apply.
        if (connectionUsesProxy)
        {
            return uri;
        }

        var addresses = await ResolveHostCachedAsync(uri.IdnHost, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException(
                "Remote URL resolves to a blocked network address. No addresses resolved");
        }

        // Fake-IP 应答段（IPv4 198.18/15、IPv6 ULA fc00::/7）不参与私网判定：fake-ip 模式的
        // 代理软件（Clash/mihomo，fake-ip-range 默认 198.18.0.1/16；fake-ip-range6 由用户配置，
        // 官方示例 fdfe::/64、sing-box 默认 fc00::/18 均落在 ULA）用这两段回答所有域名，双栈
        // 用户的正常解析必然整段落在这里。这是 AUD-SEC-006 让步的 IPv6 延伸——代价同样有界
        // （GET-only、端口限 80/443、跨主机重定向剥离凭据、响应不回传攻击方）。
        if (addresses.Any(address => !IsPublicAddress(address) && !IsFakeIpRange(address)))
        {
            var blocked = addresses.Where(a => !IsPublicAddress(a) && !IsFakeIpRange(a)).ToArray();
            throw new InvalidOperationException(
                $"Remote URL resolves to a blocked network address. Blocked: {string.Join(", ", blocked.Select(a => a.ToString()))}");
        }

        return uri;
    }

    /// <summary>
    /// Reports whether the most recent direct-path DNS resolution for
    /// <paramref name="host"/> (within the cache lifetime) was entirely made of
    /// Fake-IP range addresses — the signature of a fake-ip-mode proxy (Clash and
    /// similar) answering DNS locally. The send core consults this after a network
    /// failure to annotate the exception with a Fake-IP DNS marker, which the error
    /// presentation turns into targeted guidance instead of the generic network
    /// attribution. Literal-IP hosts never resolve and therefore never match.
    /// </summary>
    internal bool IsFakeIpResolution(string host)
    {
        return resolutionCache.TryGetValue(host, out var entry)
            && utcNow() < entry.ExpiresAt
            && entry.IsFakeIp;
    }

    /// <summary>
    /// Resolves <paramref name="host"/> through the underlying resolver, reusing a
    /// recent successful result when one is still within the cache lifetime. The
    /// launcher validates the same handful of CDN/API hosts on every request and
    /// every redirect hop — a fresh install issues thousands of file downloads, so
    /// an uncached lookup per attempt dominated resolver traffic. Only resolutions
    /// without a genuinely blocked address (fully public, or inside the Fake-IP
    /// answer bands the direct path tolerates) enter the cache: a private/blocked/
    /// empty result (and a thrown resolution error) must re-resolve next time so
    /// the SSRF guard never tolerates a record that turned private and transient
    /// DNS failures stay retryable. The entry records whether the answer was made
    /// entirely of Fake-IP range addresses for failure annotation.
    /// </summary>
    private async Task<IPAddress[]> ResolveHostCachedAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (resolutionCache.TryGetValue(host, out var entry) && utcNow() < entry.ExpiresAt)
        {
            return entry.Addresses;
        }

        var addresses = await resolveHostAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length > 0 && addresses.All(address => IsPublicAddress(address) || IsFakeIpRange(address)))
        {
            resolutionCache[host] = new CacheEntry(
                addresses,
                utcNow() + cacheLifetime,
                addresses.All(IsFakeIpRange));
        }

        return addresses;
    }

    private readonly record struct CacheEntry(IPAddress[] Addresses, DateTimeOffset ExpiresAt, bool IsFakeIp);

    internal static RemoteHttpUrlValidator CreateForTesting() =>
        new(static (_, _) => Task.FromResult<IPAddress[]>([IPAddress.Parse("93.184.216.34")]));

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublicAddress(address.MapToIPv4());
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] is >= 16 and <= 31 => false,
                192 when bytes[1] == 0 => false,
                192 when bytes[1] == 168 => false,
                >= 224 => false,

                // CGNAT（100.64/10）与基准测试段（198.18/15）刻意按公网放行
                // （AUD-SEC-006，接受风险）：fake-ip 模式的代理软件把 DNS 应答
                // 落在 198.18/15、部分 CDN 边缘节点落在 100.64/10，拦截会弄坏
                // 真实用户的横幅/下载（5a38be9 曾为此移除拦截）。放行的代价
                // 有界——GET-only、端口限 80/443、跨主机重定向剥离凭据、响应
                // 不回传攻击方。勿在未重审该让步前重新收紧。
                100 when bytes[1] is >= 64 and <= 127 => true,
                198 when bytes[1] is >= 18 and <= 19 => true,
                _ => true
            };
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return false;
        }

        var ipv6 = address.GetAddressBytes();
        return !IPAddress.IPv6Loopback.Equals(address)
            && !IPAddress.IPv6None.Equals(address)
            && !address.IsIPv6LinkLocal
            && !address.IsIPv6Multicast
            && !address.IsIPv6SiteLocal
            && (ipv6[0] & 0xFE) != 0xFC
            && !(ipv6[0] == 0x20
                 && ipv6[1] == 0x01
                 && ipv6[2] == 0x0D
                 && ipv6[3] == 0xB8);
    }

    /// <summary>
    /// 判定地址是否落在 Fake-IP 应答段：IPv4 为 RFC 2544 基准段 198.18/15（Clash/mihomo
    /// 的 fake-ip-range 默认 198.18.0.1/16），IPv6 为 ULA fc00::/7（fake-ip-range6 无统一
    /// 默认值——mihomo 官方示例 fdfe:dcba:9876::1/64 落在 fd00::/8，sing-box 默认
    /// fc00::/18——实际部署全部位于 ULA 段内）。IPv4-mapped IPv6 先展开回 IPv4 再判定。
    /// 该判定不改变 IsPublicAddress 的结论，只在直连路径的私网拦截中放行 Fake-IP 应答
    /// 段（AUD-SEC-006 的 IPv6 延伸），并驱动失败标注 <see cref="IsFakeIpResolution"/>。
    /// </summary>
    internal static bool IsFakeIpRange(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsFakeIpRange(address.MapToIPv4());
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 198 && bytes[1] is >= 18 and <= 19;
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return false;
        }

        var ipv6 = address.GetAddressBytes();
        return (ipv6[0] & 0xFE) == 0xFC;
    }
}
