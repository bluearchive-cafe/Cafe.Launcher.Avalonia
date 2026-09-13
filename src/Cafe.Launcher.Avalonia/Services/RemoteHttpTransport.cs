using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// The single outbound remote-HTTP module: one lease, one validation pass, one
/// redirect loop, one status enforcement, one buffering/streaming guarantee per
/// call. Callers describe <em>what</em> to fetch (a URI plus optional per-call
/// policy); the transport owns <em>how</em> it is fetched safely — proxy-aware
/// leasing, SSRF validation with per-URI egress resolution, manual redirects,
/// non-2xx rejection, the buffered-JSON size cap with contextual parse errors,
/// and the idle-read stall budget.
/// </summary>
/// <remarks>
/// <para>Interface contract (everything a caller must know):</para>
/// <para><b>Error modes.</b> Calls throw <see cref="HttpRequestException"/> for
/// non-2xx answers, exceeded redirect limits, HTTPS→HTTP downgrade attempts, and
/// stalled or oversized bodies; <see cref="JsonException"/> for bodies that are
/// not valid JSON (the message carries the request URL, status, content type and
/// a byte preview, without the query string); <see cref="TaskCanceledException"/>
/// when the caller cancels or the lease timeout elapses; and
/// <see cref="InvalidOperationException"/> when the URL validator rejects a
/// target (scheme, port, userinfo, localhost, private address). There are no
/// other failure modes; there is deliberately no separate exception type.</para>
/// <para><b>Retries.</b> Each attempt re-leases a connection, so a retry never
/// reuses a failed connection. <see cref="RemoteRequestOptions.RetryScope"/>
/// selects which failures are retryable; <see cref="RemoteRequestOptions.MaxAttempts"/>
/// bounds the total attempts (1 = no retry), and backoff waits are injected so
/// tests never sleep. Envelope business codes are a caller concern and never
/// retried here.</para>
/// <para><b>Streams.</b> <see cref="GetStreamAsync"/> returns once headers are
/// validated; the returned <see cref="RemoteBody.Content"/> owns the response
/// and its lease and must be disposed by the caller. Every read passes through
/// the stall budget, so a silently stalled body surfaces as an
/// <see cref="HttpRequestException"/> instead of hanging forever.</para>
/// </remarks>
public interface IRemoteHttpTransport
{
    Task<T?> GetJsonAsync<T>(
        Uri uri,
        RemoteRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<RemoteBody> GetStreamAsync(
        Uri uri,
        RemoteRequestOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Which failures a retried call may legitimately hit again. The scopes mirror
/// the disciplines the launcher's callers actually use; transient-status
/// classification (408/429/5xx) matches <c>LauncherApiClient.IsRetryableRequestFailure</c>.
/// </summary>
public enum RemoteRetryScope
{
    /// <summary>Never retry. <see cref="RemoteRequestOptions.MaxAttempts"/> must be 1.</summary>
    None = 0,

    /// <summary>
    /// Retry only when no answer was produced at all: a connection-level
    /// <see cref="HttpRequestException"/> (status null) or an HttpClient timeout
    /// (<see cref="TaskCanceledException"/>). This is the resource panel's
    /// discipline, whose status enforcement sat outside its retry loop.
    /// </summary>
    Network,

    /// <summary><see cref="Network"/> plus bodies that failed to parse as JSON.</summary>
    NetworkAndParse,

    /// <summary>
    /// <see cref="NetworkAndParse"/> plus transient status answers (408, 429, 5xx).
    /// This is the manifest/envelope fetch discipline: a server that answered
    /// "try again shortly" is asked again, while 404-style authoritative answers
    /// are surfaced immediately.
    /// </summary>
    Transient
}

/// <summary>Per-call policy for <see cref="IRemoteHttpTransport"/>. Every field defaults, so a caller that wants plain defaults passes nothing.</summary>
public sealed record RemoteRequestOptions
{
    /// <summary>
    /// Overrides the proxy mode resolved from launcher settings for this call.
    /// The image cache pins <see cref="ProxyModes.Direct"/> this way; download
    /// sessions will use it for per-session policies.
    /// </summary>
    public string? ProxyMode { get; init; }

    /// <summary>Total attempts including the first. Must be 1 when <see cref="RetryScope"/> is <see cref="RemoteRetryScope.None"/>.</summary>
    public int MaxAttempts { get; init; } = 1;

    /// <summary>
    /// Wait before retry <paramref name="i"/> (0-based). Required whenever
    /// <see cref="MaxAttempts"/> exceeds 1; there is no silent default policy.
    /// </summary>
    public Func<int, TimeSpan>? Backoff { get; init; }

    /// <summary>Which failures are retryable. Only consulted when <see cref="MaxAttempts"/> exceeds 1.</summary>
    public RemoteRetryScope RetryScope { get; init; } = RemoteRetryScope.Network;

    /// <summary>
    /// Configures each request before it is sent. Invoked once per attempt and
    /// once per redirect hop, so time-signed headers (the API authorization
    /// header embeds the current Unix time) are recomputed instead of replayed.
    /// </summary>
    public Action<HttpRequestMessage>? ConfigureRequest { get; init; }

    /// <summary>
    /// Lease timeout for this call (covers the request until headers arrive
    /// under response-headers-read streaming). Null leaves the factory default.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>JSON serializer options for <see cref="IRemoteHttpTransport.GetJsonAsync{T}"/>; defaults to <see cref="JsonDefaults.Strict"/>.</summary>
    public JsonSerializerOptions? Json { get; init; }
}

/// <summary>
/// A validated streaming response body. <see cref="Content"/> owns the response
/// message and its lease; disposing it releases everything the call held.
/// </summary>
public readonly record struct RemoteBody(Stream Content, long? DeclaredContentLength);

/// <summary>
/// Executes validated, proxy-aware remote HTTP requests and owns redirect,
/// retry, response-lifetime, buffering, and body-stall policies.
/// </summary>
public sealed class RemoteHttpTransport : IRemoteHttpTransport
{
    private readonly Func<string, TimeSpan?, CancellationToken, Task<HttpClientLease>> createLeaseAsync;
    private readonly RemoteHttpUrlValidator urlValidator;
    private readonly Func<string> resolveProxyMode;
    private readonly Func<TimeSpan, CancellationToken, Task>? delayAsync;
    private readonly TimeSpan? idleReadTimeout;

    /// <summary>Production constructor — resolves proxy modes from launcher settings and leases from the shared factory.</summary>
    public RemoteHttpTransport(
        HttpClientFactory httpClientFactory,
        RemoteHttpUrlValidator urlValidator,
        Func<string> resolveProxyMode)
        : this(
            (proxyMode, timeout, cancellationToken) =>
                httpClientFactory.CreateLeaseAsync(proxyMode, baseAddress: null, timeout, cancellationToken),
            urlValidator,
            resolveProxyMode,
            delayAsync: null,
            idleReadTimeout: null)
    {
    }

    /// <summary>
    /// Injectable constructor — accepts a lease factory and a fixed proxy mode
    /// for testability; tests hand back pre-configured clients (optionally with
    /// a connection proxy) instead of dialing real sockets. The backoff wait
    /// and idle-read budget are injectable so tests never sleep real time.
    /// </summary>
    internal RemoteHttpTransport(
        Func<string, TimeSpan?, CancellationToken, Task<HttpClientLease>> createLeaseAsync,
        RemoteHttpUrlValidator urlValidator,
        string fixedProxyMode,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? idleReadTimeout = null)
        : this(
            createLeaseAsync,
            urlValidator,
            () => fixedProxyMode,
            delayAsync,
            idleReadTimeout)
    {
    }

    private RemoteHttpTransport(
        Func<string, TimeSpan?, CancellationToken, Task<HttpClientLease>> createLeaseAsync,
        RemoteHttpUrlValidator urlValidator,
        Func<string> resolveProxyMode,
        Func<TimeSpan, CancellationToken, Task>? delayAsync,
        TimeSpan? idleReadTimeout)
    {
        this.createLeaseAsync = createLeaseAsync;
        this.urlValidator = urlValidator;
        this.resolveProxyMode = resolveProxyMode;
        this.delayAsync = delayAsync;
        this.idleReadTimeout = idleReadTimeout;
    }

    /// <inheritdoc />
    public async Task<T?> GetJsonAsync<T>(
        Uri uri,
        RemoteRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var policy = Normalize(uri, options);
        return await RetryPolicy.ExecuteWithRetryAsync(
            async ct => await SendJsonOnceAsync<T>(uri, policy, ct).ConfigureAwait(false),
            policy.MaxAttempts,
            i => policy.Backoff!(i),
            cancellationToken,
            ex => IsRetryable(policy.RetryScope, ex),
            delayAsync).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RemoteBody> GetStreamAsync(
        Uri uri,
        RemoteRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var policy = Normalize(uri, options);
        var (lease, response) = await RetryPolicy.ExecuteWithRetryAsync(
            async ct => await SendStreamAttemptAsync(uri, policy, ct).ConfigureAwait(false),
            policy.MaxAttempts,
            i => policy.Backoff!(i),
            cancellationToken,
            ex => IsRetryable(policy.RetryScope, ex),
            delayAsync).ConfigureAwait(false);
        // ReadAsStreamAsync cannot throw after a successful send, and the lease is
        // handed to the owning stream, so no disposal gap exists between here and
        // the caller's dispose.
        var content = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return new RemoteBody(
            new OwnedRemoteResponseBodyStream(content, response, lease, idleReadTimeout),
            response.Content.Headers.ContentLength);
    }

    private async Task<T?> SendJsonOnceAsync<T>(
        Uri uri,
        RemoteRequestOptions policy,
        CancellationToken cancellationToken)
    {
        using var lease = await LeaseAsync(policy, cancellationToken).ConfigureAwait(false);
        using var response = await SendAsync(uri, policy, lease, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await DeserializeJsonAsync<T>(
                response,
                uri,
                policy.Json ?? JsonDefaults.Strict,
                cancellationToken).ConfigureAwait(false);
    }

    private async Task<(HttpClientLease Lease, HttpResponseMessage Response)> SendStreamAttemptAsync(
        Uri uri,
        RemoteRequestOptions policy,
        CancellationToken cancellationToken)
    {
        var lease = await LeaseAsync(policy, cancellationToken).ConfigureAwait(false);
        HttpResponseMessage? response = null;
        try
        {
            response = await SendAsync(uri, policy, lease, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            response?.Dispose();
            lease.Dispose();
            throw;
        }

        return (lease, response);
    }

    private Task<HttpClientLease> LeaseAsync(RemoteRequestOptions policy, CancellationToken cancellationToken) =>
        createLeaseAsync(policy.ProxyMode ?? resolveProxyMode(), policy.Timeout, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(
        Uri uri,
        RemoteRequestOptions policy,
        HttpClientLease lease,
        CancellationToken cancellationToken) =>
        await RemoteHttpRequestService.SendAsync(
            lease.Client,
            uri,
            current => BuildRequest(current, policy),
            urlValidator,
            cancellationToken,
            connectionProxy: lease.ConnectionProxy).ConfigureAwait(false);

    private static HttpRequestMessage BuildRequest(Uri uri, RemoteRequestOptions policy)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        policy.ConfigureRequest?.Invoke(request);
        return request;
    }

    private static RemoteRequestOptions Normalize(Uri uri, RemoteRequestOptions? options)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var policy = options ?? new RemoteRequestOptions();
        if (policy.MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxAttempts must be at least 1.");
        }

        // A single attempt has no retry decision to make; accept any scope.
        if (policy.MaxAttempts == 1)
        {
            return policy;
        }
        if (policy.RetryScope == RemoteRetryScope.None)
        {
            throw new ArgumentException(
                "RetryScope None allows a single attempt; set MaxAttempts to 1 or pick a scope.",
                nameof(options));
        }

        if (policy.Backoff is null)
        {
            throw new ArgumentException(
                "Backoff is required when MaxAttempts exceeds 1; there is no silent default policy.",
                nameof(options));
        }

        return policy;
    }

    private static bool IsRetryable(RemoteRetryScope scope, Exception exception) => scope switch
    {
        RemoteRetryScope.Network => ProducedNoResponse(exception),
        RemoteRetryScope.NetworkAndParse => ProducedNoResponse(exception) || exception is JsonException,
        RemoteRetryScope.Transient => exception is HttpRequestException { StatusCode: { } statusCode }
            ? statusCode == HttpStatusCode.RequestTimeout
                || statusCode == HttpStatusCode.TooManyRequests
                || (int)statusCode >= 500
            : exception is HttpRequestException or TaskCanceledException or JsonException,
        _ => false
    };

    private static bool ProducedNoResponse(Exception exception) =>
        exception is HttpRequestException { StatusCode: null } or TaskCanceledException;

    /// <summary>
    /// Upper bound for buffered JSON responses. Manifests and API envelopes are
    /// small metadata payloads (a manifest with tens of thousands of entries
    /// stays in the low-megabyte range), so this limit is generous while still
    /// preventing an errant remote payload — a CDN error page or a large binary
    /// blob served with a 200 status — from exhausting memory during startup.
    /// </summary>
    internal const int MaxBufferedJsonBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Buffers a remote HTTP response body and deserializes it as JSON. When
    /// the body is not valid JSON (a CDN error page, compressed bytes served
    /// with a 200 status and no <c>Content-Encoding</c>, or a binary blob),
    /// throws a <see cref="JsonException"/> carrying the request URL, status
    /// code, content type and a hex/ASCII preview of the first bytes so the
    /// failure is actionable in logs. This replaces the opaque
    /// <c>ExpectedStartOfValueNotFound, 0x8B</c> message that the strict
    /// <see cref="Utf8JsonReader"/> emits on the first invalid byte, which
    /// carries no request context. Manifests and API envelopes are small
    /// metadata payloads, so buffering into memory is safe.
    /// </summary>
    internal static Task<T?> DeserializeJsonAsync<T>(
        HttpResponseMessage response,
        Uri? requestUri,
        JsonSerializerOptions options,
        CancellationToken cancellationToken) =>
        DeserializeJsonAsync<T>(response, requestUri, options, MaxBufferedJsonBytes, cancellationToken);

    internal static async Task<T?> DeserializeJsonAsync<T>(
        HttpResponseMessage response,
        Uri? requestUri,
        JsonSerializerOptions options,
        int maxBytes,
        CancellationToken cancellationToken,
        TimeSpan? idleReadTimeout = null)
    {
        // Reject via the declared length when present; the streaming guard below
        // still bounds responses without a Content-Length (chunked transfer).
        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > maxBytes)
        {
            throw BuildResponseTooLargeException(requestUri, response, contentLength, contentLength);
        }

        await using var networkStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await ResponseBodyReader
                .ReadAsync(networkStream, chunk, cancellationToken, idleReadTimeout)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                throw BuildResponseTooLargeException(
                    requestUri,
                    response,
                    buffer.Length + read,
                    declaredBytes: null);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        buffer.Position = 0;

        try
        {
            return await JsonSerializer
                .DeserializeAsync<T>(buffer, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw BuildRemoteJsonException(requestUri, response, buffer, ex);
        }
    }

    /// <summary>
    /// Renders a request URI for a diagnostic message without its query string. Query values can
    /// carry credentials the endpoint accepts on their own — the resource panel's <c>uid</c> is one —
    /// and diagnostics are written to the log that the export feature always bundles, so the
    /// redaction happens here rather than at each call site.
    /// </summary>
    private static string DescribeUri(Uri? requestUri) =>
        requestUri?.GetLeftPart(UriPartial.Path) ?? "(unknown)";

    private static HttpRequestException BuildResponseTooLargeException(
        Uri? requestUri,
        HttpResponseMessage response,
        long actualBytes,
        long? declaredBytes)
    {
        var invariant = CultureInfo.InvariantCulture;
        var declared = declaredBytes.HasValue
            ? declaredBytes.Value.ToString(invariant)
            : "unknown";
        return new HttpRequestException(
            $"Remote response exceeds the {MaxBufferedJsonBytes.ToString(invariant)}-byte limit "
            + $"(declared: {declared}, buffered: {actualBytes.ToString(invariant)}). "
            + $"url: {DescribeUri(requestUri)} | "
            + $"status: {((int)response.StatusCode).ToString(invariant)} {response.ReasonPhrase} | "
            + $"content-type: {response.Content.Headers.ContentType?.ToString() ?? "(none)"}");
    }

    private static JsonException BuildRemoteJsonException(
        Uri? requestUri,
        HttpResponseMessage response,
        MemoryStream buffer,
        JsonException inner)
    {
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "(none)";
        var contentLength = response.Content.Headers.ContentLength;
        var snapshotLength = (int)Math.Min(buffer.Length, 16);
        var hex = snapshotLength > 0
            ? Convert.ToHexString(buffer.GetBuffer(), 0, snapshotLength)
            : "(empty)";
        var preview = BuildAsciiPreview(new ReadOnlySpan<byte>(buffer.GetBuffer(), 0, snapshotLength));
        var encodingHint = DetectCompression(new ReadOnlySpan<byte>(buffer.GetBuffer(), 0, snapshotLength));
        var invariant = CultureInfo.InvariantCulture;

        var message =
            $"Remote response is not valid JSON ({inner.Message}). "
            + $"url: {DescribeUri(requestUri)} | "
            + $"status: {((int)response.StatusCode).ToString(invariant)} {response.ReasonPhrase} | "
            + $"content-type: {contentType} | "
            + $"content-length: {(contentLength.HasValue ? contentLength.Value.ToString(invariant) : "unknown")} | "
            + $"actual-bytes: {buffer.Length.ToString(invariant)} | "
            + $"first-bytes: {hex} | "
            + $"preview: {preview}{encodingHint}";

        return new JsonException(message, inner);
    }

    private static string BuildAsciiPreview(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return "(empty)";
        }

        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i] = b >= 0x20 && b < 0x7F ? (char)b : '.';
        }

        return new string(chars);
    }

    private static string DetectCompression(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
        {
            return " [looks gzip-compressed; Content-Encoding was not decompressed]";
        }

        if (bytes.Length >= 2 && bytes[0] == 0x78 && (bytes[1] == 0x9C || bytes[1] == 0x01 || bytes[1] == 0xDA))
        {
            return " [looks zlib/deflate-compressed]";
        }

        if (bytes.Length >= 4 && bytes[0] == 0x28 && bytes[1] == 0xB5 && bytes[2] == 0x2F && bytes[3] == 0xFD)
        {
            return " [looks zstd-compressed]";
        }

        if (bytes.Length >= 3 && bytes[0] == 0x42 && bytes[1] == 0x5A && bytes[2] == 0x68)
        {
            return " [looks bzip2-compressed]";
        }

        return "";
    }
    /// <summary>
    /// A response body stream that routes every read through the idle-read stall
    /// budget and releases the response message and its lease when disposed.
    /// Synchronous reads are rejected — pooled HTTP content streams disallow them
    /// anyway, and the budget is only defined for async reads.
    /// </summary>
    private sealed class OwnedRemoteResponseBodyStream(
        Stream content,
        HttpResponseMessage response,
        HttpClientLease lease,
        TimeSpan? idleReadTimeout) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            await ResponseBodyReader
                .ReadAsync(content, buffer, cancellationToken, idleReadTimeout)
                .ConfigureAwait(false);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Remote response bodies support asynchronous reads only.");

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                content.Dispose();
                response.Dispose();
                lease.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await content.DisposeAsync().ConfigureAwait(false);
            response.Dispose();
            lease.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}
