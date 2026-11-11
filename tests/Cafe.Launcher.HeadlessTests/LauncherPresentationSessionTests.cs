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
using Cafe.Launcher.UI.Services.Diagnostics;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI;
using Cafe.Launcher.UI.Composition;
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
        Assert.Equal("ステップ 1 / 1", wizard.StepProgress);
        Assert.Equal(LauncherLanguages.Japanese, wizard.Language);
        // 复核向导沿用已保存明暗，而不是完整初始化前的进程内默认变体。
        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
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
