using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// Scriptable <see cref="IDownloadTransport"/> test double. <see cref="Responder"/>
/// maps a request (URI + request factory for header inspection) to the
/// <see cref="HttpResponseMessage"/> to serve; throw inside the responder to
/// simulate network failures. Every request's URI and Range start are recorded.
/// </summary>
public sealed class StubDownloadTransport : IDownloadTransport
{
    private Func<Uri, Func<Uri, HttpRequestMessage>, HttpResponseMessage> responder =
        static (_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.OK);

    public StubDownloadTransport(
        Func<Uri, Func<Uri, HttpRequestMessage>, HttpResponseMessage>? responder = null)
    {
        if (responder is not null)
        {
            this.responder = responder;
        }
    }

    /// <summary>可读写；赋值即整体替换应答映射（构造器注入与后续赋值等价生效）。</summary>
    public Func<Uri, Func<Uri, HttpRequestMessage>, HttpResponseMessage> Responder
    {
        get => responder;
        set => responder = value ?? (static (_, _) =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    public List<Uri> RequestedUris { get; } = [];

    public List<long?> RangeStarts { get; } = [];

    public Task<HttpResponseMessage> SendAsync(
        Uri uri,
        Func<Uri, HttpRequestMessage> createRequest,
        CancellationToken cancellationToken)
    {
        RequestedUris.Add(uri);
        using var request = createRequest(uri);
        RangeStarts.Add(request.Headers.Range?.Ranges.FirstOrDefault()?.From);
        return Task.FromResult(responder(uri, createRequest));
    }

    public void Dispose()
    {
    }
}
