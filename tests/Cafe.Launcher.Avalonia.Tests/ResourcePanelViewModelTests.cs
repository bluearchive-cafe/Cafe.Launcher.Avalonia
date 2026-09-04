using System.Net;
using System.Text;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class ResourcePanelViewModelTests
{
    static ResourcePanelViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public async Task OpenResourcePanel_WhileStatusRequestPending_ShowsLoadingStateThenLoadedItems()
    {
        using var context = await CreateContextAsync(
            cookieUid: "UIDTESTA",
            configure: handler =>
            {
                handler.GateStatus = true;
                handler.StatusJson = """
                {
                  "text": {
                    "official": { "version": "1.0.0" },
                    "localized": { "version": "1.0.0" }
                  },
                  "voice": {
                    "official": { "version": "2.0.0" },
                    "localized": { "version": "2.1.0" }
                  }
                }
                """;
                handler.ConfigJson = """{ "text": "cn", "voice": "jp", "media": "jp" }""";
            });
        context.ViewModel.ApplySettings(new LauncherSettings
        {
            PatchUrlGroup = PatchUrlGroups.Cafe,
            ProxyMode = ProxyModes.Direct
        });

        var openTask = context.ViewModel.OpenResourcePanelCommand.ExecuteAsync(null);

        // 加载中：面板可见、忙碌、消息与三项状态均为 Loading，且此时不可保存。
        await WaitUntilAsync(() => context.ViewModel.IsResourcePanelBusy
            && context.ViewModel.ResourcePanelMessage == context.Localizer.T(LocalizationKeys.ResourcePanelLoading)
            && context.ViewModel.ResourcePanelItems.All(item => item.Status == ResourcePanelItemStatus.Loading));
        Assert.True(context.ViewModel.IsResourcePanelVisible);
        Assert.False(context.ViewModel.IsResourcePanelSaveEnabled);

        context.Handler.ReleaseStatus();
        await openTask.WaitAsync(TimeSpan.FromSeconds(5));

        // 加载完成：忙碌清除、UID 就位、文本就绪/启用、语音等待/停用、保存可用。
        Assert.False(context.ViewModel.IsResourcePanelBusy);
        Assert.Equal("UIDTESTA", context.ViewModel.ResourcePanelUid);
        Assert.Equal(
            context.Localizer.F(LocalizationKeys.ResourcePanelCurrentUid, "UIDTESTA"),
            context.ViewModel.ResourcePanelUidText);
        Assert.Equal(
            context.Localizer.T(LocalizationKeys.StatusNetworkLoaded),
            context.ViewModel.ResourcePanelMessage);
        var text = GetItem(context.ViewModel, ResourcePanelResourceCodes.Text);
        Assert.Equal(ResourcePanelItemStatus.Ready, text.Status);
        Assert.True(text.IsEnabled);
        Assert.Equal("CheckCircle", text.StatusIconKind);
        var voice = GetItem(context.ViewModel, ResourcePanelResourceCodes.Voice);
        Assert.Equal(ResourcePanelItemStatus.Waiting, voice.Status);
        Assert.False(voice.IsEnabled);
        Assert.Equal("ClockOutline", voice.StatusIconKind);
        Assert.True(context.ViewModel.IsResourcePanelSaveEnabled);
        Assert.True(context.ViewModel.IsResourcePanelVisible);
    }

    [Fact]
    public async Task OpenResourcePanel_WhenSourceIsNotCafe_RaisesConfirmAndSkipsApi()
    {
        using var context = await CreateContextAsync(cookieUid: "UIDTESTA");
        context.ViewModel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Official });
        var confirmRequested = false;
        context.ViewModel.ResourcePanelSourceConfirmRequested += () => confirmRequested = true;

        await context.ViewModel.OpenResourcePanelCommand.ExecuteAsync(null);

        // 命令守卫：非 Cafe 源只弹确认，不打开面板也不发任何 API 请求。
        Assert.True(confirmRequested);
        Assert.False(context.ViewModel.IsResourcePanelVisible);
        Assert.Equal(0, context.Handler.StatusListCount);
        Assert.Equal(0, context.Handler.ConfigGetCount);
        Assert.Equal(0, context.Handler.ConfigSetCount);
    }

    [Fact]
    public async Task OpenResourcePanel_WhenUidMissing_ShowsManualInputAndSkipsApi()
    {
        using var context = await CreateContextAsync();
        context.ViewModel.ApplySettings(new LauncherSettings
        {
            PatchUrlGroup = PatchUrlGroups.Cafe,
            ProxyMode = ProxyModes.Direct
        });

        await context.ViewModel.OpenResourcePanelCommand.ExecuteAsync(null);

        Assert.True(context.ViewModel.IsResourcePanelVisible);
        Assert.True(context.ViewModel.IsResourcePanelUidMissing);
        Assert.False(context.ViewModel.IsResourcePanelUidPresent);
        Assert.Equal("", context.ViewModel.ResourcePanelUid);
        Assert.Equal("", context.ViewModel.ResourcePanelUidText);
        Assert.Equal(
            context.Localizer.F(LocalizationKeys.ResourcePanelUidMissing, context.Service.CookieLibraryPath),
            context.ViewModel.ResourcePanelMessage);
        Assert.False(context.ViewModel.IsResourcePanelSaveEnabled);
        Assert.Equal(0, context.Handler.StatusListCount);
        Assert.Equal(0, context.Handler.ConfigGetCount);
    }

    [Fact]
    public async Task OpenResourcePanel_WhenApiFails_MarksItemsFailedAndDisablesSave()
    {
        using var context = await CreateContextAsync(
            cookieUid: "UIDTESTA",
            configure: handler => handler.ConfigGetStatusCode = HttpStatusCode.InternalServerError);
        context.ViewModel.ApplySettings(new LauncherSettings
        {
            PatchUrlGroup = PatchUrlGroups.Cafe,
            ProxyMode = ProxyModes.Direct
        });

        await context.ViewModel.OpenResourcePanelCommand.ExecuteAsync(null);

        // 失败反馈路径：忙碌清除、消息为格式化错误、三项进入 Failed 且版本归零、保存不可用。
        Assert.False(context.ViewModel.IsResourcePanelBusy);
        Assert.StartsWith(
            LocalizedPrefix(context.Localizer, LocalizationKeys.ResourcePanelLoadFailed),
            context.ViewModel.ResourcePanelMessage,
            StringComparison.Ordinal);
        foreach (var item in context.ViewModel.ResourcePanelItems)
        {
            Assert.Equal(ResourcePanelItemStatus.Failed, item.Status);
            Assert.Equal("AlertCircle", item.StatusIconKind);
            Assert.Equal(context.Localizer.T(LocalizationKeys.ResourcePanelFailed), item.StatusText);
            Assert.Equal("--", item.OfficialVersion);
            Assert.Equal("--", item.LocalizedVersion);
        }

        Assert.False(context.ViewModel.IsResourcePanelSaveEnabled);
        Assert.Equal(1, context.Handler.StatusListCount);
        Assert.Equal(1, context.Handler.ConfigGetCount);
    }

    [Fact]
    public async Task SaveManualResourcePanelUid_WhenUidIsValid_PersistsUidSwitchesToCustomAndReloads()
    {
        using var context = await CreateContextAsync();
        context.ViewModel.ApplySettings(new LauncherSettings
        {
            PatchUrlGroup = PatchUrlGroups.Cafe,
            ProxyMode = ProxyModes.Direct
        });
        context.ViewModel.ManualResourcePanelUid = " MANUALAA ";

        await context.ViewModel.SaveManualResourcePanelUidCommand.ExecuteAsync(null);

        Assert.Equal("MANUALAA", context.ViewModel.ResourcePanelUid);
        Assert.Equal(ResourcePanelUidSources.Custom, context.ViewModel.SelectedResourcePanelUidSource);
        Assert.True(context.ViewModel.IsResourcePanelUidSourceCustom);
        Assert.False(context.ViewModel.IsResourcePanelUidMissing);
        Assert.False(context.ViewModel.IsResourcePanelUidEditing);
        Assert.True(context.ViewModel.IsResourcePanelUidPresent);
        // 保存后立即重载面板数据：最终消息为重载完成提示（UidSaved 是被覆盖的瞬态）。
        Assert.Equal(
            context.Localizer.T(LocalizationKeys.StatusNetworkLoaded),
            context.ViewModel.ResourcePanelMessage);
        // 设置持久化：手动 UID 与 custom 偏好都落盘。
        var saved = await context.SettingsService.ReadAsync();
        Assert.Equal("MANUALAA", saved.ResourcePanelUid);
        Assert.Equal(ResourcePanelUidSources.Custom, saved.ResourcePanelUidSource);
        // 保存后用新 UID 重载了面板数据。
        Assert.Equal(1, context.Handler.StatusListCount);
        Assert.Equal(1, context.Handler.ConfigGetCount);
    }

    [Fact]
    public async Task SaveResourcePanel_WhenItemsToggled_SendsCnJpModesAndRaisesSuccessToast()
    {
        using var context = await CreateContextAsync(cookieUid: "UIDTESTA");
        context.ViewModel.ApplySettings(new LauncherSettings
        {
            PatchUrlGroup = PatchUrlGroups.Cafe,
            ProxyMode = ProxyModes.Direct
        });
        await context.ViewModel.OpenResourcePanelCommand.ExecuteAsync(null);
        GetItem(context.ViewModel, ResourcePanelResourceCodes.Text).IsEnabled = true;
        GetItem(context.ViewModel, ResourcePanelResourceCodes.Voice).IsEnabled = false;
        GetItem(context.ViewModel, ResourcePanelResourceCodes.Media).IsEnabled = true;
        ToastNotification? toast = null;
        context.ToastService.ToastRaised += notification => toast = notification;

        await context.ViewModel.SaveResourcePanelCommand.ExecuteAsync(null);

        Assert.Equal("?uid=UIDTESTA&text=cn&voice=jp&media=cn", context.Handler.LastConfigSetQuery);
        Assert.Equal(
            context.Localizer.T(LocalizationKeys.ResourcePanelSaved),
            context.ViewModel.ResourcePanelMessage);
        Assert.NotNull(toast);
        Assert.Equal(ToastSeverity.Success, toast!.Severity);
        Assert.False(context.ViewModel.IsResourcePanelBusy);
    }

    [Fact]
    public async Task SaveResourcePanel_WhenApiThrows_ReportsThroughErrorHandlingAndClearsBusy()
    {
        using var context = await CreateContextAsync(
            cookieUid: "UIDTESTA",
            configure: handler => handler.ConfigSetStatusCode = HttpStatusCode.InternalServerError);
        context.ViewModel.ApplySettings(new LauncherSettings
        {
            PatchUrlGroup = PatchUrlGroups.Cafe,
            ProxyMode = ProxyModes.Direct
        });
        await context.ViewModel.OpenResourcePanelCommand.ExecuteAsync(null);
        var toastCount = 0;
        context.ToastService.ToastRaised += _ => toastCount++;

        await context.ViewModel.SaveResourcePanelCommand.ExecuteAsync(null);

        // 保存失败：成功 toast 不弹，消息走 saveFailed 格式，并经 IErrorHandlingService 上报
        // （toast 内容与行内消息一致）。
        Assert.Equal(1, context.Handler.ConfigSetCount);
        Assert.Equal(0, toastCount);
        Assert.StartsWith(
            LocalizedPrefix(context.Localizer, LocalizationKeys.ResourcePanelSaveFailed),
            context.ViewModel.ResourcePanelMessage,
            StringComparison.Ordinal);
        Assert.Equal(1, context.ErrorHandling.HandleErrorCount);
        Assert.Equal("Resource panel save failed.", context.ErrorHandling.LastContext);
        Assert.Equal(context.ViewModel.ResourcePanelMessage, context.ErrorHandling.LastOptions?.ToastMessage);
        Assert.False(context.ViewModel.IsResourcePanelBusy);
    }

    [Fact]
    public async Task SetUidSource_WhenUserSelectsCustom_PersistsPreferenceAndReloads()
    {
        using var context = await CreateContextAsync(cookieUid: "UIDTESTA");
        context.ViewModel.ApplySettings(new LauncherSettings
        {
            PatchUrlGroup = PatchUrlGroups.Cafe,
            ProxyMode = ProxyModes.Direct
        });
        await context.ViewModel.OpenResourcePanelCommand.ExecuteAsync(null);
        Assert.Equal(1, context.Handler.ConfigGetCount);

        // 属性变更即触发 SetUidSourceCommand（isLoadingSource 守卫确保加载期间的赋值不触发）。
        context.ViewModel.SelectedResourcePanelUidSource = ResourcePanelUidSources.Custom;

        await WaitUntilAsync(() => context.Handler.ConfigGetCount == 2 && !context.ViewModel.IsResourcePanelBusy);

        var saved = await context.SettingsService.ReadAsync();
        Assert.Equal(ResourcePanelUidSources.Custom, saved.ResourcePanelUidSource);
        Assert.True(context.ViewModel.IsResourcePanelUidSourceCustom);
        // custom 存档 UID 为空 → 回退到 cookie 自动检测，UID 保持可用。
        Assert.Equal("UIDTESTA", context.ViewModel.ResourcePanelUid);
        Assert.False(context.ViewModel.IsResourcePanelUidMissing);
    }

    private static ResourcePanelItem GetItem(ResourcePanelViewModel viewModel, string code) =>
        viewModel.ResourcePanelItems.First(item => item.Code == code);

    /// <summary>T() 返回含 {0} 的原始模板；取占位符之前的前缀用于 StartsWith 断言。</summary>
    private static string LocalizedPrefix(LocalizationService localizer, string key)
    {
        var template = localizer.T(key);
        var placeholderIndex = template.IndexOf("{0}", StringComparison.Ordinal);
        return placeholderIndex < 0 ? template : template[..placeholderIndex];
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeoutCts.Token);
        }
    }

    private async Task<TestContext> CreateContextAsync(
        string? cookieUid = null,
        Action<GatedResourcePanelHandler>? configure = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var cookiePath = Path.Combine(tempDir, "Library");
        if (cookieUid is not null)
        {
            await WriteCookieLibraryAsync(cookiePath, cookieUid);
        }

        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);
        var handler = new GatedResourcePanelHandler();
        configure?.Invoke(handler);
        var apiClient = new ResourcePanelApiClient(handler);
        var localizer = new LocalizationService();
        var toastService = new ToastService();
        var errorHandling = new FakeErrorHandlingService();
        var service = new ResourcePanelService(uidService, apiClient, new LocalDiagnostics());
        var viewModel = new ResourcePanelViewModel(service, localizer, toastService, errorHandling);
        return new TestContext(
            viewModel,
            handler,
            settingsService,
            apiClient,
            toastService,
            errorHandling,
            service,
            localizer,
            tempDir);
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

    private sealed record TestContext(
        ResourcePanelViewModel ViewModel,
        GatedResourcePanelHandler Handler,
        LauncherSettingsService SettingsService,
        ResourcePanelApiClient ApiClient,
        ToastService ToastService,
        FakeErrorHandlingService ErrorHandling,
        ResourcePanelService Service,
        LocalizationService Localizer,
        string TempDir) : IDisposable
    {
        public void Dispose()
        {
            ViewModel.Dispose();
            ApiClient.Dispose();
            SettingsService.Dispose();
            try
            {
                Directory.Delete(TempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the per-test data directory.
            }
        }
    }

    /// <summary>
    /// 可选门控的假 API handler：/status/list 可被挂起以观察“加载中”状态；
    /// 非 2xx 状态码用于注入失败（不触发网络重试，用例保持快速确定）。
    /// </summary>
    private sealed class GatedResourcePanelHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource statusGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool GateStatus { get; set; }

        public string StatusJson { get; set; } = "{}";
        public string ConfigJson { get; set; } = "{}";

        public int StatusListCount { get; private set; }
        public int ConfigGetCount { get; private set; }
        public int ConfigSetCount { get; private set; }
        public string? LastConfigSetQuery { get; private set; }
        public HttpStatusCode? ConfigGetStatusCode { get; set; }
        public HttpStatusCode? ConfigSetStatusCode { get; set; }

        public void ReleaseStatus() => statusGate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path == "/status/list")
            {
                StatusListCount++;
                if (GateStatus)
                {
                    await statusGate.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }

                return Json(StatusJson);
            }

            if (path == "/config/get")
            {
                ConfigGetCount++;
                return ConfigGetStatusCode is { } getCode ? Status(getCode) : Json(ConfigJson);
            }

            if (path == "/config/set")
            {
                ConfigSetCount++;
                LastConfigSetQuery = request.RequestUri?.Query;
                return ConfigSetStatusCode is { } setCode ? Status(setCode) : Json("{}");
            }

            return Status(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        private static HttpResponseMessage Status(HttpStatusCode statusCode) => new(statusCode);
    }

    /// <summary>手写 fake：记录 HandleErrorAsync 调用（无 mocking 框架）。</summary>
    private sealed class FakeErrorHandlingService : IErrorHandlingService
    {
        public int HandleErrorCount { get; private set; }
        public string? LastContext { get; private set; }
        public Exception? LastException { get; private set; }
        public ErrorHandlingOptions? LastOptions { get; private set; }

        public event Action<CriticalErrorInfo>? CriticalErrorRequested;

        public Task HandleErrorAsync(string context, Exception exception, ErrorHandlingOptions? options = null)
        {
            HandleErrorCount++;
            LastContext = context;
            LastException = exception;
            LastOptions = options;
            return Task.CompletedTask;
        }

        public Task HandleCriticalErrorAsync(string context, Exception exception) => Task.CompletedTask;

        /// <summary>供潜在的关键错误用例触发事件，同时消除 CS0067。</summary>
        public void RaiseCriticalError(CriticalErrorInfo info) => CriticalErrorRequested?.Invoke(info);
    }
}
