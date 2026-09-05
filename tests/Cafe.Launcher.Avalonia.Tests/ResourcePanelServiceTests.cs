using System.Net;
using System.Text;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ResourcePanelServiceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ResourcePanelServiceTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of the per-test data directory.
        }
    }

    [Fact]
    public async Task LoadDataAsync_WhenOneParallelRequestFails_ThrowsInsteadOfReturningPartialResult()
    {
        var handler = new FakeResourcePanelHandler { ConfigGetStatusCode = HttpStatusCode.InternalServerError };
        var service = await CreateServiceAsync(handler);

        // 并行语义：status 与 config 同时发出，config 失败时整体抛出（无部分结果）。
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.LoadDataAsync("UIDTESTA", ProxyModes.Direct));

        Assert.Equal(1, handler.StatusListCount);
        Assert.Equal(1, handler.ConfigGetCount);
    }

    [Fact]
    public async Task LoadDataAsync_WhenBothRequestsSucceed_MapsVersionsModesAndReadiness()
    {
        var handler = new FakeResourcePanelHandler
        {
            StatusJson = """
            {
              "text": {
                "official": { "version": "1.0.0" },
                "localized": { "version": "1.0.0" }
              },
              "voice": {
                "official": { "version": "2.0.0" },
                "localized": { "version": "2.1.0" }
              },
              "media": {
                "official": { "version": "" },
                "localized": { "version": "" }
              }
            }
            """,
            ConfigJson = """{ "text": "cn", "voice": "jp", "media": "jp" }"""
        };
        var service = await CreateServiceAsync(handler);

        var result = await service.LoadDataAsync("UIDTESTA", ProxyModes.Direct);

        // 文本：版本一致 → 就绪；配置为 cn → 已启用。
        Assert.Equal("1.0.0", result.Text.OfficialVersion);
        Assert.Equal("1.0.0", result.Text.LocalizedVersion);
        Assert.True(result.Text.IsReady);
        Assert.True(result.Text.IsEnabled);
        // 语音：官方与本地化版本不同 → 等待中。
        Assert.False(result.Voice.IsReady);
        Assert.False(result.Voice.IsEnabled);
        // 媒体：空版本映射为 "--" 占位，两个占位按 Ordinal 相等 → 视为就绪（实现契约）。
        Assert.Equal("--", result.Media.OfficialVersion);
        Assert.Equal("--", result.Media.LocalizedVersion);
        Assert.True(result.Media.IsReady);
        Assert.False(result.Media.IsEnabled);
    }

    [Fact]
    public async Task SaveConfigAsync_WhenServerRejects_ThrowsHttpRequestException()
    {
        var handler = new FakeResourcePanelHandler { ConfigSetStatusCode = HttpStatusCode.InternalServerError };
        var service = await CreateServiceAsync(handler);

        // 保存失败按实现语义向上传播，由 ViewModel 层转为用户可见错误。
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.SaveConfigAsync("UIDTESTA", true, false, true, ProxyModes.Direct));

        Assert.Equal(1, handler.ConfigSetCount);
    }

    [Fact]
    public async Task SaveConfigThenLoadData_WhenModesChange_RoundTripsEnabledFlags()
    {
        var handler = new FakeResourcePanelHandler
        {
            ConfigJson = """{ "text": "jp", "voice": "jp", "media": "jp" }"""
        };
        var service = await CreateServiceAsync(handler);

        await service.SaveConfigAsync("UIDTESTA", textEnabled: true, voiceEnabled: false, mediaEnabled: true, ProxyModes.Direct);

        // 保存序列化契约：true → cn，false → jp。
        Assert.Equal("?uid=UIDTESTA&text=cn&voice=jp&media=cn", handler.LastConfigSetQuery);

        // 模拟服务器按保存内容更新配置后再次读取，IsEnabled 应与保存值一致。
        handler.ConfigJson = """{ "text": "cn", "voice": "jp", "media": "cn" }""";
        var result = await service.LoadDataAsync("UIDTESTA", ProxyModes.Direct);

        Assert.True(result.Text.IsEnabled);
        Assert.False(result.Voice.IsEnabled);
        Assert.True(result.Media.IsEnabled);
    }

    [Fact]
    public async Task ResolveUidWithSourceAsync_WhenAutoSource_PrefersCookieOverSavedUid()
    {
        var service = await CreateServiceAsync(
            new FakeResourcePanelHandler(),
            cookieUid: "COOKIEAA",
            settings: new LauncherSettings { ResourcePanelUid = "SAVEDUID" });

        var uid = await service.ResolveUidWithSourceAsync(ResourcePanelUidSources.Auto);

        Assert.Equal("COOKIEAA", uid);
    }

    [Fact]
    public async Task ResolveUidWithSourceAsync_WhenCustomSource_PrefersSavedUidOverCookie()
    {
        var service = await CreateServiceAsync(
            new FakeResourcePanelHandler(),
            cookieUid: "COOKIEAA",
            settings: new LauncherSettings { ResourcePanelUid = "SAVEDUID" });

        var uid = await service.ResolveUidWithSourceAsync(ResourcePanelUidSources.Custom);

        Assert.Equal("SAVEDUID", uid);
    }

    [Fact]
    public async Task ResolveUidWithSourceAsync_WhenCustomSourceUidIsInvalid_FallsBackToCookie()
    {
        var service = await CreateServiceAsync(
            new FakeResourcePanelHandler(),
            cookieUid: "COOKIEAA",
            settings: new LauncherSettings { ResourcePanelUid = "bad" });

        var uid = await service.ResolveUidWithSourceAsync(ResourcePanelUidSources.Custom);

        // 回退链：custom 存的 UID 非法 → 回退到 cookie 自动检测。
        Assert.Equal("COOKIEAA", uid);
    }

    [Fact]
    public async Task SaveUidSourceAsync_ThenGetUidSourceAsync_RoundTripsPreferenceAndManualUid()
    {
        var service = await CreateServiceAsync(new FakeResourcePanelHandler());

        await service.SaveUidSourceAsync(ResourcePanelUidSources.Custom);
        await service.SaveManualUidAsync("MANUALAA");

        Assert.Equal(ResourcePanelUidSources.Custom, await service.GetUidSourceAsync());
        // 无 cookie 时 auto 解析读取已保存的手动 UID。
        Assert.Equal("MANUALAA", await service.ResolveUidWithSourceAsync(ResourcePanelUidSources.Auto));
    }

    [Fact]
    public async Task SaveUidSourceAsync_WhenSettingsWriteFails_PropagatesStorageException()
    {
        // 目标目录被同名文件占用 → AtomicJsonFileStore 的 CreateDirectory 抛出。
        var blocker = Path.Combine(tempDir, "blocker");
        await File.WriteAllTextAsync(blocker, "not a directory");
        var settingsService = new LauncherSettingsService(Path.Combine(blocker, "settings.json"));
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));
        var service = new ResourcePanelService(
            uidService,
            new ResourcePanelApiClient(new FakeResourcePanelHandler()),
            new LocalDiagnostics());

        // 存储写入失败按实现语义原样传播（不做吞并或降级）。
        await Assert.ThrowsAsync<IOException>(
            () => service.SaveUidSourceAsync(ResourcePanelUidSources.Custom));
    }

    [Fact]
    public async Task SaveManualUidAsync_WhenUidHasInvalidFormat_ThrowsArgumentException()
    {
        var service = await CreateServiceAsync(new FakeResourcePanelHandler());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveManualUidAsync("bad-uid"));
    }

    private async Task<ResourcePanelService> CreateServiceAsync(
        FakeResourcePanelHandler handler,
        string? cookieUid = null,
        LauncherSettings? settings = null)
    {
        var cookiePath = Path.Combine(tempDir, $"Library-{Guid.NewGuid():N}");
        if (cookieUid is not null)
        {
            await WriteCookieLibraryAsync(cookiePath, cookieUid);
        }

        var settingsService = new LauncherSettingsService(
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        if (settings is not null)
        {
            await settingsService.SaveAsync(settings);
        }

        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);
        return new ResourcePanelService(uidService, new ResourcePanelApiClient(handler), new LocalDiagnostics());
    }

    private static async Task WriteCookieLibraryAsync(string path, string uid)
    {
        await using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(1);
        writer.Write(1);
        writer.Write(1);
        writer.Write("uid");
        writer.Write(uid);
        writer.Write(DateTime.UtcNow.ToBinary());
        writer.Write(DateTime.UtcNow.ToBinary());
        writer.Write(DateTime.FromBinary(0).ToBinary());
        writer.Write(2147483647L);
        writer.Write(false);
        writer.Write("bluearchive.cafe");
        writer.Write("/");
        writer.Write(false);
        writer.Write(false);
        writer.Flush();
    }

    /// <summary>
    /// 按路径分发的假 API handler：可用可空状态码注入失败（非 2xx 不触发重试，用例保持快速确定），
    /// 并记录 /config/set 的精确查询串。
    /// </summary>
    private sealed class FakeResourcePanelHandler : HttpMessageHandler
    {
        public string StatusJson { get; set; } = "{}";
        public string ConfigJson { get; set; } = "{}";
        public HttpStatusCode? StatusStatusCode { get; set; }
        public HttpStatusCode? ConfigGetStatusCode { get; set; }
        public HttpStatusCode? ConfigSetStatusCode { get; set; }

        public int StatusListCount { get; private set; }
        public int ConfigGetCount { get; private set; }
        public int ConfigSetCount { get; private set; }
        public string? LastConfigSetQuery { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path == "/status/list")
            {
                StatusListCount++;
                return Task.FromResult(Respond(StatusStatusCode, StatusJson));
            }

            if (path == "/config/get")
            {
                ConfigGetCount++;
                return Task.FromResult(Respond(ConfigGetStatusCode, ConfigJson));
            }

            if (path == "/config/set")
            {
                ConfigSetCount++;
                LastConfigSetQuery = request.RequestUri?.Query;
                return Task.FromResult(Respond(ConfigSetStatusCode, "{}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Respond(HttpStatusCode? statusCode, string json) =>
            statusCode is { } code
                ? new HttpResponseMessage(code)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
    }
}
