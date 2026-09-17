using System.Net;
using System.Text;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ResourcePanelServiceTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    public void Dispose()
    {
        try
        {
            tempDir.Dispose();
        }
        catch
        {
            // Best-effort cleanup of the per-test data directory.
        }
    }

    [Fact]
    public async Task LoadDataAsync_WhenOneParallelRequestFails_ThrowsInsteadOfReturningPartialResult()
    {
        var transport = new StubRemoteHttpTransport(uri => uri.AbsolutePath switch
        {
            "/status/list" => "{}",
            "/config/get" => throw new HttpRequestException(
                "stub transport answered InternalServerError",
                null,
                HttpStatusCode.InternalServerError),
            _ => "ok"
        });
        var service = await CreateServiceAsync(transport);

        // 并行语义：status 与 config 同时发出，config 失败时整体抛出（无部分结果）。
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.LoadDataAsync("UIDTESTA"));

        Assert.Equal(1, CountRequests(transport, "/status/list"));
        Assert.Equal(1, CountRequests(transport, "/config/get"));
    }

    [Fact]
    public async Task LoadDataAsync_WhenBothRequestsSucceed_MapsVersionsModesAndReadiness()
    {
        var statusJson = """
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
            """;
        var configJson = """{ "text": "cn", "voice": "jp", "media": "jp" }""";
        var transport = new StubRemoteHttpTransport(uri => uri.AbsolutePath switch
        {
            "/status/list" => statusJson,
            "/config/get" => configJson,
            _ => "ok"
        });
        var service = await CreateServiceAsync(transport);

        var result = await service.LoadDataAsync("UIDTESTA");

        // 装载结果与条目表按位对齐：0 = Text、1 = Voice、2 = Media（D11）。
        // 文本：版本一致 → 就绪；配置为 cn → 已启用。
        Assert.Equal("1.0.0", result[0].OfficialVersion);
        Assert.Equal("1.0.0", result[0].LocalizedVersion);
        Assert.True(result[0].IsReady);
        Assert.True(result[0].IsEnabled);
        // 语音：官方与本地化版本不同 → 等待中。
        Assert.False(result[1].IsReady);
        Assert.False(result[1].IsEnabled);
        // 媒体：空版本映射为 "--" 占位，两个占位按 Ordinal 相等 → 视为就绪（实现契约）。
        Assert.Equal("--", result[2].OfficialVersion);
        Assert.Equal("--", result[2].LocalizedVersion);
        Assert.True(result[2].IsReady);
        Assert.False(result[2].IsEnabled);
    }

    [Fact]
    public async Task SaveConfigAsync_WhenServerRejects_ThrowsHttpRequestException()
    {
        var transport = new StubRemoteHttpTransport(uri => uri.AbsolutePath switch
        {
            "/config/set" => throw new HttpRequestException(
                "stub transport answered InternalServerError",
                null,
                HttpStatusCode.InternalServerError),
            _ => "ok"
        });
        var service = await CreateServiceAsync(transport);

        // 保存失败按实现语义向上传播，由 ViewModel 层转为用户可见错误。
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.SaveConfigAsync("UIDTESTA", true, false, true));

        Assert.Equal(1, CountRequests(transport, "/config/set"));
    }

    [Fact]
    public async Task SaveConfigThenLoadData_WhenModesChange_RoundTripsEnabledFlags()
    {
        // 与上方 LoadData 测试同形的 status 应答：LoadDataAsync 并行拉取
        // status + config，两者都必须拿到合法 JSON。
        var statusJson = """
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
            """;
        var configJson = """{ "text": "jp", "voice": "jp", "media": "jp" }""";
        var transport = new StubRemoteHttpTransport(uri => uri.AbsolutePath switch
        {
            "/status/list" => statusJson,
            "/config/get" => configJson,
            _ => "ok"
        });
        var service = await CreateServiceAsync(transport);

        await service.SaveConfigAsync("UIDTESTA", textEnabled: true, voiceEnabled: false, mediaEnabled: true);

        // 保存序列化契约：true → cn，false → jp。
        Assert.Equal("?uid=UIDTESTA&text=cn&voice=jp&media=cn", LastConfigSetQuery(transport));

        // 模拟服务器按保存内容更新配置后再次读取，IsEnabled 应与保存值一致。
        configJson = """{ "text": "cn", "voice": "jp", "media": "cn" }""";
        var result = await service.LoadDataAsync("UIDTESTA");

        // 装载结果与条目表按位对齐（Text, Voice, Media）。
        Assert.True(result[0].IsEnabled);
        Assert.False(result[1].IsEnabled);
        Assert.True(result[2].IsEnabled);
    }

    [Fact]
    public async Task ResolveUidWithSourceAsync_WhenAutoSource_PrefersCookieOverSavedUid()
    {
        var service = await CreateServiceAsync(
            new StubRemoteHttpTransport(),
            cookieUid: "COOKIEAA",
            settings: new LauncherSettings { ResourcePanelUid = "SAVEDUID" });

        var uid = await service.ResolveUidWithSourceAsync(ResourcePanelUidSources.Auto);

        Assert.Equal("COOKIEAA", uid);
    }

    [Fact]
    public async Task ResolveUidWithSourceAsync_WhenCustomSource_PrefersSavedUidOverCookie()
    {
        var service = await CreateServiceAsync(
            new StubRemoteHttpTransport(),
            cookieUid: "COOKIEAA",
            settings: new LauncherSettings { ResourcePanelUid = "SAVEDUID" });

        var uid = await service.ResolveUidWithSourceAsync(ResourcePanelUidSources.Custom);

        Assert.Equal("SAVEDUID", uid);
    }

    [Fact]
    public async Task ResolveUidWithSourceAsync_WhenCustomSourceUidIsInvalid_FallsBackToCookie()
    {
        var service = await CreateServiceAsync(
            new StubRemoteHttpTransport(),
            cookieUid: "COOKIEAA",
            settings: new LauncherSettings { ResourcePanelUid = "bad" });

        var uid = await service.ResolveUidWithSourceAsync(ResourcePanelUidSources.Custom);

        // 回退链：custom 存的 UID 非法 → 回退到 cookie 自动检测。
        Assert.Equal("COOKIEAA", uid);
    }

    [Fact]
    public async Task SaveUidSourceAsync_ThenGetUidSourceAsync_RoundTripsPreferenceAndManualUid()
    {
        var service = await CreateServiceAsync(new StubRemoteHttpTransport());

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
        var savedSettings = new SavedSettingsTestRig(Path.Combine(blocker, "settings.json"));
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            savedSettings.SettingsService,
            savedSettings.Writer,
            Path.Combine(tempDir, "missing"));
        var service = new ResourcePanelService(
            uidService,
            new ResourcePanelApiClient(new StubRemoteHttpTransport()),
            new LocalDiagnostics());

        // 存储写入失败按实现语义原样传播（不做吞并或降级）。
        await Assert.ThrowsAsync<IOException>(
            () => service.SaveUidSourceAsync(ResourcePanelUidSources.Custom));
    }

    [Fact]
    public async Task SaveManualUidAsync_WhenUidHasInvalidFormat_ThrowsArgumentException()
    {
        var service = await CreateServiceAsync(new StubRemoteHttpTransport());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveManualUidAsync("bad-uid"));
    }

    private async Task<ResourcePanelService> CreateServiceAsync(
        StubRemoteHttpTransport transport,
        string? cookieUid = null,
        LauncherSettings? settings = null)
    {
        var cookiePath = Path.Combine(tempDir, $"Library-{Guid.NewGuid():N}");
        if (cookieUid is not null)
        {
            await BestHttpCookieLibraryFixture.WriteUidAsync(cookiePath, cookieUid);
        }

        var savedSettings = new SavedSettingsTestRig(
            tempDir.Sub(GamePaths.LauncherSettingsFileName));
        if (settings is not null)
        {
            await savedSettings.SeedAsync(settings);
        }

        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), savedSettings.SettingsService, savedSettings.Writer, cookiePath);
        return new ResourcePanelService(uidService, new ResourcePanelApiClient(transport), new LocalDiagnostics());
    }

    private static int CountRequests(StubRemoteHttpTransport transport, string path) =>
        transport.RequestedUris.Count(uri => uri.AbsolutePath == path);

    private static string? LastConfigSetQuery(StubRemoteHttpTransport transport) =>
        transport.RequestedUris
            .LastOrDefault(uri => uri.AbsolutePath == "/config/set")?.Query;
}
