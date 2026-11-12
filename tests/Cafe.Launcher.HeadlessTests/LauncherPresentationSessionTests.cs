using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Styling;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.ViewModels;
using Xunit;
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Services.Diagnostics;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI;
using Cafe.Launcher.UI.Composition;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

/// <summary>
/// <see cref="LauncherPresentationSession"/> 是宿主与表现层之间唯一的生命周期词汇表。
/// 协作者由登记入口装配后经构造函数注入（会话本身不做服务定位），因此这里断言的是接缝的契约：
/// 缺少表现层登记时解析失败而不是产出「半个会话」、登记是单例、未建窗口前的调用会明确报错
/// 而不是空引用、释放幂等且在触达任何表现层服务前就失败。
/// </summary>
public sealed class LauncherPresentationSessionTests
{
    [Fact]
    public void AddLauncherPresentation_WithoutAServiceCollection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LauncherPresentationServiceCollectionExtensions.AddLauncherPresentation(null!));
    }

    [Fact]
    public void Session_WithoutItsCollaborators_FailsToResolve()
    {
        // 只有门面登记、没有表现层服务：会话解析不出来，而不是先构造出一个半成品再在
        // 运行时抛空引用——构造失败点因此落在组合根。
        using var provider = new ServiceCollection().AddLauncherPresentation().BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<LauncherPresentationSession>());
    }

    [AvaloniaFact]
    public void Session_IsRegisteredAsASingleton()
    {
        using var directory = TestDirectory.Create();
        using var provider = HeadlessTestHost.CreateServiceProvider(directory);

        Assert.Same(
            provider.GetRequiredService<LauncherPresentationSession>(),
            provider.GetRequiredService<LauncherPresentationSession>());
    }

    [AvaloniaFact]
    public async Task Session_BeforeTheWindowExists_ReportsTheMissingPresentation()
    {
        // 容器齐备但尚未建窗口：任何需要窗口的调用都必须明确报错，
        // 而不是在宿主已经交回控制权之后抛空引用。
        using var directory = TestDirectory.Create();
        using var provider = HeadlessTestHost.CreateServiceProvider(directory);
        var session = provider.GetRequiredService<LauncherPresentationSession>();

        Assert.Throws<InvalidOperationException>(() =>
            session.AttachStartupBehavior(firstLaunch: false, launchGameRequested: false, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InitializeAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.LaunchGameAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.PrepareForShutdownAsync(TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public void Session_CreatesTheCrashReportWindow_WithoutThePrimaryWindow()
    {
        // 崩溃窗口是独立终端窗口（崩溃报告进程也直接构造它），因此不属于「需要主窗口」的一类：
        // 宿主在致命崩溃路径上先藏主窗口再换上它。
        using var directory = TestDirectory.Create();
        using var provider = HeadlessTestHost.CreateServiceProvider(directory);
        var session = provider.GetRequiredService<LauncherPresentationSession>();

        var crashWindow = session.CreateCrashReportWindow(CreateCrashReport());

        Assert.IsType<CrashReportWindow>(crashWindow);
    }

    [AvaloniaFact]
    public async Task Session_AfterDispose_RejectsEveryLifecycleCall()
    {
        using var directory = TestDirectory.Create();
        using var provider = HeadlessTestHost.CreateServiceProvider(directory);
        var session = provider.GetRequiredService<LauncherPresentationSession>();

        session.Dispose();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.CreateMainWindow());
        Assert.Throws<ObjectDisposedException>(() => session.ShowWindow());
        Assert.Throws<ObjectDisposedException>(() => session.HideMainWindow());
        // 异步成员在触达协作者之前就抛：返回的任务是同步失败地完成的。
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.InitializeAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.LaunchGameAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.PrepareForShutdownAsync());
    }

    [AvaloniaFact]
    public void Session_CreatesTheWindowAndTrayFromTheContainer()
    {
        using var directory = TestDirectory.Create();
        using var provider = HeadlessTestHost.CreateServiceProvider(directory);
        var session = provider.GetRequiredService<LauncherPresentationSession>();

        var window = session.CreateMainWindow();

        Assert.IsType<MainWindow>(window);
        // 会话只有一个窗口：重复调用返回同一实例，托盘也只初始化一次。
        Assert.Same(window, session.CreateMainWindow());

        session.ShowWindow();
        session.AttachStartupBehavior(firstLaunch: false, launchGameRequested: false, TestContext.Current.CancellationToken);
    }

    [AvaloniaFact]
    public async Task Startup_WhenTheSavedAgreementVersionIsStale_ShowsOnlyTheAgreementStep()
    {
        using var directory = TestDirectory.Create();
        using var provider = HeadlessTestHost.CreateServiceProvider(directory);
        using var theme = ThemeVariantSnapshot.Capture(ThemeVariant.Light);
        var writer = provider.GetRequiredService<ISavedSettingsWriter>();
        var saved = LauncherSettings.CreateDefaults();
        saved.Language = LauncherLanguages.Japanese;
        saved.ThemeMode = ThemeModes.Dark;
        saved.AcceptedAgreementVersion = "2020-01-01";

        await writer.ReplaceAsync(saved);

        var session = provider.GetRequiredService<LauncherPresentationSession>();
        var window = session.CreateMainWindow();
        session.AttachStartupBehavior(firstLaunch: false, launchGameRequested: false, TestContext.Current.CancellationToken);
        window.Show();

        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        await HeadlessTestHost.WaitUntilAsync(
            () => viewModel.Dialogs.IsSetupWizardVisible,
            TimeSpan.FromSeconds(5),
            "协议版本过期时未在预算内显示重新征询。");

        var wizard = viewModel.Dialogs.SetupWizard;
        Assert.True(wizard.IsConsentUpdateMode);
        Assert.Equal(1, wizard.Step);
        Assert.True(wizard.IsLastStep);
        Assert.False(wizard.CanSkip);
        Assert.False(wizard.CanGoPrevious);
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.Japanese);
        Assert.Equal(localizer.F(LocalizationKeys.SetupWizardStepProgress, 1, 1), wizard.StepProgress);
        Assert.Equal(LauncherLanguages.Japanese, wizard.Language);
        // 复核向导沿用已保存明暗，而不是完整初始化前的进程内默认变体。
        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void SetupWizard_WhenExitingWithoutConsent_RequestsShutdownThroughTheHostWiring()
    {
        // 未同意就退出必须真正关闭启动器：向导只发事件，宿主接线负责转成关闭请求。
        using var directory = TestDirectory.Create();
        using var provider = HeadlessTestHost.CreateServiceProvider(directory);
        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        var shutdowns = 0;
        viewModel.WindowChrome.ShutdownRequested += () => shutdowns++;
        viewModel.Dialogs.ShowSetupWizard();

        viewModel.Dialogs.SetupWizard.SkipCommand.Execute(null);

        Assert.Equal(1, shutdowns);
    }

    [AvaloniaFact]
    public async Task Startup_WhenConsentIsCompleted_InitializesTheShellOnce()
    {
        // 重新征询完成时，保存后续流程已经做过一次完整刷新；会话只补命令行启动，
        // 不得再跑一遍初始化（否则一次同意会两次加载主机状态）。
        using var directory = TestDirectory.Create();
        var core = new CountingCoreService();
        using var provider = HeadlessTestHost.CreateServiceProvider(
            directory,
            services => services.AddSingleton<ILauncherCoreService>(core));
        var writer = provider.GetRequiredService<ISavedSettingsWriter>();
        var saved = LauncherSettings.CreateDefaults();
        saved.AcceptedAgreementVersion = "2020-01-01";
        saved.EnableStartupUpdateCheck = false;
        await writer.ReplaceAsync(saved);

        var session = provider.GetRequiredService<LauncherPresentationSession>();
        var window = session.CreateMainWindow();
        session.AttachStartupBehavior(firstLaunch: false, launchGameRequested: false, TestContext.Current.CancellationToken);
        window.Show();

        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        await HeadlessTestHost.WaitUntilAsync(
            () => viewModel.Dialogs.IsSetupWizardVisible,
            TimeSpan.FromSeconds(5),
            "协议版本过期时未在预算内显示重新征询。");

        var wizard = viewModel.Dialogs.SetupWizard;
        wizard.MarkAgreementRead();
        wizard.IsAgreementAccepted = true;
        await wizard.CompleteCommand.ExecuteAsync(null);

        Assert.Equal(1, core.LoadCount);
    }

    [AvaloniaFact]
    public async Task LaunchGameAsync_WithACancelledShutdownToken_DoesNotStartTheGame()
    {
        // 令牌是宿主的关闭令牌：关闭已经开始时不再发起新的游戏启动（命令本身不接受取消，
        // 因此拦截点就在入口），而不是把令牌丢掉、照旧启动。
        using var directory = TestDirectory.Create();
        using var provider = HeadlessTestHost.CreateServiceProvider(directory);
        var session = provider.GetRequiredService<LauncherPresentationSession>();
        session.CreateMainWindow();
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => session.LaunchGameAsync(shutdown.Token));
    }

    [AvaloniaFact]
    public async Task Shutdown_DuringStartupInitialization_WaitsForTheStartupContinuationWithoutFaulting()
    {
        // 启动初始化以显式弃等执行（审计 AUD-ARCH-019）：关闭时必须先等这条续体结束，
        // 之后宿主才能安全释放关闭令牌源；续体不得把关闭竞态变成未观察任务异常。
        using var directory = TestDirectory.Create();
        using var shutdown = new CancellationTokenSource();
        var settings = new InFlightStartupRead(shutdown.Token);
        using var provider = HeadlessTestHost.CreateServiceProvider(directory, services =>
            services.AddSingleton<ILauncherSettingsService>(settings));
        var session = provider.GetRequiredService<LauncherPresentationSession>();
        var window = session.CreateMainWindow();
        session.AttachStartupBehavior(firstLaunch: false, launchGameRequested: false, shutdown.Token);
        window.Show();

        await settings.StartupReadEntered.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(session.IsStartupFinished, "启动续体应在窗口打开后处于在途状态。");

        await shutdown.CancelAsync();

        var stopwatch = Stopwatch.StartNew();
        await session.PrepareForShutdownAsync().WaitAsync(
            LauncherPresentationSession.StartupShutdownWaitBudget + TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < LauncherPresentationSession.StartupShutdownWaitBudget,
            $"关闭屏障等了 {stopwatch.Elapsed}，超过了启动收口预算。");
        Assert.True(session.IsStartupFinished, "关闭屏障返回时启动续体仍在运行。");
        var startup = session.StartupTask
            ?? throw new InvalidOperationException("窗口打开后启动续体必须被跟踪。");
        Assert.True(startup.IsCompletedSuccessfully, $"启动续体留下了 {startup.Status}。");
        Assert.True(settings.StartupReadCanceled, "启动读取没有观察到关闭令牌。");
    }

    [AvaloniaFact]
    public async Task PrepareForShutdownAsync_WhenStartupIgnoresTheShutdownToken_ReturnsWithinTheWaitBudget()
    {
        // 有界收口：启动续体不响应取消时，关闭屏障按预算返回，绝不被无限挂住；
        // 此时宿主据 IsStartupFinished 保留令牌源而不是释放它。
        using var directory = TestDirectory.Create();
        using var shutdown = new CancellationTokenSource();
        var settings = new StubbornStartupRead(shutdown.Token);
        using var provider = HeadlessTestHost.CreateServiceProvider(directory, services =>
            services.AddSingleton<ILauncherSettingsService>(settings));
        var session = provider.GetRequiredService<LauncherPresentationSession>();
        var window = session.CreateMainWindow();
        session.AttachStartupBehavior(firstLaunch: false, launchGameRequested: false, shutdown.Token);
        window.Show();

        await settings.StartupReadEntered.WaitAsync(TimeSpan.FromSeconds(5));
        await shutdown.CancelAsync();

        var stopwatch = Stopwatch.StartNew();
        await session.PrepareForShutdownAsync().WaitAsync(
            LauncherPresentationSession.StartupShutdownWaitBudget + TimeSpan.FromSeconds(10));
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < LauncherPresentationSession.StartupShutdownWaitBudget + TimeSpan.FromSeconds(5),
            $"关闭屏障没有按预算收口：{stopwatch.Elapsed}。");
        Assert.False(session.IsStartupFinished, "启动续体仍在运行，宿主不得释放关闭令牌源。");

        settings.ReleaseStartupRead();
        var startup = session.StartupTask
            ?? throw new InvalidOperationException("窗口打开后启动续体必须被跟踪。");
        await startup.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PrepareForShutdownAsync_WithRealDownload_WaitsForCancellationCleanupAndPreservesStopIntent(
        bool paused, bool userStopped)
    {
        using var directory = TestDirectory.Create();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloader = new StubFileDownloadService(async (_, control, token) =>
        {
            await control.ReportProgressAsync(0, token);
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            finally
            {
                canceled.TrySetResult();
                await releaseCleanup.Task;
            }
        });
        var transport = new StubRemoteHttpTransport(uri =>
            uri.AbsolutePath.Contains("/api/launcher/game/config/json", StringComparison.Ordinal)
                ? "{\"code\":200,\"data\":{\"url\":\"https://manifest.example.invalid/manifest.json\"}}"
                : JsonSerializer.Serialize(new RemoteManifest
                {
                    Source = "source",
                    File = [new ManifestFile { Path = "data/file.bin", Size = "1", Hash = "0" }]
                }));
        using var provider = HeadlessTestHost.CreateServiceProvider(directory, services =>
        {
            services.AddSingleton<IRemoteHttpTransport>(transport);
            services.AddSingleton<IFileDownloadService>(downloader);
            services.AddSingleton<IGameProcessTracker>(TestGameProcessTracker.None());
        });
        var session = provider.GetRequiredService<LauncherPresentationSession>();
        session.CreateMainWindow();
        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        var gamePath = Path.Combine(directory.Path, "YostarGames", "BlueArchive_JP");
        var settings = new LauncherSettings { GamePath = gamePath, ProxyMode = ProxyModes.Direct };
        await provider.GetRequiredService<ISavedSettingsWriter>().ReplaceAsync(settings);
        viewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        {
            Settings = settings,
            LocalGame = new LocalInstallationState { GamePath = gamePath },
            RuntimeState = LauncherRuntimeState.NotInstalled,
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse
                {
                    GameLatestVersion = "1.0.0", GameLatestFilePath = "manifest.json", GameStartExeName = "CafeTestGame"
                },
                CdnConfig = new CdnConfigResponse { PrimaryCdn = "https://cdn.example.invalid", BackUpCdn = "https://backup.example.invalid" }
            }
        });
        viewModel.Shell.IsBusy = false;
        var operation = viewModel.Operations.InstallOrUpdateCommand.ExecuteAsync(null);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var downloads = provider.GetRequiredService<GameDownloadService>();
            if (paused)
            {
                downloads.Pause();
                Assert.True(downloads.IsPaused);
            }

            if (userStopped)
            {
                viewModel.Operations.StopOperation(GameOperationStopIntent.UserStop);
                Assert.False(downloads.IsRunning);
            }

            var shutdown = session.PrepareForShutdownAsync();
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(shutdown.IsCompleted);
            Assert.False(operation.IsCompleted);
            releaseCleanup.TrySetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(!userStopped, File.Exists(directory.DataRoot.DownloadStatePath));
            Assert.False(downloads.IsRunning);
            Assert.Empty(viewModel.Toasts.ActiveToasts);
        }
        finally
        {
            releaseCleanup.TrySetResult();
            viewModel.Operations.StopOperation(GameOperationStopIntent.ProcessExit);
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
    /// <summary>记录主机状态加载次数；跨刷新线程递增，读取按原子操作。</summary>
    private sealed class CountingCoreService : ILauncherCoreService
    {
        private int loadCount;

        public int LoadCount => Volatile.Read(ref loadCount);

        public Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref loadCount);
            return Task.FromResult(new LauncherStatusSnapshot());
        }
    }

    /// <summary>
    /// 启动读取在途的替身：只让宿主关闭令牌上的那次读取挂起（与真实读取一样把令牌注册进去），
    /// 其他协作者顺带读取设置时立即返回默认值。
    /// </summary>
    private sealed class InFlightStartupRead : ILauncherSettingsService
    {
        private readonly CancellationToken shutdownToken;
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public InFlightStartupRead(CancellationToken shutdownToken)
        {
            this.shutdownToken = shutdownToken;
        }

        public Task StartupReadEntered => entered.Task;

        public bool StartupReadCanceled { get; private set; }

        public async Task<LauncherSettings> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken != shutdownToken)
            {
                return LauncherSettings.CreateDefaults();
            }

            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                StartupReadCanceled = true;
                throw;
            }

            return LauncherSettings.CreateDefaults();
        }
    }

    /// <summary>故意不响应取消的启动读取：只有测试放行才结束，用来证明关闭屏障有明确上限。</summary>
    private sealed class StubbornStartupRead : ILauncherSettingsService
    {
        private readonly CancellationToken shutdownToken;
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StubbornStartupRead(CancellationToken shutdownToken)
        {
            this.shutdownToken = shutdownToken;
        }

        public Task StartupReadEntered => entered.Task;

        public void ReleaseStartupRead() => release.TrySetResult();

        public async Task<LauncherSettings> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken != shutdownToken)
            {
                return LauncherSettings.CreateDefaults();
            }

            entered.TrySetResult();
            await release.Task;
            // 放行后按取消结束：关闭令牌确实已取消，续体因此走既有收尾分支而不是继续初始化。
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private static CrashReport CreateCrashReport() => new()
    {
        Id = "CR-TEST",
        OccurredAt = DateTimeOffset.UnixEpoch,
        Source = "test",
        AppVersion = "1.0.0",
        BuildSha = "abc1234",
        OperatingSystem = "test",
        UiCulture = "en",
        ExceptionType = nameof(InvalidOperationException),
        TechnicalDetails = "test"
    };
}
