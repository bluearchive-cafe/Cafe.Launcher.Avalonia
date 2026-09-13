using System.Net;
using System.Text;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public partial class MainWindowViewModelTests
{
    [Fact]
    public async Task OpenResourcePanelAsync_WhenCookieUidExists_LoadsStatusAndConfig()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await WriteResourcePanelCookieLibraryAsync(cookiePath, "UIDTESTA");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);
        var transport = CreateResourcePanelTransport();
        var apiClient = new ResourcePanelApiClient(transport);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, settingsService, uidService, apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Cafe });

        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);

        Assert.True(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.False(viewModel.ResourcePanel.IsResourcePanelUidMissing);
        Assert.Equal("UIDTESTA", viewModel.ResourcePanel.ResourcePanelUid);
        Assert.Equal(1, CountRequests(transport, "/status/list"));
        Assert.Equal(1, CountRequests(transport, "/config/get"));
        var text = viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Text);
        var voice = viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Voice);
        Assert.Equal(viewModel.Shell.I18n["resourcePanelReady"], text.StatusText);
        Assert.True(text.IsEnabled);
        Assert.Equal(viewModel.Shell.I18n["resourcePanelWaiting"], voice.StatusText);
        Assert.False(voice.IsEnabled);
    }

    [Fact]
    public async Task OpenResourcePanelAsync_WhenSourceIsNotCafe_ShowsConfirmBeforeOpening()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await WriteResourcePanelCookieLibraryAsync(cookiePath, "UIDTESTA");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);
        var transport = CreateResourcePanelTransport();
        var apiClient = new ResourcePanelApiClient(transport);
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            settingsService,
            uidService,
            apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Official });

        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);

        Assert.True(viewModel.Dialogs.ResourcePanelSourceConfirm.IsVisible);
        Assert.False(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.Equal(0, CountRequests(transport, "/status/list"));
        Assert.Equal(0, CountRequests(transport, "/config/get"));
    }

    [Fact]
    public async Task ConfirmResourcePanelSourceSwitch_WhenUidExists_SwitchesToCafeAndOpensPanel()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await WriteResourcePanelCookieLibraryAsync(cookiePath, "UIDTESTA");
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings
        {
            PatchUrlGroup = PatchUrlGroups.Official
        });
        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);
        var transport = CreateResourcePanelTransport();
        var apiClient = new ResourcePanelApiClient(transport);
        var snapshot = CreateSnapshot();
        snapshot.Settings.PatchUrlGroup = PatchUrlGroups.Cafe;
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            settingsService,
            uidService,
            apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Official });
        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);

        viewModel.Dialogs.ResourcePanelSourceConfirm.ConfirmCommand.Execute(null);
        await WaitForConditionAsync(() =>
            viewModel.ResourcePanel.IsResourcePanelVisible
            && CountRequests(transport, "/status/list") == 1
            && CountRequests(transport, "/config/get") == 1);

        Assert.False(viewModel.Dialogs.ResourcePanelSourceConfirm.IsVisible);
        Assert.True(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.Equal(PatchUrlGroups.Cafe, viewModel.Settings.Editor.Current.PatchUrlGroup);
        Assert.Equal(PatchUrlGroups.Cafe, (await settingsService.ReadAsync()).PatchUrlGroup);
        Assert.Equal(1, CountRequests(transport, "/config/get"));
    }

    [Fact]
    public async Task ResourcePanelApplySettings_UsesCafeSourceAndSystemProxyWhenOpeningPanel()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var proxyEndpoint = (IPEndPoint)listener.LocalEndpoint;
        var proxySettings = new ProxySettingsService(() => new SystemProxySettings(
            $"http://127.0.0.1:{proxyEndpoint.Port}",
            []));
        using var clientFactory = new HttpClientFactory(proxySettings);
        // 代理租约与出口校验属于传输层：用真实 RemoteHttpTransport 验证面板请求经系统代理发出。
        var transport = new RemoteHttpTransport(
            clientFactory,
            new RemoteHttpUrlValidator(),
            () => ProxyModes.System);
        var apiClient = new ResourcePanelApiClient(transport);
        var settingsService = new LauncherSettingsService(
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ResourcePanelUid = "UIDTESTA" });
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            settingsService,
            uidService,
            apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings
        {
            ProxyMode = ProxyModes.System,
            PatchUrlGroup = PatchUrlGroups.Cafe
        });
        var proxyConnection = listener.AcceptTcpClientAsync();

        var openTask = viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);
        using var acceptedClient = await proxyConnection.WaitAsync(TimeSpan.FromSeconds(5));
        acceptedClient.Close();
        listener.Stop();
        await openTask;

        Assert.True(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.False(viewModel.Dialogs.ResourcePanelSourceConfirm.IsVisible);
    }

    [Fact]
    public async Task SaveResourcePanelAsync_SendsCnForEnabledAndJpForDisabled()
    {
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ResourcePanelUid = "UIDTESTA" });
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));
        var transport = CreateResourcePanelTransport();
        var apiClient = new ResourcePanelApiClient(transport);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, settingsService, uidService, apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Cafe });
        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);
        viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Text).IsEnabled = true;
        viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Voice).IsEnabled = false;
        viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Media).IsEnabled = true;

        await viewModel.ResourcePanel.SaveResourcePanelCommand.ExecuteAsync(null);

        var setRequest = Assert.Single(
            transport.RequestedUris,
            uri => uri.AbsolutePath == "/config/set");
        Assert.Equal("/config/set?uid=UIDTESTA&text=cn&voice=jp&media=cn", setRequest.PathAndQuery);
        Assert.Equal(1, CountRequests(transport, "/config/set"));
    }

    [Fact]
    public async Task OpenResourcePanelAsync_WhenUidMissing_ShowsManualInputAndSkipsApiCalls()
    {
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));
        var transport = CreateResourcePanelTransport();
        var apiClient = new ResourcePanelApiClient(transport);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, settingsService, uidService, apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Cafe });

        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);

        Assert.True(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.True(viewModel.ResourcePanel.IsResourcePanelUidMissing);
        Assert.Equal("", viewModel.ResourcePanel.ResourcePanelUid);
        Assert.Equal(0, CountRequests(transport, "/status/list"));
        Assert.Equal(0, CountRequests(transport, "/config/get"));
        Assert.Equal(0, CountRequests(transport, "/config/set"));
    }

    [Fact]
    public async Task SaveManualResourcePanelUidAsync_WhenUidIsBlank_ShowsValidationMessage()
    {
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));
        var transport = CreateResourcePanelTransport();
        var apiClient = new ResourcePanelApiClient(transport);
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            settingsService,
            uidService,
            apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Cafe });
        viewModel.ResourcePanel.ManualResourcePanelUid = "   ";

        await viewModel.ResourcePanel.SaveManualResourcePanelUidCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.Shell.I18n["resourcePanelUidEmpty"], viewModel.ResourcePanel.ResourcePanelMessage);
        Assert.Equal(0, CountRequests(transport, "/status/list"));
        Assert.Equal(0, CountRequests(transport, "/config/get"));
        Assert.Equal(0, CountRequests(transport, "/config/set"));
    }

    private static async Task WriteResourcePanelCookieLibraryAsync(string path, string uid)
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

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token);
        }
    }

    private static int CountRequests(StubRemoteHttpTransport transport, string path) =>
        transport.RequestedUris.Count(uri => uri.AbsolutePath == path);

    /// <summary>按路径应答资源面板端点的共享替身；/config/set 以 "ok" 正文应答流式读取。</summary>
    private static StubRemoteHttpTransport CreateResourcePanelTransport() => new(uri => uri.AbsolutePath switch
    {
        "/status/list" =>
            """
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
                "official": { "version": "3.0.0" },
                "localized": { "version": "3.0.0" }
              }
            }
            """,
        "/config/get" =>
            """
            {
              "text": "cn",
              "voice": "jp",
              "media": "cn"
            }
            """,
        _ => "ok"
    });
}
