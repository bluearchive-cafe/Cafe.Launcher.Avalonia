using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class RemoteHttpTransportTests
{
    // ---- Redirect / send semantics (migrated from RemoteHttpUrlValidatorTests) ----

    [Fact]
    public async Task GetStreamAsync_WhenRedirectTargetsLocalhost_BlocksBeforeSecondRequest()
    {
        var handler = new RedirectHandler();
        var transport = CreateTransport(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.GetStreamAsync(new Uri("http://example.test/start")));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetStreamAsync_WhenConnectionRoutesThroughProxy_SkipsLocalDnsResolution()
    {
        var validator = new RemoteHttpUrlValidator(
            static (_, _) => throw new InvalidOperationException("DNS must not be resolved."));
        var transport = CreateTransport(
            new OkHandler(),
            validator: validator,
            connectionProxy: new RoutingProxyStub());

        using var body = (await transport.GetStreamAsync(new Uri("https://example.test/start"))).Content;

        Assert.True(body.CanRead);
    }

    [Fact]
    public async Task GetStreamAsync_WhenConnectionProxyBypassesUri_RunsLocalDnsValidation()
    {
        // 守卫（AUD-NET-002）：代理模式在目标被旁路（或 GetProxy 返回原 URI）时
        // 实际是本机直连，必须保留 URL 校验器的本地 DNS 私网解析，而不是按
        // 设置枚举一刀切跳过。
        var resolvedHosts = new List<string>();
        var validator = new RemoteHttpUrlValidator((host, _) =>
        {
            resolvedHosts.Add(host);
            return Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
        });
        var transport = CreateTransport(
            new OkHandler(),
            validator: validator,
            connectionProxy: new BypassingProxyStub());

        using var body = (await transport.GetStreamAsync(new Uri("https://example.test/start"))).Content;

        Assert.Equal(["example.test"], resolvedHosts);
    }

    [Fact]
    public async Task GetStreamAsync_WhenResponseIsNotRedirect_ReturnsFirstResponse()
    {
        var handler = new OkHandler();
        var transport = CreateTransport(handler);

        var remote = await transport.GetStreamAsync(new Uri("https://example.test/start"));
        using var body = remote.Content;
        var buffer = new Memory<byte>(new byte[16]);
        var read = await body.ReadAsync(buffer);

        Assert.Equal(0, read);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetStreamAsync_WhenClientPrefersHttp2_AppliesPreferenceToManualRequest()
    {
        var handler = new OkHandler();
        var transport = CreateTransport(
            handler,
            configureClient: client =>
            {
                client.DefaultRequestVersion = HttpVersion.Version20;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            });

        var remote = await transport.GetStreamAsync(new Uri("https://example.test/start"));
        using var body = remote.Content;

        Assert.Equal(HttpVersion.Version20, handler.RequestVersion);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, handler.RequestVersionPolicy);
    }

    [Fact]
    public async Task GetStreamAsync_WhenRedirectIsRelative_FollowsRedirect()
    {
        var handler = new RelativeRedirectHandler();
        var transport = CreateTransport(handler);

        var remote = await transport.GetStreamAsync(new Uri("https://example.test/start"));
        using var body = remote.Content;

        Assert.Equal(
            ["https://example.test/start", "https://example.test/final"],
            handler.RequestUris);
    }

    [Fact]
    public async Task GetStreamAsync_WhenRedirectHasNoLocation_Throws()
    {
        var transport = CreateTransport(new MissingLocationHandler());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => transport.GetStreamAsync(new Uri("https://example.test/start")));
    }

    [Fact]
    public async Task GetStreamAsync_WhenRedirectDowngradesHttpsToHttp_Throws()
    {
        var transport = CreateTransport(new DowngradeRedirectHandler());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => transport.GetStreamAsync(new Uri("https://example.test/start")));
    }

    [Fact]
    public async Task GetStreamAsync_WhenRedirectLimitIsExceeded_ThrowsAfterSixRequests()
    {
        var handler = new EndlessRedirectHandler();
        var transport = CreateTransport(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => transport.GetStreamAsync(new Uri("https://example.test/start")));

        Assert.Equal(6, handler.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Moved)]
    [InlineData(HttpStatusCode.RedirectMethod)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task GetStreamAsync_WhenRedirectUsesSupportedStatusCode_FollowsRedirect(HttpStatusCode statusCode)
    {
        var handler = new SingleRedirectHandler(statusCode);
        var transport = CreateTransport(handler);

        var remote = await transport.GetStreamAsync(new Uri("https://example.test/start"));
        using var body = remote.Content;

        Assert.Equal(
            ["https://example.test/start", "https://example.test/final"],
            handler.RequestUris);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EgressesThroughProxy_WhenProxyBypassesOrRoutesToSameUri_UsesDirectConnection(
        bool bypassed)
    {
        var proxy = new StubProxy(
            IsBypassed: bypassed,
            Via: new Uri("https://example.test/start"));

        var egressesThroughProxy = RemoteHttpRequestService.EgressesThroughProxy(
            proxy,
            new Uri("https://example.test/start"));

        Assert.False(egressesThroughProxy);
    }

    [Fact]
    public void EgressesThroughProxy_WhenProxyRoutesViaDifferentUri_EgressesThroughProxy()
    {
        var proxy = new StubProxy(
            IsBypassed: false,
            Via: new Uri("http://proxy.example.invalid:8080"));

        var egressesThroughProxy = RemoteHttpRequestService.EgressesThroughProxy(
            proxy,
            new Uri("https://example.test/start"));

        Assert.True(egressesThroughProxy);
    }

    [Fact]
    public void EgressesThroughProxy_WhenProxyIsNull_UsesDirectConnection()
    {
        Assert.False(RemoteHttpRequestService.EgressesThroughProxy(
            null,
            new Uri("https://example.test/start")));
    }

    // ---- JSON contract ----

    [Fact]
    public async Task GetJsonAsync_WhenBodyIsValid_ReturnsDeserialized()
    {
        var transport = CreateTransport(
            new ScriptedHandler(ScriptedHandler.Ok("""{"Name":"hello"}""")));

        var payload = await transport.GetJsonAsync<SamplePayload>(new Uri("https://example.test/data"));

        Assert.Equal("hello", payload?.Name);
    }

    [Fact]
    public async Task GetJsonAsync_WhenBodyIsJsonNull_ReturnsNull()
    {
        // T? 的 null 语义仅覆盖 JSON 字面量 null；空体会按无效 JSON 抛出。
        var transport = CreateTransport(
            new ScriptedHandler(ScriptedHandler.Ok("null")));

        var payload = await transport.GetJsonAsync<SamplePayload>(new Uri("https://example.test/data"));

        Assert.Null(payload);
    }

    [Fact]
    public async Task GetJsonAsync_WhenBodyIsEmpty_ThrowsJsonException()
    {
        var transport = CreateTransport(
            new ScriptedHandler(ScriptedHandler.Ok("")));

        await Assert.ThrowsAsync<JsonException>(
            () => transport.GetJsonAsync<SamplePayload>(new Uri("https://example.test/data")));
    }

    [Fact]
    public async Task GetJsonAsync_WhenBodyIsMalformed_ThrowsJsonException()
    {
        var transport = CreateTransport(
            new ScriptedHandler(ScriptedHandler.Ok("<html>not json</html>")));

        await Assert.ThrowsAsync<JsonException>(
            () => transport.GetJsonAsync<SamplePayload>(new Uri("https://example.test/data")));
    }

    [Fact]
    public async Task GetJsonAsync_WhenOptionsProvideProxyMode_OverridesResolvedMode()
    {
        var leaseSource = new StubLeaseSource(new OkHandler());
        var transport = new RemoteHttpTransport(
            leaseSource,
            RemoteHttpUrlValidator.CreateForTesting(),
            ProxyModes.Auto);

        var remote = await transport.GetStreamAsync(
            new Uri("https://example.test/start"),
            new RemoteRequestOptions { ProxyMode = ProxyModes.Direct });
        using var body = remote.Content;

        Assert.Equal([ProxyModes.Direct], leaseSource.RequestedProxyModes);
    }

    [Fact]
    public async Task GetJsonAsync_WhenNoOverrideIsGiven_UsesResolvedMode()
    {
        var leaseSource = new StubLeaseSource(new OkHandler());
        var transport = new RemoteHttpTransport(
            leaseSource,
            RemoteHttpUrlValidator.CreateForTesting(),
            ProxyModes.Auto);

        var remote = await transport.GetStreamAsync(new Uri("https://example.test/start"));
        using var body = remote.Content;

        Assert.Equal([ProxyModes.Auto], leaseSource.RequestedProxyModes);
    }

    [Fact]
    public async Task GetJsonAsync_WhenConfigureRequestIsSet_AppliesHeadersOnEveryAttempt()
    {
        // ConfigureRequest 必须每次尝试重新执行：API 授权头内嵌当前 Unix 时间，
        // 重放旧请求等于发送过期签名。
        var attempt = 0;
        var handler = new ScriptedHandler(
            ScriptedHandler.Fail(new HttpRequestException("connection lost")),
            ScriptedHandler.Ok("""{"Name":"hello"}"""));
        var transport = CreateTransport(handler);
        var options = new RemoteRequestOptions
        {
            MaxAttempts = 2,
            Backoff = _ => TimeSpan.FromMilliseconds(1),
            RetryScope = RemoteRetryScope.Network,
            ConfigureRequest = request => request.Headers.TryAddWithoutValidation(
                "X-Test-Header",
                Interlocked.Increment(ref attempt).ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        var payload = await transport.GetJsonAsync<SamplePayload>(
            new Uri("https://example.test/data"), options);

        Assert.Equal("hello", payload?.Name);
        Assert.Equal(["1", "2"], handler.TestHeaderValues);
    }

    // ---- Retry policy ----

    [Fact]
    public async Task GetJsonAsync_WhenMaxAttemptsExceedsOneWithoutBackoff_Throws()
    {
        var transport = CreateTransport(new OkHandler());
        var options = new RemoteRequestOptions { MaxAttempts = 2 };

        await Assert.ThrowsAsync<ArgumentException>(
            () => transport.GetJsonAsync<SamplePayload>(new Uri("https://example.test/data"), options));
    }

    [Fact]
    public async Task GetJsonAsync_WhenScopeIsNoneWithMultipleAttempts_Throws()
    {
        var transport = CreateTransport(new OkHandler());
        var options = new RemoteRequestOptions
        {
            MaxAttempts = 2,
            Backoff = _ => TimeSpan.FromMilliseconds(1),
            RetryScope = RemoteRetryScope.None
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => transport.GetJsonAsync<SamplePayload>(new Uri("https://example.test/data"), options));
    }

    [Fact]
    public async Task GetJsonAsync_WhenMaxAttemptsIsZero_Throws()
    {
        var transport = CreateTransport(new OkHandler());
        var options = new RemoteRequestOptions { MaxAttempts = 0 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => transport.GetJsonAsync<SamplePayload>(new Uri("https://example.test/data"), options));
    }

    [Fact]
    public async Task GetJsonAsync_WhenNetworkErrorThenSuccess_RetriesAndReleasesPerAttempt()
    {
        var recordedDelays = new List<TimeSpan>();
        var leaseSource = new StubLeaseSource(new ScriptedHandler(
            ScriptedHandler.Fail(new HttpRequestException("connection lost")),
            ScriptedHandler.Ok("""{"Name":"hello"}""")));
        var transport = new RemoteHttpTransport(
            leaseSource,
            RemoteHttpUrlValidator.CreateForTesting(),
            ProxyModes.Direct,
            delayAsync: (delay, _) =>
            {
                recordedDelays.Add(delay);
                return Task.CompletedTask;
            });
        var options = new RemoteRequestOptions
        {
            MaxAttempts = 2,
            Backoff = i => TimeSpan.FromMilliseconds(50 * (i + 1)),
            RetryScope = RemoteRetryScope.Network
        };

        var payload = await transport.GetJsonAsync<SamplePayload>(
            new Uri("https://example.test/data"), options);

        Assert.Equal("hello", payload?.Name);
        Assert.Equal([TimeSpan.FromMilliseconds(50)], recordedDelays);
        // 每次尝试重新租约：重试绝不复用失败的连接。
        Assert.Equal(2, leaseSource.LeaseCount);
    }

    [Fact]
    public async Task GetJsonAsync_WhenScopeIsNetworkAndServerFails_DoesNotRetry()
    {
        var handler = new ScriptedHandler(ScriptedHandler.Status(HttpStatusCode.InternalServerError));
        var transport = CreateTransport(handler);
        var options = new RemoteRequestOptions
        {
            MaxAttempts = 2,
            Backoff = _ => TimeSpan.FromMilliseconds(1),
            RetryScope = RemoteRetryScope.Network
        };

        await Assert.ThrowsAsync<HttpRequestException>(
            () => transport.GetJsonAsync<SamplePayload>(new Uri("https://example.test/data"), options));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetJsonAsync_WhenScopeIsTransientAndServerRecovers_RetriesUntilSuccess()
    {
        var recordedDelays = new List<TimeSpan>();
        var handler = new ScriptedHandler(
            ScriptedHandler.Status(HttpStatusCode.InternalServerError),
            ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable),
            ScriptedHandler.Ok("""{"Name":"hello"}"""));
        var transport = CreateTransport(
            handler,
            delayAsync: (delay, _) =>
            {
                recordedDelays.Add(delay);
                return Task.CompletedTask;
            });
        var options = new RemoteRequestOptions
        {
            MaxAttempts = 3,
            Backoff = _ => TimeSpan.FromMilliseconds(1),
            RetryScope = RemoteRetryScope.Transient
        };

        var payload = await transport.GetJsonAsync<SamplePayload>(
            new Uri("https://example.test/data"), options);

        Assert.Equal("hello", payload?.Name);
        Assert.Equal(3, handler.RequestCount);
        Assert.Equal(2, recordedDelays.Count);
    }

    [Fact]
    public async Task GetJsonAsync_WhenScopeIsTransientAndStatusIsAuthoritative_DoesNotRetry()
    {
        var handler = new ScriptedHandler(ScriptedHandler.Status(HttpStatusCode.NotFound));
        var transport = CreateTransport(handler);
        var options = new RemoteRequestOptions
        {
            MaxAttempts = 3,
            Backoff = _ => TimeSpan.FromMilliseconds(1),
            RetryScope = RemoteRetryScope.Transient
        };

        await Assert.ThrowsAsync<HttpRequestException>(
            () => transport.GetJsonAsync<SamplePayload>(new Uri("https://example.test/data"), options));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetJsonAsync_WhenScopeIsNetworkAndBodyIsMalformed_DoesNotRetry()
    {
        var handler = new ScriptedHandler(ScriptedHandler.Ok("not json"));
        var transport = CreateTransport(handler);
        var options = new RemoteRequestOptions
        {
            MaxAttempts = 2,
            Backoff = _ => TimeSpan.FromMilliseconds(1),
            RetryScope = RemoteRetryScope.Network
        };

        await Assert.ThrowsAsync<JsonException>(
            () => transport.GetJsonAsync<SamplePayload>(new Uri("https://example.test/data"), options));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetJsonAsync_WhenScopeIsTransientAndBodyIsMalformed_RetriesUntilValid()
    {
        var handler = new ScriptedHandler(
            ScriptedHandler.Ok("<html>cdn error page</html>"),
            ScriptedHandler.Ok("""{"Name":"hello"}"""));
        var transport = CreateTransport(handler);
        var options = new RemoteRequestOptions
        {
            MaxAttempts = 2,
            Backoff = _ => TimeSpan.FromMilliseconds(1),
            RetryScope = RemoteRetryScope.Transient
        };

        var payload = await transport.GetJsonAsync<SamplePayload>(
            new Uri("https://example.test/data"), options);

        Assert.Equal("hello", payload?.Name);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetJsonAsync_WhenHttpClientTimesOut_RetriesUnderNetworkScope()
    {
        var handler = new ScriptedHandler(
            ScriptedHandler.Cancel(),
            ScriptedHandler.Ok("""{"Name":"hello"}"""));
        var transport = CreateTransport(handler);
        var options = new RemoteRequestOptions
        {
            MaxAttempts = 2,
            Backoff = _ => TimeSpan.FromMilliseconds(1),
            RetryScope = RemoteRetryScope.Network
        };

        var payload = await transport.GetJsonAsync<SamplePayload>(
            new Uri("https://example.test/data"), options);

        Assert.Equal("hello", payload?.Name);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetJsonAsync_WhenCallerIsAlreadyCancelled_DoesNotRetry()
    {
        var handler = new ScriptedHandler(ScriptedHandler.Ok("""{"Name":"hello"}"""));
        var transport = CreateTransport(handler);
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.GetJsonAsync<SamplePayload>(
                new Uri("https://example.test/data"),
                cancellationToken: cancellationTokenSource.Token));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GetStreamAsync_WhenFirstAttemptFails_RetriesUntilHeaders()
    {
        var handler = new ScriptedHandler(
            ScriptedHandler.Fail(new HttpRequestException("connection lost")),
            ScriptedHandler.Ok(""));
        var transport = CreateTransport(handler);
        var options = new RemoteRequestOptions
        {
            MaxAttempts = 2,
            Backoff = _ => TimeSpan.FromMilliseconds(1),
            RetryScope = RemoteRetryScope.Network
        };

        var remote = await transport.GetStreamAsync(
            new Uri("https://example.test/start"), options);
        using var body = remote.Content;

        Assert.Equal(2, handler.RequestCount);
    }

    // ---- Stream contract ----

    [Fact]
    public async Task GetStreamAsync_WhenContentLengthIsDeclared_ExposesDeclaredContentLength()
    {
        var transport = CreateTransport(new ScriptedHandler(ScriptedHandler.Ok("hello")));

        var remote = await transport.GetStreamAsync(new Uri("https://example.test/image.png"));
        using var body = remote.Content;
        var buffer = new Memory<byte>(new byte[16]);
        var read = await body.ReadAsync(buffer);

        Assert.Equal(5, remote.DeclaredContentLength);
        Assert.Equal("hello", Encoding.UTF8.GetString(buffer.Span[..read]));
    }

    [Fact]
    public async Task GetStreamAsync_WhenBodyStalls_ThrowsInsteadOfHanging()
    {
        // 守卫（停顿预算）：头部到达后正文再无字节，读取必须转为
        // HttpRequestException 而不是永久挂起。
        var transport = CreateTransport(
            new StalledBodyHandler(),
            idleReadTimeout: TimeSpan.FromMilliseconds(50));

        var remote = await transport.GetStreamAsync(new Uri("https://example.test/image.png"));
        using var body = remote.Content;

        await Assert.ThrowsAsync<HttpRequestException>(
            () => body.ReadAsync(new Memory<byte>(new byte[16])).AsTask());
    }

    [Fact]
    public async Task GetStreamAsync_WhenSyncReadIsRequested_Throws()
    {
        var transport = CreateTransport(new ScriptedHandler(ScriptedHandler.Ok("hello")));

        var remote = await transport.GetStreamAsync(new Uri("https://example.test/image.png"));
        using var body = remote.Content;

        Assert.Throws<NotSupportedException>(
            () => body.Read(new byte[16], 0, 16));
    }

    [Fact]
    public async Task GetStreamAsync_WhenServerErrorThenSuccess_DisposesFailedAttemptAndRetries()
    {
        var handler = new ScriptedHandler(
            ScriptedHandler.Status(HttpStatusCode.InternalServerError),
            ScriptedHandler.Ok(""));
        var transport = CreateTransport(handler);
        var options = new RemoteRequestOptions
        {
            MaxAttempts = 2,
            Backoff = _ => TimeSpan.FromMilliseconds(1),
            RetryScope = RemoteRetryScope.Transient
        };

        var remote = await transport.GetStreamAsync(
            new Uri("https://example.test/start"), options);
        using var body = remote.Content;

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetStreamAsync_WhenDisposedAsync_SubsequentReadThrows()
    {
        var transport = CreateTransport(new ScriptedHandler(ScriptedHandler.Ok("hello")));

        var remote = await transport.GetStreamAsync(new Uri("https://example.test/image.png"));
        var body = remote.Content;
        await body.DisposeAsync();

        await Assert.ThrowsAnyAsync<ObjectDisposedException>(
            () => body.ReadAsync(new Memory<byte>(new byte[16])).AsTask());
    }

    [Fact]
    public async Task GetStreamAsync_WhenUnsupportedStreamMembersAreUsed_Throws()
    {
        var transport = CreateTransport(new ScriptedHandler(ScriptedHandler.Ok("hello")));

        var remote = await transport.GetStreamAsync(new Uri("https://example.test/image.png"));
        using var body = remote.Content;

        Assert.False(body.CanSeek);
        Assert.Throws<NotSupportedException>(() => body.Length);
        Assert.Throws<NotSupportedException>(() => body.Position);
        Assert.Throws<NotSupportedException>(() => body.Position = 0);
        Assert.Throws<NotSupportedException>(() => body.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => body.SetLength(0));
        Assert.Throws<NotSupportedException>(() => body.Write(new byte[1], 0, 1));
    }

    // ---- Fakes and stubs ----

    private static RemoteHttpTransport CreateTransport(
        HttpMessageHandler handler,
        RemoteHttpUrlValidator? validator = null,
        IWebProxy? connectionProxy = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? idleReadTimeout = null,
        Action<HttpClient>? configureClient = null) =>
        new(
            new StubLeaseSource(handler, connectionProxy, configureClient),
            validator ?? RemoteHttpUrlValidator.CreateForTesting(),
            ProxyModes.Direct,
            delayAsync,
            idleReadTimeout);

    private sealed record SamplePayload(string Name);

    /// <summary>按调用方请求模式记账的租约源：连接代理可选，客户端可再配置。</summary>
    private sealed class StubLeaseSource(
        HttpMessageHandler handler,
        IWebProxy? connectionProxy = null,
        Action<HttpClient>? configureClient = null) : IHttpClientLeaseSource
    {
        private readonly HttpClient client = CreateClient(handler, configureClient);

        public List<string> RequestedProxyModes { get; } = [];

        public int LeaseCount { get; private set; }

        public Task<HttpClientLease> CreateLeaseAsync(
            string proxyMode,
            CancellationToken cancellationToken = default)
        {
            RequestedProxyModes.Add(proxyMode);
            LeaseCount++;
            return Task.FromResult(new HttpClientLease(client) { ConnectionProxy = connectionProxy });
        }

        public void Dispose() => client.Dispose();

        private static HttpClient CreateClient(HttpMessageHandler handler, Action<HttpClient>? configureClient)
        {
            var client = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            configureClient?.Invoke(client);
            return client;
        }
    }

    /// <summary>按脚本逐次应答的处理器；脚本耗尽后重复最后一项。记录请求计数与测试头。</summary>
    private sealed class ScriptedHandler(params Func<HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        private int next;

        public int RequestCount { get; private set; }

        public List<string?> TestHeaderValues { get; } = [];

        public static Func<HttpResponseMessage> Ok(string json) => () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        public static Func<HttpResponseMessage> Status(HttpStatusCode statusCode) =>
            () => new HttpResponseMessage(statusCode);

        public static Func<HttpResponseMessage> Fail(HttpRequestException reason) => () => throw reason;

        public static Func<HttpResponseMessage> Cancel() => () => throw new TaskCanceledException();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            request.Headers.TryGetValues("X-Test-Header", out var values);
            TestHeaderValues.Add(values is null ? null : string.Join(",", values));
            var step = steps[Math.Min(next, steps.Length - 1)];
            next++;
            return Task.FromResult(step());
        }
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Version? RequestVersion { get; private set; }
        public HttpVersionPolicy? RequestVersionPolicy { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestVersion = request.Version;
            RequestVersionPolicy = request.VersionPolicy;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers =
                {
                    Location = new Uri("http://localhost/private")
                }
            });
        }
    }

    private sealed class RelativeRedirectHandler : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.AbsoluteUri ?? "");
            return Task.FromResult(RequestUris.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("/final", UriKind.Relative) }
                }
                : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class SingleRedirectHandler(HttpStatusCode redirectStatusCode) : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.AbsoluteUri ?? "");
            return Task.FromResult(RequestUris.Count == 1
                ? new HttpResponseMessage(redirectStatusCode)
                {
                    Headers = { Location = new Uri("/final", UriKind.Relative) }
                }
                : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class MissingLocationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect));
    }

    private sealed class DowngradeRedirectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers =
                {
                    Location = new Uri("http://example.test/final")
                }
            });
    }

    private sealed class EndlessRedirectHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers =
                {
                    Location = new Uri($"/redirect-{RequestCount}", UriKind.Relative)
                }
            });
        }
    }

    private sealed class StalledBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NeverEndingStream())
            });
    }

    private sealed class NeverEndingStream : Stream
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
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>模拟配置了系统代理且目标未被旁路：所有请求经代理 URI 出网。</summary>
    private sealed class RoutingProxyStub : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        public Uri? GetProxy(Uri destination) => new("http://proxy.example.invalid:8080");

        public bool IsBypassed(Uri host) => false;
    }

    /// <summary>模拟无系统代理或目标被旁路：GetProxy 返回原 URI 且 IsBypassed 为真。</summary>
    private sealed class BypassingProxyStub : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        public Uri? GetProxy(Uri destination) => destination;

        public bool IsBypassed(Uri host) => true;
    }

    private sealed record StubProxy(bool IsBypassed, Uri Via) : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        Uri? IWebProxy.GetProxy(Uri destination) => Via;

        bool IWebProxy.IsBypassed(Uri host) => IsBypassed;
    }
}
