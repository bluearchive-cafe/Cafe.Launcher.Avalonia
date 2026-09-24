using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.ViewModels;

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

    private readonly TestDirectory tempDir = TestDirectory.Create();
    private readonly List<MainWindowTestContext> contexts = [];
    private readonly ProxySettingsService proxySettings = new();
    private readonly HttpClientFactory httpClientFactory;
    private readonly ImageCacheService imageCacheService;

    public MainWindowViewModelTests()
    {
        httpClientFactory = new HttpClientFactory(proxySettings);
        imageCacheService = new ImageCacheService(
            new StubRemoteHttpTransport(),
            new Crc64Service(),
            tempDir.DataRoot);
    }

    /// <summary>
    /// 装配一个主窗口 ViewModel 并登记它的上下文。对象图与日志的构造在
    /// <see cref="MainWindowTestContext"/>（连同那里的所有权规则），这里只负责
    /// 创建上下文、让它活到用例结束，并把 ViewModel 交给用例。
    /// </summary>
    private async Task<MainWindowViewModel> CreateViewModelAsync(
        ILauncherCoreService coreService,
        SavedSettingsTestRig? savedSettings = null,
        ResourcePanelUidService? resourcePanelUidService = null,
        ResourcePanelApiClient? resourcePanelApiClient = null,
        ToastService? toastService = null,
        LauncherUpdateService? launcherUpdateService = null,
        StubGameOperationExecutor? gameOperationsBackend = null,
        SystemAnimationSettingsProvider? systemAnimationSettingsProvider = null,
        Func<TimeSpan, CancellationToken, Task>? toastDelayAsync = null,
        StubFilePickerService? filePickerService = null)
    {
        var context = MainWindowTestContext.Create(
            tempDir,
            httpClientFactory,
            imageCacheService,
            coreService,
            savedSettings,
            resourcePanelUidService,
            resourcePanelApiClient,
            toastService,
            launcherUpdateService,
            null,
            null,
            gameOperationsBackend,
            systemAnimationSettingsProvider,
            toastDelayAsync,
            filePickerService);
        contexts.Add(context);
        return context.ViewModel;
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
        // 顺序即所有权：用例已经释放过自己的 ViewModel，接着释放上下文自建的资源
        // （日志、设置装配、下载用工厂），最后才删临时目录。
        foreach (var context in contexts)
        {
            context.Dispose();
        }

        imageCacheService.Dispose();
        httpClientFactory.Dispose();
        tempDir.Dispose();
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
}
