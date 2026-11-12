using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Testing;

/// <summary>
/// 最简传输源替身：每批交出一个共享 <see cref="StubDownloadTransport"/>，
/// 并记录 CreateAsync 的调用次数与代理模式。
/// </summary>
public sealed class StubDownloadTransportSource : IDownloadTransportSource
{
    public int CreateCount { get; private set; }

    public List<string> RequestedProxyModes { get; } = [];

    public Task<IDownloadTransport> CreateAsync(string proxyMode, CancellationToken cancellationToken)
    {
        CreateCount++;
        RequestedProxyModes.Add(proxyMode);
        return Task.FromResult<IDownloadTransport>(new StubDownloadTransport());
    }
}
