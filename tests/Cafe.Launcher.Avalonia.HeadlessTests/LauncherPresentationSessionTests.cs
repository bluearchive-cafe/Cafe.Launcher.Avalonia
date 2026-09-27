using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.Avalonia.UI;
using Cafe.Launcher.Avalonia.UI.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// <see cref="LauncherPresentationSession"/> 是宿主与表现层之间唯一的生命周期词汇表：
/// 它必须真的转发每次调用、释放幂等，并在释放后拒绝继续回调宿主闭包。
/// 这些断言也是 UI 程序集当前唯一的覆盖来源（迁移阶段它还没有视图）。
/// </summary>
public sealed class LauncherPresentationSessionTests
{
    [AvaloniaFact]
    public void Session_ForwardsEveryLifecycleOperationToTheCallbacks()
    {
        var calls = new List<string>();
        using var provider = BuildProvider(calls);
        var callbacks = provider.GetRequiredService<LauncherPresentationCallbacks>();
        var session = provider.GetRequiredService<LauncherPresentationSession>();

        Assert.Same(callbacks, provider.GetRequiredService<LauncherPresentationCallbacks>());
        Assert.Same(session, provider.GetRequiredService<LauncherPresentationSession>());

        var window = session.CreateMainWindow();
        session.ShowWindow();
        session.InitializeAsync().GetAwaiter().GetResult();
        session.LaunchGameAsync().GetAwaiter().GetResult();
        session.PrepareForShutdownAsync().GetAwaiter().GetResult();

        Assert.IsType<Window>(window);
        Assert.Equal(["create", "show", "initialize", "launch", "shutdown"], calls);
    }

    [AvaloniaFact]
    public async Task Session_AfterDispose_RejectsLifecycleCallsAndReleasesThePresentationOnce()
    {
        var calls = new List<string>();
        using var provider = BuildProvider(calls);
        var session = provider.GetRequiredService<LauncherPresentationSession>();

        session.Dispose();
        session.Dispose();

        Assert.Equal(["dispose"], calls);
        Assert.Throws<ObjectDisposedException>(session.CreateMainWindow);
        Assert.Throws<ObjectDisposedException>(session.ShowWindow);
        // Dispose 之后的每次调用都必须在触达宿主闭包之前失败：这些任务在返回前同步抛出。
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.InitializeAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.LaunchGameAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.PrepareForShutdownAsync());
    }

    [Fact]
    public void Session_WithoutCallbacks_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LauncherPresentationSession(null!));
    }

    [Fact]
    public void AddLauncherPresentation_WithoutArguments_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LauncherPresentationServiceCollectionExtensions.AddLauncherPresentation(
                null!,
                BuildCallbacks(new List<string>())));
        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddLauncherPresentation(null!));
    }

    private static ServiceProvider BuildProvider(List<string> calls)
    {
        var services = new ServiceCollection();
        services.AddLauncherPresentation(BuildCallbacks(calls));
        return services.BuildServiceProvider();
    }

    private static LauncherPresentationCallbacks BuildCallbacks(List<string> calls) =>
        new(
            CreateMainWindow: () =>
            {
                calls.Add("create");
                return new Window();
            },
            InitializeAsync: _ =>
            {
                calls.Add("initialize");
                return Task.CompletedTask;
            },
            ShowWindow: () => calls.Add("show"),
            LaunchGameAsync: _ =>
            {
                calls.Add("launch");
                return Task.CompletedTask;
            },
            PrepareForShutdownAsync: _ =>
            {
                calls.Add("shutdown");
                return Task.CompletedTask;
            },
            Dispose: () => calls.Add("dispose"));
}
