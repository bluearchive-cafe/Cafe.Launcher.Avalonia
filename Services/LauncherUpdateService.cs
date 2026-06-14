using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Checks for launcher self-updates using a simple check-and-notify approach.
/// Mirrors the original Electron launcher's electron-updater behavior (check only, not auto-install).
/// </summary>
public sealed class LauncherUpdateService : IDisposable
{
    private readonly SocketsHttpHandler handler;
    private readonly HttpClient httpClient;
    private readonly ProxySettingsService proxySettingsService = new();
    private string proxyMode = ProxyModes.Direct;

    public LauncherUpdateService()
    {
        handler = new SocketsHttpHandler
        {
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
        httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public void SetProxyMode(string value)
    {
        proxyMode = value == ProxyModes.System ? ProxyModes.System : ProxyModes.Direct;
    }

    /// <summary>
    /// Checks if a newer launcher version is available.
    /// Returns the latest version string, or null if current or unavailable.
    /// </summary>
    public async Task<string?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var versionUrl = $"{LauncherConstants.UpdatePackageUrl}latest.yml";
            using var lease = await CreateRequestClientAsync(ct);
            var response = await lease.Client.GetStringAsync(versionUrl, ct);

            // Parse the version from the latest.yml file
            foreach (var line in response.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("version:"))
                {
                    // Strip quotes and whitespace — YAML values may be quoted or unquoted
                    var latestVersion = trimmed["version:".Length..].Trim().Trim('"', '\'');
                    if (IsNewer(latestVersion, LauncherConstants.LauncherVersion))
                    {
                        return latestVersion;
                    }
                    return null;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true if version1 is greater than version2 using numeric segment comparison.
    /// </summary>
    private static bool IsNewer(string v1, string v2)
    {
        var comparison = VersionComparer.Compare(v1, v2);
        return comparison > 0;
    }

    public void Dispose()
    {
        httpClient.Dispose();
        handler.Dispose();
    }

    private async Task<RequestHttpClientLease> CreateRequestClientAsync(CancellationToken ct)
    {
        if (proxyMode != ProxyModes.System)
        {
            return new RequestHttpClientLease(httpClient);
        }

        var requestHandler = await proxySettingsService.CreateHttpHandlerAsync(proxyMode, ct);
        var client = new HttpClient(requestHandler)
        {
            Timeout = httpClient.Timeout
        };
        return new RequestHttpClientLease(client, requestHandler);
    }

    private sealed class RequestHttpClientLease : IDisposable
    {
        private readonly SocketsHttpHandler? handler;
        private readonly bool ownsClient;

        public RequestHttpClientLease(HttpClient client)
        {
            Client = client;
        }

        public RequestHttpClientLease(HttpClient client, SocketsHttpHandler handler)
        {
            Client = client;
            this.handler = handler;
            ownsClient = true;
        }

        public HttpClient Client { get; }

        public void Dispose()
        {
            if (ownsClient)
            {
                Client.Dispose();
                handler?.Dispose();
            }
        }
    }
}
