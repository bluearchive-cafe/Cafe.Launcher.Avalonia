using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.UI;
using Cafe.Launcher.Avalonia.UI.Composition;
using Cafe.Launcher.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

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
