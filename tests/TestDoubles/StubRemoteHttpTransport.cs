using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// Scriptable <see cref="IRemoteHttpTransport"/> test double. Outcomes are
/// resolved per URI through <see cref="Responder"/>; a returned JSON string is
/// deserialized with the caller's serializer options so wire-contract fidelity
/// (casing, structure) keeps being exercised, a ready <typeparamref name="T"/>
/// is passed through, an <see cref="Exception"/> is thrown, and a
/// <c>byte[]</c>/<c>string</c> becomes a <see cref="RemoteBody"/> body.
/// Requests are recorded in <see cref="RequestedUris"/>.
/// </summary>
public sealed class StubRemoteHttpTransport : IRemoteHttpTransport
{
    public StubRemoteHttpTransport(Func<Uri, object?>? responder = null)
    {
        Responder = responder ?? (_ => null);
    }

    public Func<Uri, object?> Responder { get; set; }

    public List<Uri> RequestedUris { get; } = [];

    public Task<T?> GetJsonAsync<T>(
        Uri uri,
        RemoteRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        RequestedUris.Add(uri);
        return Task.FromResult(Resolve<T>(Responder(uri), options?.Json));
    }

    public Task<RemoteBody> GetStreamAsync(
        Uri uri,
        RemoteRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        RequestedUris.Add(uri);
        var body = Responder(uri) switch
        {
            Exception reason => throw reason,
            byte[] bytes => CreateStream(bytes),
            string text => CreateStream(Encoding.UTF8.GetBytes(text)),
            var unexpected => throw new InvalidOperationException(
                $"Stub transport cannot build a stream from {unexpected?.GetType().Name ?? "null"}.")
        };
        return Task.FromResult(body);
    }

    private static RemoteBody CreateStream(byte[] bytes) =>
        new(new MemoryStream(bytes), bytes.LongLength);

    private static T? Resolve<T>(object? outcome, JsonSerializerOptions? options) => outcome switch
    {
        null => default,
        Exception reason => throw reason,
        T typed => typed,
        string json => JsonSerializer.Deserialize<T>(json, options ?? JsonDefaults.Strict),
        _ => throw new InvalidOperationException(
            $"Stub transport outcome type {outcome.GetType().Name} cannot produce {typeof(T).Name}.")
    };
}
