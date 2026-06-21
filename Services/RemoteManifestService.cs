using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Deep module that owns the two-phase remote manifest protocol:
///   1. Get manifest URL from server (metadata → URL)
///   2. Download manifest from URL (URL → RemoteManifest)
///
/// Eliminates duplicate protocol interpretation across GameDownloadService
/// and ManifestValidationService, each of which re-implemented the same
/// two-phase fetch with different failure semantics.
/// </summary>
public sealed class RemoteManifestService
{
    private readonly LauncherApiClient apiClient;

    public RemoteManifestService(LauncherApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    /// <summary>
    /// Fetch a remote manifest that the caller MUST have (e.g. latest version
    /// for download/repair). Throws if the manifest URL is empty — the caller
    /// cannot proceed without this manifest.
    /// Cancellation propagates as <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<RemoteManifest> GetRequiredManifestAsync(
        string version,
        string basis,
        string patchUrlGroup,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        var url = await apiClient.GetManifestUrlAsync(
            version,
            basis,
            patchUrlGroup,
            proxyMode,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(url.Url))
        {
            throw new InvalidOperationException("Remote manifest URL is empty.");
        }

        return await apiClient.GetRemoteManifestAsync(
            url.Url,
            proxyMode,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetch a remote manifest as a best-effort. Returns null on empty URL
    /// or non-critical network/protocol failures. The caller can fall back
    /// to local data.
    /// Cancellation always propagates.
    /// </summary>
    public async Task<RemoteManifest?> GetOptionalManifestAsync(
        string version,
        string basis,
        string patchUrlGroup,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = await apiClient.GetManifestUrlAsync(
                version,
                basis,
                patchUrlGroup,
                proxyMode,
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(url.Url))
            {
                return null;
            }

            return await apiClient.GetRemoteManifestAsync(
                url.Url,
                proxyMode,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
