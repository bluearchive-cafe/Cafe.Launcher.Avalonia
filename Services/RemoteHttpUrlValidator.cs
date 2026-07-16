using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class RemoteHttpUrlValidator
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolveHostAsync;

    public RemoteHttpUrlValidator()
        : this(static (host, cancellationToken) =>
            Dns.GetHostAddressesAsync(host, cancellationToken))
    {
    }

    internal RemoteHttpUrlValidator(
        Func<string, CancellationToken, Task<IPAddress[]>> resolveHostAsync)
    {
        this.resolveHostAsync = resolveHostAsync;
    }

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

        var addresses = await resolveHostAsync(uri.IdnHost, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
        {
            var blocked = addresses.Where(a => !IsPublicAddress(a)).ToArray();
            var blockedInfo = blocked.Length > 0
                ? $"Blocked: {string.Join(", ", blocked.Select(a => a.ToString()))}"
                : "No addresses resolved";
            throw new InvalidOperationException(
                $"Remote URL resolves to a blocked network address. {blockedInfo}");
        }

        return uri;
    }

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
}
