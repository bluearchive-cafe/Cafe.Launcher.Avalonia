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
/// 它自己从容器解析窗口、ViewModel 与托盘，因此这里断言的是接缝本身的契约：登记是单例、
/// 未建窗口前的调用会明确报错而不是空引用、释放幂等且在触达任何表现层服务前就失败。
/// </summary>
public sealed class LauncherPresentationSessionTests
{
    [Fact]
    public void Session_WithoutServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LauncherPresentationSession(null!));
    }

    [Fact]
    public void AddLauncherPresentation_WithoutAServiceCollection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LauncherPresentationServiceCollectionExtensions.AddLauncherPresentation(null!));
    }

    [Fact]
    public void Session_IsRegisteredAsASingleton()
    {
        using var provider = new ServiceCollection().AddLauncherPresentation().BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<LauncherPresentationSession>(),
            provider.GetRequiredService<LauncherPresentationSession>());
    }

    [Fact]
    public async Task Session_BeforeTheWindowExists_ReportsTheMissingPresentation()
    {
        // 只有会话本身的容器：任何需要窗口/ViewModel 的调用都必须明确报错，
        // 而不是在宿主已经交回控制权之后抛空引用。
        using var provider = new ServiceCollection().AddLauncherPresentation().BuildServiceProvider();
        var session = provider.GetRequiredService<LauncherPresentationSession>();

        Assert.Throws<InvalidOperationException>(() =>
            session.AttachStartupBehavior(firstLaunch: false, launchGameRequested: false, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InitializeAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.LaunchGameAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.PrepareForShutdownAsync(TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => session.CreateCrashReportWindow(new CrashReport
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
        }));
    }

    [AvaloniaFact]
    public async Task Session_AfterDispose_RejectsEveryLifecycleCall()
    {
        using var provider = new ServiceCollection().AddLauncherPresentation().BuildServiceProvider();
        var session = provider.GetRequiredService<LauncherPresentationSession>();

        session.Dispose();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.CreateMainWindow());
        Assert.Throws<ObjectDisposedException>(() => session.ShowWindow());
        Assert.Throws<ObjectDisposedException>(() => session.HideMainWindow());
        // 异步成员在触达容器之前就抛：返回的任务是同步失败地完成的。
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
}
