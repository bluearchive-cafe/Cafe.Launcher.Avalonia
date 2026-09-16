using System.Net;
using System.Text;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Tests;

public partial class MainWindowViewModelTests
{
    [Fact]
    public async Task OpenResourcePanelAsync_WhenCookieUidExists_LoadsStatusAndConfig()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await BestHttpCookieLibraryFixture.WriteUidAsync(cookiePath, "UIDTESTA");
        var savedSettings = new SavedSettingsTestRig(tempDir.Sub(GamePaths.LauncherSettingsFileName));
        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), savedSettings.SettingsService, savedSettings.Writer, cookiePath);
        var transport = CreateResourcePanelTransport();
        var apiClient = new ResourcePanelApiClient(transport);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, savedSettings, uidService, apiClient);
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
        await BestHttpCookieLibraryFixture.WriteUidAsync(cookiePath, "UIDTESTA");
        var savedSettings = new SavedSettingsTestRig(tempDir.Sub(GamePaths.LauncherSettingsFileName));
        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), savedSettings.SettingsService, savedSettings.Writer, cookiePath);
        var transport = CreateResourcePanelTransport();
        var apiClient = new ResourcePanelApiClient(transport);
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            savedSettings,
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
        await BestHttpCookieLibraryFixture.WriteUidAsync(cookiePath, "UIDTESTA");
        var settingsPath = tempDir.Sub(GamePaths.LauncherSettingsFileName);
        var savedSettings = new SavedSettingsTestRig(settingsPath);
        await savedSettings.SeedAsync(new LauncherSettings
        {
            PatchUrlGroup = PatchUrlGroups.Official
        });
        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), savedSettings.SettingsService, savedSettings.Writer, cookiePath);
        var transport = CreateResourcePanelTransport();
        var apiClient = new ResourcePanelApiClient(transport);
        var snapshot = CreateSnapshot();
        snapshot.Settings.PatchUrlGroup = PatchUrlGroups.Cafe;
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            savedSettings,
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
        Assert.Equal(PatchUrlGroups.Cafe, (await savedSettings.SettingsService.ReadAsync()).PatchUrlGroup);
        Assert.Equal(1, CountRequests(transport, "/config/get"));
    }

    /// <summary>
    /// 呈现层：以系统代理打开面板不引入任何二次确认（源为 Cafe 时直接打开）。
    /// 「请求真的经系统代理发出」属于传输层，由
    /// <c>RemoteHttpTransportTests.GetJsonAsync_WhenSystemProxyConfigured_DialsTheProxyAndReadsItsAnswer</c>
    /// 用回环代理覆盖——此前这条用例让监听器接受连接后立刻断开，再等真实传输的重试退避走完，
    /// 一次约 10 秒；拆开后两边都在确定的结果上收口。
    /// </summary>
    [Fact]
    public async Task ResourcePanelApplySettings_WhenSystemProxyAndCafeSource_OpensPanelWithoutSourceConfirm()
    {
        var savedSettings = new SavedSettingsTestRig(
            tempDir.Sub(GamePaths.LauncherSettingsFileName));
        await savedSettings.SeedAsync(new LauncherSettings { ResourcePanelUid = "UIDTESTA" });
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            savedSettings.SettingsService,
            savedSettings.Writer,
            Path.Combine(tempDir, "missing"));
        var transport = CreateResourcePanelTransport();
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            savedSettings,
            uidService,
            new ResourcePanelApiClient(transport));
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings
        {
            ProxyMode = ProxyModes.System,
            PatchUrlGroup = PatchUrlGroups.Cafe
        });

        await viewModel.ResourcePanel.OpenResourcePanelCommand
            .ExecuteAsync(null)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.False(viewModel.Dialogs.ResourcePanelSourceConfirm.IsVisible);
        Assert.Equal(1, CountRequests(transport, "/status/list"));
        Assert.Equal(1, CountRequests(transport, "/config/get"));
    }

    [Fact]
    public async Task SaveResourcePanelAsync_SendsCnForEnabledAndJpForDisabled()
    {
        var savedSettings = new SavedSettingsTestRig(tempDir.Sub(GamePaths.LauncherSettingsFileName));
        await savedSettings.SeedAsync(new LauncherSettings { ResourcePanelUid = "UIDTESTA" });
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            savedSettings.SettingsService,
            savedSettings.Writer,
            Path.Combine(tempDir, "missing"));
        var transport = CreateResourcePanelTransport();
        var apiClient = new ResourcePanelApiClient(transport);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, savedSettings, uidService, apiClient);
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
        var savedSettings = new SavedSettingsTestRig(tempDir.Sub(GamePaths.LauncherSettingsFileName));
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            savedSettings.SettingsService,
            savedSettings.Writer,
            Path.Combine(tempDir, "missing"));
        var transport = CreateResourcePanelTransport();
        var apiClient = new ResourcePanelApiClient(transport);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, savedSettings, uidService, apiClient);
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
        var savedSettings = new SavedSettingsTestRig(tempDir.Sub(GamePaths.LauncherSettingsFileName));
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            savedSettings.SettingsService,
            savedSettings.Writer,
            Path.Combine(tempDir, "missing"));
        var transport = CreateResourcePanelTransport();
        var apiClient = new ResourcePanelApiClient(transport);
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            savedSettings,
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

    private static Task WaitForConditionAsync(Func<bool> condition) =>
        TestWait.UntilAsync(condition, TimeSpan.FromSeconds(2), "Resource panel command did not settle.");

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
