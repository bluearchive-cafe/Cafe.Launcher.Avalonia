using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// Supplies short-lived, proxy-aware HTTP client leases to Core's remote transport.
/// The host owns pooled handlers and platform proxy discovery; the transport owns request policy.
/// </summary>
public interface IRemoteHttpClientLeaseSource
{
    Task<HttpClientLease> CreateLeaseAsync(
        string proxyMode,
        Uri? baseAddress = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
