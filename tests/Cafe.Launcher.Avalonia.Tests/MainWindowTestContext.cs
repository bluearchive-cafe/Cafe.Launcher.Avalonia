using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Cafe.Launcher.Avalonia.Services.Update;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 主窗口 ViewModel 的测试装配：把这一整张对象图（设置装配、传输替身、日志、各分域
/// ViewModel）的构造收在一处，并把每样东西的所有权写清楚。
/// </summary>
/// <remarks>
/// <para><b>所有权。</b>上下文只释放它自己创建的对象；调用方传入的替身、工厂与设置装配
/// 由调用方释放。ViewModel 也归调用方（用例以 <c>using var viewModel = ...</c> 持有它）。</para>
/// <para><b>日志活期。</b>这里是本类型存在的首要理由：<c>SettingsViewModel</c> 与
/// <c>LogViewerDialogViewModel</c> 长期持有 <c>UnifiedLogger</c>，而它们此前是装配方法的
/// 局部 <c>using</c>——方法一返回句柄就被关掉，消费者的后续写入落进已释放的管道。
/// 日志现在由上下文持有，活到用例结束，且一定晚于 ViewModel 释放（调用方先释放
/// ViewModel，再释放上下文）。</para>
/// <para><b>真实 DI 的边界。</b>只有装配、绑定与窗口集成测试用真实容器
/// （<c>ServiceConfigurationTests</c> 等）；这里的对象图按需手工构造，避免每个用例都
/// 承担整套应用初始化的成本。</para>
/// </remarks>
internal sealed class MainWindowTestContext : IDisposable
{
    private readonly List<IDisposable> owned;
    private bool disposed;

    private MainWindowTestContext(MainWindowViewModel viewModel, List<IDisposable> owned)
    {
        ViewModel = viewModel;
        this.owned = owned;
        Loggers = owned.OfType<UnifiedLogger>().ToArray();
    }

    /// <summary>装配好的主窗口 ViewModel；生命周期归调用方。</summary>
    public MainWindowViewModel ViewModel { get; }

    /// <summary>
    /// 本上下文持有并负责释放的日志器（设置页、调试面板、日志查看器各持一个）。
    /// 「装配返回后日志仍然可写」这条契约需要能直接对它们写一条来验证。
    /// </summary>
    public IReadOnlyList<UnifiedLogger> Loggers { get; }

    /// <summary>
    /// 装配主窗口 ViewModel。参数与各用例原先手工装配时一致：调用方显式指定关键替身，
    /// 未指定时按生产形状补齐。
    /// </summary>
    public static MainWindowTestContext Create(
        TestDirectory directory,
        HttpClientFactory httpClientFactory,
        ImageCacheService imageCacheService,
        ILauncherCoreService coreService,
        SavedSettingsTestRig? savedSettings = null,
        ResourcePanelUidService? resourcePanelUidService = null,
        ResourcePanelApiClient? resourcePanelApiClient = null,
        ToastService? toastService = null,
        LauncherUpdateService? launcherUpdateService = null,
        LauncherSelfUpdateService? launcherSelfUpdateService = null,
        IWindowsLauncherUpdateApplier? launcherUpdateApplier = null,
        StubGameOperationExecutor? gameOperationsBackend = null,
        SystemAnimationSettingsProvider? systemAnimationSettingsProvider = null,
        Func<TimeSpan, CancellationToken, Task>? toastDelayAsync = null,
        StubFilePickerService? filePickerService = null)
    {
        var owned = new List<IDisposable>();
        return new MainWindowTestContext(
            CreateViewModel(
                directory,
                httpClientFactory,
                imageCacheService,
                coreService,
                savedSettings,
                resourcePanelUidService,
                resourcePanelApiClient,
                toastService,
                launcherUpdateService,
                launcherSelfUpdateService,
                launcherUpdateApplier,
                gameOperationsBackend,
                systemAnimationSettingsProvider,
                toastDelayAsync,
                filePickerService,
                owned),
            owned);
    }

    private static MainWindowViewModel CreateViewModel(
        TestDirectory directory,
        HttpClientFactory httpClientFactory,
        ImageCacheService imageCacheService,
        ILauncherCoreService coreService,
        SavedSettingsTestRig? savedSettings,
        ResourcePanelUidService? resourcePanelUidService,
        ResourcePanelApiClient? resourcePanelApiClient,
        ToastService? toastService,
        LauncherUpdateService? launcherUpdateService,
        LauncherSelfUpdateService? launcherSelfUpdateService,
        IWindowsLauncherUpdateApplier? launcherUpdateApplier,
        StubGameOperationExecutor? gameOperationsBackend,
        SystemAnimationSettingsProvider? systemAnimationSettingsProvider,
        Func<TimeSpan, CancellationToken, Task>? toastDelayAsync,
        StubFilePickerService? filePickerService,
        List<IDisposable> owned)
    {
        filePickerService ??= new StubFilePickerService();
        if (savedSettings is null)
        {
            savedSettings = new SavedSettingsTestRig(directory.DataRoot);
            owned.Add(savedSettings);
        }

        var settingsService = savedSettings.SettingsService;
        var savedSettingsWriter = savedSettings.Writer;
        var localInstallationStateStore = new LocalInstallationStateStore();
        var diagnostics = new LocalDiagnostics();
        var localizationService = new LocalizationService();
        var apiClient = new LauncherApiClient(
            new StubRemoteHttpTransport(),
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());
        var remoteManifestService = new RemoteManifestService(apiClient);
        var downloadDiagnostics = new LocalDiagnostics();
        var fileDownloadService = new FileDownloadService(
            new Crc64Service(),
            downloadDiagnostics);
        var manifestValidationService = new ManifestValidationService(apiClient, remoteManifestService, localizationService);
        var gameRuntime = new GameRuntime(
            [GameRunnerDefinition.Native],
            new DefaultProcessLauncher(),
            TestGameProcessTracker.None());
        var gameLaunchService = new GameLaunchService(
            manifestValidationService,
            gameRuntime,
            localizationService);
        var gameDownloadHttpClientFactory = new HttpClientFactory(new ProxySettingsService());
        owned.Add(gameDownloadHttpClientFactory);
        var gameDownloadService = new GameDownloadService(
            apiClient,
            remoteManifestService,
            fileDownloadService,
            localInstallationStateStore,
            settingsService,
            gameDownloadHttpClientFactory,
            RemoteHttpUrlValidator.CreateForTesting(),
            new Crc64Service(),
            new DiskSpaceService(),
            diagnostics,
            localizationService,
            new GameInstallationPath(),
            TestGameProcessTracker.None(),
            TestDataRoot.ForDirectory(directory.Sub(Guid.NewGuid().ToString("N"))));
        if (resourcePanelUidService is null)
        {
            resourcePanelUidService = new ResourcePanelUidService(
                new BestHttpCookieLibraryService(),
                settingsService,
                savedSettingsWriter,
                directory.Sub("missing-resource-panel-cookie"));
        }

        resourcePanelApiClient ??= new ResourcePanelApiClient(new StubRemoteHttpTransport());

        if (toastService is null)
        {
            toastService = new ToastService();
        }

        var diskSpaceService = new DiskSpaceService();
        var launcherUpdateSvc = launcherUpdateService ?? new LauncherUpdateService(new StubRemoteHttpTransport());
        launcherUpdateApplier ??= new WindowsLauncherUpdateApplier(
            directory.DataRoot, diagnostics, directory.Path, directory.Path);
        launcherSelfUpdateService ??= new LauncherSelfUpdateService(
            new LauncherUpdateDownloader(new StubRemoteHttpTransport()),
            new LauncherUpdateHostInfoProvider(),
            launcherUpdateApplier,
            directory.DataRoot,
            diagnostics);
        var settingsEditor = savedSettings.Editor;
        var settingsOptions = new SettingsOptionsViewModel(localizationService, diskSpaceService);
        var settingsAppearance = new SettingsAppearanceViewModel(settingsEditor, new ThemeApplier());
        var shellViewModel = new ShellViewModel(localizationService);
        var errorHandling = new ErrorHandlingService(localizationService, diagnostics, toastService);
        var noticeStateService = new NoticeStateService(
            TestDataRoot.ForDirectory(directory.Sub(Guid.NewGuid().ToString("N"))));
        var dialogsViewModel = new DialogsViewModel(
            localizationService,
            noticeStateService,
            new SetupWizardViewModel(localizationService, new GameInstallationPath(), new LocalInstallationStateStore(), diagnostics, filePickerService),
            diagnostics);
        // 这两个日志是上下文持有的资源：设置页与日志查看器在其整个活期内继续写入。
        var settingsLogger = new UnifiedLogger(directory.Sub(Guid.NewGuid().ToString("N")));
        owned.Add(settingsLogger);
        var settingsViewModel = new SettingsViewModel(
            settingsService, savedSettingsWriter, localizationService, toastService,
            launcherUpdateSvc, launcherSelfUpdateService, dialogsViewModel,
            settingsLogger,
            new GameInstallationPath(),
            settingsOptions, settingsAppearance, errorHandling,
            gameRuntime, filePickerService);
        var resourcePanelService = new ResourcePanelService(
            resourcePanelUidService, resourcePanelApiClient, diagnostics);
        var resourcePanelViewModel = new ResourcePanelViewModel(
            resourcePanelService, localizationService, toastService, errorHandling);
        var gameUninstallService = new GameUninstallService(
            localInstallationStateStore,
            diagnostics,
            localizationService,
            new GameInstallationPath(),
            new DownloadCheckpointStore(TestDataRoot.ForDirectory(directory.Sub(Guid.NewGuid().ToString("N")))),
            TestGameProcessTracker.None(),
            new TestGameShortcutService());

        var remoteContentViewModel = new RemoteContentViewModel(localizationService, imageCacheService, diagnostics);
        var backgroundViewModel = new BackgroundViewModel(imageCacheService, diagnostics, settingsViewModel);
        var gameSessionMonitor = new FakeGameSessionMonitor();
        var gameOperationsViewModel = gameOperationsBackend is null
            ? new GameOperationsViewModel(
                new GameOperationExecutor(gameLaunchService, gameDownloadService, gameUninstallService),
                new GameShortcutService(localizationService),
                gameSessionMonitor,
                localizationService,
                toastService,
                diagnostics,
                shellViewModel,
                dialogsViewModel,
                errorHandling)
            : new GameOperationsViewModel(
                gameOperationsBackend,
                new TestGameShortcutService(),
                gameSessionMonitor,
                localizationService,
                toastService,
                diagnostics,
                shellViewModel,
                dialogsViewModel,
                errorHandling,
                _ => Task.CompletedTask);
        var toastHostViewModel = toastDelayAsync is null
            ? new ToastHostViewModel(toastService, localizationService, diagnostics)
            : new ToastHostViewModel(
                toastService,
                localizationService,
                diagnostics,
                action =>
                {
                    action();
                    return Task.CompletedTask;
                },
                toastDelayAsync);
        var debugLogger = new UnifiedLogger(directory.Sub(Guid.NewGuid().ToString("N")));
        owned.Add(debugLogger);
        var debugViewModel = new DebugViewModel(
            directory.DataRoot,
            toastService,
            debugLogger,
            errorHandling,
            new StubFatalCrashService(),
            settingsService,
            gameOperationsViewModel,
            shellViewModel);
        var windowChromeViewModel = new WindowChromeViewModel(
            directory.DataRoot,
            settingsViewModel, remoteContentViewModel, dialogsViewModel, gameOperationsViewModel,
            debugViewModel);

        var windowLogger = new UnifiedLogger(directory.Path);
        owned.Add(windowLogger);
        return new MainWindowViewModel(
            coreService,
            settingsService,
            savedSettingsWriter,
            localizationService,
            toastService,
            launcherUpdateSvc,
            launcherSelfUpdateService,
            launcherUpdateApplier,
            diagnostics,
            new ShellPresentationFamily(
                shellViewModel,
                backgroundViewModel,
                remoteContentViewModel,
                dialogsViewModel,
                gameOperationsViewModel,
                toastHostViewModel,
                windowChromeViewModel,
                settingsViewModel,
                resourcePanelViewModel,
                new LogViewerDialogViewModel(windowLogger, new ToastService(), new LocalizationService(), new LocalDiagnostics()),
                new LogExportDialogViewModel(
                    new LogExportService(new LocalDiagnostics(windowLogger), TestDataRoot.ForCurrentProcess(), new CrashReportStore(TestDataRoot.ForCurrentProcess())),
                    filePickerService,
                    toastService,
                    localizationService,
                    diagnostics),
                debugViewModel,
                new ModalHostViewModel()),
            errorHandling,
            systemAnimationSettingsProvider ?? new SystemAnimationSettingsProvider(),
            filePickerService);
    }

    /// <summary>先放容器外资源，再让调用方在此之前已经释放掉的 ViewModel 保持最后一次写入有效。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        for (var i = owned.Count - 1; i >= 0; i--)
        {
            owned[i].Dispose();
        }

        owned.Clear();
    }
}
