using System;
using System.Net.Http;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// A scoped lease on an <see cref="HttpClient"/> that conditionally disposes its
/// underlying handler when the client was created specifically for a single request
/// (e.g. with a proxy-aware <see cref="SocketsHttpHandler"/>).
/// </summary>
public sealed class HttpClientLease : IDisposable
{
    private readonly SocketsHttpHandler? handler;
    private readonly bool ownsClient;

    /// <summary>
    /// Borrows a long-lived client. The lease does NOT take ownership.
    /// </summary>
    public HttpClientLease(HttpClient client)
    {
        Client = client;
    }

    /// <summary>
    /// Wraps a client that owns only its own managed resources while its handler is shared.
    /// </summary>
    public HttpClientLease(HttpClient client, bool ownsClient)
    {
        Client = client;
        this.ownsClient = ownsClient;
    }

    /// <summary>
    /// Wraps a per-request client with its own handler. The lease owns both
    /// and will dispose them on <see cref="Dispose"/>.
    /// </summary>
    public HttpClientLease(HttpClient client, SocketsHttpHandler handler)
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
        GC.SuppressFinalize(this);
    }
}
