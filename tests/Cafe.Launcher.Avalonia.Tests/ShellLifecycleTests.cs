using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// ShellLifecycle 失败路径回归:核心刷新异常、设置持久化失败、向导完成失败、
/// 启动更新检查失败都必须按"降级 + 用户可见反馈 + 壳可重试"的契约收场,
/// 既不允许把半套用的状态留在壳上,也不允许把异常静默吞掉。
/// </summary>
[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class ShellLifecycleTests : IDisposable
{
    static ShellLifecycleTests()
    {
        TestLocalizationHelper.Initialize();
    }

    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly HttpClientFactory httpClientFactory;
    private readonly ToastService toastService = new();
    private readonly List<ToastNotification> raisedToasts = [];
    private readonly List<IDisposable> disposables = [];
    private readonly List<SetupWizardViewModel> wizards = [];

    public ShellLifecycleTests()
    {
        Directory.CreateDirectory(tempDir);
        httpClientFactory = new HttpClientFactory(new ProxySettingsService());
        toastService.ToastRaised += notification => raisedToasts.Add(notification);
    }

    [Fact]
    public async Task RefreshAsync_WhenCoreLoadThrows_ShowsRefreshErrorStateAndErrorToast()
    {
        // 预置"显示远程内容卡片",让 BeginLoading/EndLoading 的加载闸门真实开合,
        // 从而能断言失败后加载态被 finally 收干净。
        var settingsService = new LauncherSettingsService(
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ShowRemoteContentCard = true });
        var core = new ScriptedCoreService(new InvalidOperationException("load failed"));
        var fixture = CreateLifecycle(core, settingsService: settingsService);

        await fixture.Lifecycle.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(2));

        // 失败被降级为壳上的错误状态,而不是把异常抛给调用方。
        Assert.Contains("load failed", fixture.Shell.NetworkText, StringComparison.Ordinal);
        Assert.Equal(fixture.Shell.I18n["versionUnavailable"], fixture.Shell.VersionText);
        Assert.False(fixture.Lifecycle.IsBusy);
        Assert.False(fixture.Shell.IsBusy);
        Assert.False(fixture.RemoteContent.IsLoading);
        var errorToasts = raisedToasts.Where(t => t.Severity == ToastSeverity.Error).ToList();
        var errorToast = Assert.Single(errorToasts);
        Assert.Contains("load failed", errorToast.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_AfterRecoverableFailure_RestoresSnapshotOnNextRefresh()
    {
        var core = new ScriptedCoreService(new InvalidOperationException("load failed"), CreateSnapshot());
        var fixture = CreateLifecycle(core);

        await fixture.Lifecycle.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.Lifecycle.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(2));

        // 壳必须保持可用:第二次刷新(服务恢复后)完整应用快照并清除错误状态。
        Assert.Equal(2, core.LoadCount);
        Assert.Equal("BlueArchive.exe", fixture.Shell.ExecutableNameText);
        Assert.Equal(fixture.Shell.I18n["statusNetworkLoaded"], fixture.Shell.NetworkText);
        Assert.False(fixture.Lifecycle.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_WhenCoreLoadFails_SkipsPersistedDownloadResumeUntilRecovery()
    {
        var core = new ScriptedCoreService(new InvalidOperationException("load failed"), CreateSnapshot());
        var fixture = CreateLifecycle(core);

        await fixture.Lifecycle.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(2));

        // 状态未知时不得续传持久化下载(AfterLoad 仅在加载成功后执行)。
        Assert.Equal(0, fixture.OperationsBackend.ResumeCallCount);

        await fixture.Lifecycle.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, fixture.OperationsBackend.ResumeCallCount);
        Assert.Equal(2, core.LoadCount);
    }

    [Fact]
    public async Task HandleSettingsSavedAsync_WhenFollowUpRefreshFails_ReportsErrorWithoutThrowing()
    {
        var core = new ScriptedCoreService(new InvalidOperationException("load failed"));
        var fixture = CreateLifecycle(core);

        // 设置保存成功后的跟进刷新若失败,必须被生命周期内部消化:
        // await 本身不得抛出(否则会打断设置页保存命令的收尾流程)。
        await fixture.Lifecycle.HandleSettingsSavedAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, core.LoadCount);
        Assert.False(fixture.Lifecycle.IsBusy);
        Assert.Contains("load failed", fixture.Shell.NetworkText, StringComparison.Ordinal);
        Assert.Contains(
            raisedToasts,
            t => t.Severity == ToastSeverity.Error && t.Message.Contains("load failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleSettingsSavedAsync_WhileDownloadRunning_SkipsShellRefresh()
    {
        var backend = new StubGameOperationExecutor();
        var core = new ScriptedCoreService(CreateSnapshot());
        var fixture = CreateLifecycle(core, operationsBackend: backend);
        await fixture.Lifecycle.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, core.LoadCount);

        backend.IsDownloadRunning = true;
        await fixture.Lifecycle.HandleSettingsSavedAsync().WaitAsync(TimeSpan.FromSeconds(2));

        // 下载进行中保存设置:只回读持久化设置,不得全量刷新打断下载。
        Assert.Equal(1, core.LoadCount);
        Assert.Equal(1, backend.ResumeCallCount);
        Assert.True(backend.IsDownloadRunning);
        Assert.DoesNotContain(raisedToasts, t => t.Severity == ToastSeverity.Error);
    }

    [Fact]
    public async Task HandleSetupWizardCompletedAsync_WhenSaveFails_KeepsWizardVisibleAndSkipsRefresh()
    {
        var settingsService = new LauncherSettingsService(CreateBlockedSettingsPath());
        var core = new ScriptedCoreService(CreateSnapshot());
        var fixture = CreateLifecycle(core, settingsService: settingsService);
        fixture.Dialogs.IsSetupWizardVisible = true;

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.Lifecycle
                .HandleSetupWizardCompletedAsync(CreateWizardSettings())
                .WaitAsync(TimeSpan.FromSeconds(2)));

        // 保存失败时完成序列必须停在保存这一步:向导保持打开(不被误关),
        // 也不得带着未保存的设置进入刷新。
        Assert.True(fixture.Dialogs.IsSetupWizardVisible);
        Assert.Equal(0, core.LoadCount);
        Assert.Equal(0, fixture.OperationsBackend.ResumeCallCount);
    }

    [Fact]
    public async Task HandleSetupWizardCompletedAsync_AfterSaveFailureRetry_CompletesAndHidesWizard()
    {
        var blockedPath = CreateBlockedSettingsPath();
        var settingsService = new LauncherSettingsService(blockedPath);
        var core = new ScriptedCoreService(CreateSnapshot());
        var fixture = CreateLifecycle(core, settingsService: settingsService);
        fixture.Dialogs.IsSetupWizardVisible = true;
        await Assert.ThrowsAsync<IOException>(() =>
            fixture.Lifecycle
                .HandleSetupWizardCompletedAsync(CreateWizardSettings())
                .WaitAsync(TimeSpan.FromSeconds(2)));

        // 持久层恢复(移除阻塞文件)后,同一条完成流程原样重试即可成功,
        // 证明第一次失败没有把壳状态留在"半套用"。
        File.Delete(Path.GetDirectoryName(blockedPath)!);
        await fixture.Lifecycle
            .HandleSetupWizardCompletedAsync(CreateWizardSettings())
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(fixture.Dialogs.IsSetupWizardVisible);
        Assert.True(File.Exists(blockedPath));
        Assert.Equal(1, core.LoadCount);
        Assert.False(fixture.Lifecycle.IsBusy);
    }

    [Fact]
    public async Task SwitchSourceThenOpenPanelAsync_WhenSaveFails_ShowsErrorToastAndKeepsPanelClosed()
    {
        var settingsService = new LauncherSettingsService(CreateBlockedSettingsPath());
        var core = new ScriptedCoreService(CreateSnapshot());
        var fixture = CreateLifecycle(core, settingsService: settingsService);
        // CreateDefaults 的下载源随系统 UI 文化浮动,这里显式预置 Official 作为"原值"。
        fixture.Settings.Editor.Current.PatchUrlGroup = PatchUrlGroups.Official;

        // 持久化失败被 SwitchSourceThenOpenPanelAsync 捕获并转成错误 toast,不外抛。
        await fixture.Lifecycle.SwitchSourceThenOpenPanelAsync().WaitAsync(TimeSpan.FromSeconds(2));

        var errorToasts = raisedToasts.Where(t => t.Severity == ToastSeverity.Error).ToList();
        var errorToast = Assert.Single(errorToasts);
        Assert.Contains("IOException", errorToast.Message, StringComparison.Ordinal);
        Assert.False(fixture.ResourcePanel.IsResourcePanelVisible);
        // 内存设置快照不得被半套用:保存失败时下载源必须保持预置的 Official
        // (对 Editor 的 Cafe 改写位于 SaveAsync 之后,失败时不可达)。
        Assert.Equal(PatchUrlGroups.Official, fixture.Settings.Editor.Current.PatchUrlGroup);
        Assert.False(File.Exists(settingsService.SettingsPath));
        Assert.Equal(0, core.LoadCount);
    }

    [Fact]
    public async Task SwitchSourceThenOpenPanelAsync_AfterSaveFailureRetry_PersistsCafeSourceAndOpensPanel()
    {
        var blockedPath = CreateBlockedSettingsPath();
        var settingsService = new LauncherSettingsService(blockedPath);
        // 核心服务真实实现会在加载时带回刚持久化的设置;这里让快照携带
        // Cafe 下载源来模拟"刷新读回已保存的 Cafe 源"。
        var snapshot = CreateSnapshot();
        snapshot.Settings.PatchUrlGroup = PatchUrlGroups.Cafe;
        var core = new ScriptedCoreService(snapshot);
        var fixture = CreateLifecycle(core, settingsService: settingsService);
        await fixture.Lifecycle.SwitchSourceThenOpenPanelAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(fixture.ResourcePanel.IsResourcePanelVisible);

        // 持久层恢复后重试:下载源写入成功、内存快照经刷新对齐为 Cafe、面板被打开。
        File.Delete(Path.GetDirectoryName(blockedPath)!);
        await fixture.Lifecycle.SwitchSourceThenOpenPanelAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(fixture.ResourcePanel.IsResourcePanelVisible);
        Assert.Equal(PatchUrlGroups.Cafe, fixture.Settings.Editor.Current.PatchUrlGroup);
        Assert.True(File.Exists(blockedPath));
        Assert.Equal(1, core.LoadCount);
        Assert.False(fixture.Lifecycle.IsBusy);
    }

    [Fact]
    public async Task InitializeAsync_WhenStartupUpdateCheckThrows_CompletesWithoutErrorToast()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.EnableStartupUpdateCheck = true;
        var core = new ScriptedCoreService(snapshot);
        var fixture = CreateLifecycle(
            core,
            launcherUpdateService: new LauncherUpdateService(new ThrowingHttpHandler(), currentVersionOverride: "0.0.0"));

        await fixture.Lifecycle.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(2));
        var pendingUpdateCheck = fixture.Lifecycle.PendingStartupUpdateCheck;
        await pendingUpdateCheck.WaitAsync(TimeSpan.FromSeconds(2));

        // 启动更新检查失败是有意的非关键契约:任务正常完成(不 fault),
        // 不弹错误 toast,壳照常完成加载。
        Assert.Equal(TaskStatus.RanToCompletion, pendingUpdateCheck.Status);
        Assert.DoesNotContain(raisedToasts, t => t.Severity == ToastSeverity.Error);
        Assert.Equal("BlueArchive.exe", fixture.Shell.ExecutableNameText);
        Assert.False(fixture.Lifecycle.IsBusy);
    }

    public void Dispose()
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }

        foreach (var wizard in wizards)
        {
            wizard.Dispose();
        }

        httpClientFactory.Dispose();
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>用同名文件占位,让 Directory.CreateDirectory 抛出 IOException,模拟持久化层不可写。</summary>
    private string CreateBlockedSettingsPath()
    {
        var blocker = Path.Combine(tempDir, "settings-blocked");
        File.WriteAllText(blocker, "a file blocks directory creation");
        return Path.Combine(blocker, "settings.json");
    }

    private LauncherSettings CreateWizardSettings()
    {
        var settings = LauncherSettings.CreateDefaults();
        settings.GamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        return settings;
    }

    private LauncherStatusSnapshot CreateSnapshot()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        return new LauncherStatusSnapshot
        {
            Settings = new LauncherSettings
            {
                GamePath = gamePath
            },
            LocalGame = new LocalInstallationState
            {
                GamePath = gamePath
            },
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse
                {
                    GameLatestVersion = "1.0.0",
                    GameStartExeName = "BlueArchive"
                }
            },
            CheckedAt = DateTimeOffset.Now
        };
    }

    private ShellFixture CreateLifecycle(
        ILauncherCoreService coreService,
        LauncherSettingsService? settingsService = null,
        LauncherUpdateService? launcherUpdateService = null,
        StubGameOperationExecutor? operationsBackend = null)
    {
        settingsService ??= new LauncherSettingsService(
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        launcherUpdateService ??= new LauncherUpdateService(
            new NotFoundHttpHandler(),
            currentVersionOverride: "0.0.0");
        operationsBackend ??= new StubGameOperationExecutor();

        var localizer = new LocalizationService();
        var diagnostics = new LocalDiagnostics();
        var filePickerService = new StubFilePickerService();
        var imageCacheService = new ImageCacheService(
            httpClientFactory,
            new Crc64Service(),
            RemoteHttpUrlValidator.CreateForTesting());
        var settingsEditor = new SettingsEditor();
        var settingsAppearance = new SettingsAppearanceViewModel(settingsEditor);
        var settingsOptions = new SettingsOptionsViewModel(localizer, new DiskSpaceService());
        var shell = new ShellViewModel(localizer);
        var errorHandling = new ErrorHandlingService(localizer, diagnostics, toastService);
        var wizard = new SetupWizardViewModel(
            localizer,
            new GameInstallationPath(),
            new LocalInstallationStateStore(),
            new LocalDiagnostics(),
            filePickerService);
        wizards.Add(wizard);
        var dialogs = new DialogsViewModel(
            localizer,
            new NoticeStateService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "shown_notices.json")),
            wizard);
        using var settingsLogger = new UnifiedLogger(Path.Combine(tempDir, Guid.NewGuid().ToString("N")));
        var settings = new SettingsViewModel(
            settingsService,
            localizer,
            toastService,
            launcherUpdateService,
            dialogs,
            settingsLogger,
            new GameInstallationPath(),
            settingsOptions,
            settingsAppearance,
            errorHandling,
            new GameRuntime([GameRunnerDefinition.Native], new DefaultProcessLauncher(), new GameProcessTracker()),
            filePickerService);
        var resourcePanelService = new ResourcePanelService(
            new ResourcePanelUidService(
                new BestHttpCookieLibraryService(),
                settingsService,
                Path.Combine(tempDir, "missing-resource-panel-cookie")),
            new ResourcePanelApiClient(new NotFoundHttpHandler()),
            diagnostics);
        var resourcePanel = new ResourcePanelViewModel(resourcePanelService, localizer, toastService, errorHandling);
        var remoteContent = new RemoteContentViewModel(localizer, imageCacheService, diagnostics);
        var background = new BackgroundViewModel(imageCacheService, diagnostics, settings);
        var operations = new GameOperationsViewModel(
            operationsBackend,
            new TestGameShortcutService(),
            localizer,
            toastService,
            diagnostics,
            shell,
            dialogs,
            errorHandling,
            _ => Task.CompletedTask);
        var toastHost = new ToastHostViewModel(toastService, localizer, diagnostics);
        var debug = new DebugViewModel(
            toastService,
            new UnifiedLogger(Path.Combine(tempDir, Guid.NewGuid().ToString("N"))),
            errorHandling,
            settingsService,
            operations,
            shell,
            filePickerService);
        var windowChrome = new WindowChromeViewModel(settings, remoteContent, dialogs, operations, debug);
        using var testLogger = new UnifiedLogger(tempDir);
        var logViewer = new LogViewerDialogViewModel(testLogger, null, null, null, null, null, filePickerService);
        var family = new ShellPresentationFamily(
            shell,
            background,
            remoteContent,
            dialogs,
            operations,
            toastHost,
            windowChrome,
            settings,
            resourcePanel,
            logViewer,
            debug,
            new ModalHostViewModel());
        var lifecycle = new ShellLifecycle(
            coreService,
            settingsService,
            localizer,
            toastService,
            launcherUpdateService,
            diagnostics,
            errorHandling,
            new WindowsAnimationSettingsProvider(),
            family,
            filePickerService);

        disposables.Add(lifecycle);
        disposables.Add(imageCacheService);
        disposables.Add(settingsService);
        disposables.Add(launcherUpdateService);

        return new ShellFixture(
            lifecycle,
            shell,
            remoteContent,
            dialogs,
            settings,
            resourcePanel,
            operationsBackend);
    }

    private sealed record ShellFixture(
        ShellLifecycle Lifecycle,
        ShellViewModel Shell,
        RemoteContentViewModel RemoteContent,
        DialogsViewModel Dialogs,
        SettingsViewModel Settings,
        ResourcePanelViewModel ResourcePanel,
        StubGameOperationExecutor OperationsBackend);

    /// <summary>按脚本逐次返回快照或抛异常的核心服务替身;最后一个步骤可重复命中。</summary>
    private sealed class ScriptedCoreService : ILauncherCoreService
    {
        private readonly object[] script;
        private int next;

        public ScriptedCoreService(params object[] script)
        {
            this.script = script;
        }

        public int LoadCount { get; private set; }

        public Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            var step = script[Math.Min(next, script.Length - 1)];
            next++;
            LoadCount++;
            return Task.FromResult(step switch
            {
                Exception failure => throw failure,
                LauncherStatusSnapshot snapshot => snapshot,
                _ => throw new InvalidOperationException("Unexpected script step.")
            });
        }
    }

    /// <summary>所有请求都返回 404 的默认替身,保证夹具不发真实网络请求。</summary>
    private sealed class NotFoundHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>让更新检查在 HTTP 层抛非网络异常,驱动 CheckForStartupUpdateAsync 的兜底分支。</summary>
    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("update probe failed");
    }
}
