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
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.ViewModels;
using System.Net;
using System.Text;

namespace Cafe.Launcher.Avalonia.Tests;

// MainWindowViewModelTests 的共享核心（夹具字段、ViewModel 组装、通用快照/保存 helper
// 与多处共用的测试替身）。各职责域的分卷见 MainWindowViewModelTests.<域>.cs：
// Lifecycle / Settings / Appearance / Background / RemoteContent / ResourcePanel /
// GameOperations / WizardDialogs / Motion。
[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed partial class MainWindowViewModelTests : IDisposable
{
    static MainWindowViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly ProxySettingsService proxySettings = new();
    private readonly HttpClientFactory httpClientFactory;
    private readonly LauncherApiClient apiClient = new(
        new HttpClientHandler(),
        new AuthorizationHeaderFactory(),
        new PatchUrlGroupService());
    private readonly ImageCacheService imageCacheService;

    public MainWindowViewModelTests()
    {
        Directory.CreateDirectory(tempDir);
        httpClientFactory = new HttpClientFactory(proxySettings);
        imageCacheService = new ImageCacheService(
            httpClientFactory,
            new Crc64Service(),
            RemoteHttpUrlValidator.CreateForTesting());
    }

    private async Task<MainWindowViewModel> CreateViewModelAsync(
        ILauncherCoreService coreService,
        LauncherSettingsService? settingsService = null,
        ResourcePanelUidService? resourcePanelUidService = null,
        ResourcePanelApiClient? resourcePanelApiClient = null,
        ToastService? toastService = null,
        LauncherUpdateService? launcherUpdateService = null,
        StubGameOperationExecutor? gameOperationsBackend = null,
        WindowsAnimationSettingsProvider? windowsAnimationSettingsProvider = null,
        Func<TimeSpan, CancellationToken, Task>? toastDelayAsync = null,
        StubFilePickerService? filePickerService = null)
    {
        filePickerService ??= new StubFilePickerService();
        settingsService ??= new LauncherSettingsService(
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var localInstallationStateStore = new LocalInstallationStateStore();
        var diagnostics = new LocalDiagnostics();
        var localizationService = new LocalizationService();
        var remoteManifestService = new RemoteManifestService(apiClient);
        var diagnosticsVal = new LocalDiagnostics();
        var fileDownloadService = new FileDownloadService(
            new Crc64Service(),
            diagnosticsVal,
            RemoteHttpUrlValidator.CreateForTesting());
        var manifestValidationService = new ManifestValidationService(apiClient, remoteManifestService, localizationService);
        var gameRuntime = new GameRuntime(
            [GameRunnerDefinition.Native],
            new DefaultProcessLauncher(),
            new GameProcessTracker());
        var gameLaunchService = new GameLaunchService(
            manifestValidationService,
            new ClickCodeService(),
            gameRuntime,
            localizationService);
        var gameDownloadService = new GameDownloadService(
            apiClient,
            remoteManifestService,
            fileDownloadService,
            localInstallationStateStore,
            settingsService,
            new HttpClientFactory(new ProxySettingsService()),
            new Crc64Service(),
            new DiskSpaceService(),
            diagnostics,
            localizationService,
            new GameInstallationPath(),
            new GameProcessTracker(),
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "download_state.json"));
        resourcePanelUidService ??= new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing-resource-panel-cookie"));
        resourcePanelApiClient ??= new ResourcePanelApiClient(new ResourcePanelHandler());

        toastService ??= new ToastService();
        var diskSpaceService = new DiskSpaceService();
        var launcherUpdateSvc = launcherUpdateService ?? new LauncherUpdateService(new LauncherUpdateHandler());
        var settingsEditor = new SettingsEditor();
        var settingsOptions = new SettingsOptionsViewModel(localizationService, diskSpaceService);
        var settingsAppearance = new SettingsAppearanceViewModel(settingsEditor);
        var shellViewModel = new ShellViewModel(localizationService);
        var errorHandling = new ErrorHandlingService(localizationService, diagnostics, toastService);
        var noticeStateService = new NoticeStateService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "shown_notices.json"));
        var dialogsViewModel = new DialogsViewModel(localizationService, noticeStateService, new SetupWizardViewModel(localizationService, new GameInstallationPath(), new LocalInstallationStateStore(), new LocalDiagnostics(), filePickerService));
        using var settingsLogger = new UnifiedLogger(Path.Combine(tempDir, Guid.NewGuid().ToString("N")));
        var settingsViewModel = new SettingsViewModel(
            settingsService, localizationService, toastService,
            launcherUpdateSvc, dialogsViewModel,
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
            new GameProcessTracker());

        var remoteContentViewModel = new RemoteContentViewModel(localizationService, imageCacheService, diagnostics);
        var backgroundViewModel = new BackgroundViewModel(imageCacheService, diagnostics, settingsViewModel);
        var gameOperationsViewModel = gameOperationsBackend is null
            ? new GameOperationsViewModel(
                new GameOperationExecutor(gameLaunchService, gameDownloadService, gameUninstallService),
                new GameShortcutService(localizationService),
                localizationService,
                toastService,
                diagnostics,
                shellViewModel,
                dialogsViewModel,
                errorHandling)
            : new GameOperationsViewModel(
                gameOperationsBackend,
                new TestGameShortcutService(),
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
        var debugViewModel = new DebugViewModel(toastService, new UnifiedLogger(Path.Combine(tempDir, Guid.NewGuid().ToString("N"))), errorHandling, settingsService, gameOperationsViewModel, shellViewModel, filePickerService);
        var windowChromeViewModel = new WindowChromeViewModel(
            settingsViewModel, remoteContentViewModel, dialogsViewModel, gameOperationsViewModel,
            debugViewModel);

        using var testLogger = new UnifiedLogger(tempDir);
        return new MainWindowViewModel(
            coreService,
            settingsService,
            localizationService,
            toastService,
            launcherUpdateSvc,
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
                new LogViewerDialogViewModel(testLogger, null, null, null, null, null, filePickerService),
                debugViewModel,
                new ModalHostViewModel()),
            errorHandling,
            windowsAnimationSettingsProvider ?? new WindowsAnimationSettingsProvider(),
            filePickerService);
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

    private static OperationsResourceResponse CreateOperationsResource()
    {
        return new OperationsResourceResponse
        {
            OperationsResourceOpen = true,
            NewsList = new NewsListEnvelope
            {
                Code = 0,
                Data = new NewsListData
                {
                    News =
                    [
                        new NewsTypeItem
                        {
                            TypeLabel = "news",
                            Rows =
                            [
                                new NewsRowItem
                                {
                                    Title = "news title",
                                    PublishTime = 0,
                                    Link = "https://example.invalid/news"
                                }
                            ]
                        }
                    ]
                }
            },
            NoticeList =
            [
                new NoticeTypeItem
                {
                    NoticeType = "notice",
                    NoticeDetailList =
                    [
                        new NoticeDetailItem
                        {
                            NoticeTitle = "notice title",
                            NoticeTime = "2026-06-12",
                            JumpUrl = "https://example.invalid/notice"
                        }
                    ]
                }
            ]
        };
    }

    private static async Task SaveSettingsAsync(MainWindowViewModel viewModel)
    {
        await viewModel.Settings.SaveSettingsCommand.ExecuteAsync(null);
    }

    public void Dispose()
    {
        imageCacheService.Dispose();
        apiClient.Dispose();
        httpClientFactory.Dispose();
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class CountingCoreService : ILauncherCoreService
    {
        private readonly LauncherStatusSnapshot snapshot;

        public CountingCoreService(LauncherStatusSnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        public int LoadCount { get; private set; }

        public Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class ThrowingCoreService : ILauncherCoreService
    {
        public Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("load failed");
        }
    }

    private sealed class BlockingSecondLoadCoreService : ILauncherCoreService
    {
        private readonly LauncherStatusSnapshot snapshot;
        private int loadCount;

        public BlockingSecondLoadCoreService(LauncherStatusSnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        public TaskCompletionSource SecondLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSecondLoad { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int LoadCount => Volatile.Read(ref loadCount);

        public async Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref loadCount) == 2)
            {
                SecondLoadStarted.TrySetResult();
                await ReleaseSecondLoad.Task.WaitAsync(cancellationToken);
            }

            return snapshot;
        }
    }

    private sealed class LauncherUpdateHandler : HttpMessageHandler
    {
        private readonly string? responseJson;

        public LauncherUpdateHandler(string? responseJson = null)
        {
            this.responseJson = responseJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (responseJson is not null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
