using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Composition;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Features.Diagnostics;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Features.ResourcePanel;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Features.SetupWizard;
using Cafe.Launcher.UI.Features.Shell;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Services.Diagnostics;
using Cafe.Launcher.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Tests;

/// <summary>
/// 测试侧的真实组合根：调用宿主的 <see cref="ServiceConfiguration.AddLauncherServices"/>
/// 建立与生产同形的对象图，再只为确定性无关的边界换入替身（传输、更新、执行器、选择器、时钟）。
/// </summary>
/// <remarks>
/// <para><b>为什么不再手工拼图。</b>手工 fixture 与 53 条真实注册并行维护，注册一加就会
/// 悄悄漂移；这里把「表现图怎么拼」交还组合根，测试只声明它要观察或替换的那几个接缝。</para>
/// <para><b>所有权。</b>测试图持有全部资源；<see cref="Dispose"/> 先释放容器，再释放显式持有的外部装配。调用方传入的
/// 替身与工厂在此时一并释放——它们都实现了可重复释放，调用方无需再管。</para>
/// <para><b>默认替身。</b>下载传输、图片缓存、更新检查与自更新都换成不发真实请求的实现；
/// 游戏快捷方式与会话看护用无副作用的替身，避免测试在宿主机上创建快捷方式或订阅真实进程。</para>
/// </remarks>
internal sealed class LauncherTestGraph : IDisposable
{
    private readonly ServiceProvider provider;
    private readonly IDisposable[] externalResources;
    private bool disposed;

    private LauncherTestGraph(ServiceProvider provider, IDisposable[] externalResources)
    {
        this.provider = provider;
        this.externalResources = externalResources;
    }

    /// <summary>容器为整棵对象图建立的日志器；测试可直接对它写探针。</summary>
    public UnifiedLogger Logger => provider.GetRequiredService<UnifiedLogger>();

    /// <summary>与生产同形的表现图。</summary>
    public ShellPresentationFamily Family => provider.GetRequiredService<ShellPresentationFamily>();

    /// <summary>壳生命周期（接线模块的唯一持有者）。</summary>
    public ShellLifecycle Lifecycle => provider.GetRequiredService<ShellLifecycle>();

    /// <summary>主窗口 ViewModel。</summary>
    public MainWindowViewModel MainWindow => provider.GetRequiredService<MainWindowViewModel>();

    public T Get<T>()
        where T : notnull => provider.GetRequiredService<T>();

    /// <summary>按真实注册建立对象图，只替换 <paramref name="options"/> 指定的接缝。</summary>
    public static LauncherTestGraph Create(
        LauncherDataRoot dataRoot,
        LauncherTestGraphOptions? options = null,
        Action<IServiceCollection>? alterProductionRegistrations = null)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        options ??= new LauncherTestGraphOptions();

        var services = new ServiceCollection();
        services.AddLauncherServices(
            existingFatalCrashService: options.FatalCrashService ?? new StubFatalCrashService(),
            launcherDataRoot: dataRoot);
        // Failure injection verifies that a missing production VM cannot be hidden by this fixture.
        alterProductionRegistrations?.Invoke(services);

        if (options.CoreService is not null)
        {
            services.AddSingleton(options.CoreService);
        }

        if (options.SavedSettings is { } savedSettings)
        {
            services.AddSingleton(savedSettings.SettingsService);
            services.AddSingleton(savedSettings.Editor);
            services.AddSingleton<ISavedSettingsWriter>(_ => savedSettings.Writer);
        }

        if (options.HttpClientFactory is not null)
        {
            services.AddSingleton(options.HttpClientFactory);
        }

        var imageCache = options.ImageCache ?? new RemoteImageStore(
                new StubRemoteHttpTransport(),
                new Crc64Service(),
                dataRoot);
        services.AddSingleton(imageCache);

        if (options.ResourcePanelUidService is not null)
        {
            services.AddSingleton(options.ResourcePanelUidService);
        }

        if (options.ResourcePanelApiClient is not null)
        {
            services.AddSingleton(options.ResourcePanelApiClient);
        }
        else
        {
            // 资源面板打开会发请求；默认替身让测试既不联网也不等待超时。
            services.AddSingleton(new ResourcePanelApiClient(
                LauncherProfiles.Cafe,
                new StubRemoteHttpTransport()));
        }

        services.AddSingleton(options.ToastService ?? new ToastService());
        services.AddSingleton<ILauncherUpdateService>(
            options.LauncherUpdateService
            ?? new LauncherUpdateService(
                LauncherProfiles.Cafe,
                CreateNotFoundTransport(),
                currentVersionOverride: "0.0.0"));

        if (options.LauncherSelfUpdateService is not null)
        {
            services.AddSingleton(options.LauncherSelfUpdateService);
        }

        if (options.LauncherUpdateApplier is not null)
        {
            services.AddSingleton(options.LauncherUpdateApplier);
        }

        if (options.OperationExecutor is not null)
        {
            services.AddSingleton(options.OperationExecutor);
        }

        // 游戏侧替身：真实快捷方式会写宿主机桌面，真实看护会订阅进程跟踪器。
        services.AddSingleton<IGameShortcutService>(new TestGameShortcutService());
        services.AddSingleton<IGameSessionMonitor>(new FakeGameSessionMonitor());
        if (options.AnimationSettingsProvider is not null)
        {
            services.AddSingleton(options.AnimationSettingsProvider);
        }

        services.AddSingleton<IFilePickerService>(options.FilePicker ?? new StubFilePickerService());

        var environment = new PresentationEnvironment();
        services.AddSingleton(environment with
        {
            InvokeOnUiAsync = options.DispatchToUi
                ?? (options.ToastDelayAsync is not null ? DispatchSynchronously : environment.InvokeOnUiAsync),
            DelayAsync = options.ToastDelayAsync ?? environment.DelayAsync,
            DelayBeforeWindowActionAsync = static _ => Task.CompletedTask,
            OpenExternalUrl = options.OpenExternalUrl ?? (_ => { }),
            OpenDirectory = options.OpenDirectory ?? (_ => { })
        });

        // Instance registrations are external to Microsoft DI ownership. Keep them even if
        // no service resolves them; the graph, rather than an alias side effect, owns cleanup.
        var externalResources = new object?[]
        {
            options.CoreService, options.SavedSettings, options.HttpClientFactory, imageCache,
            options.ResourcePanelUidService, options.ResourcePanelApiClient,
            options.LauncherUpdateService, options.LauncherSelfUpdateService,
            options.LauncherUpdateApplier, options.OperationExecutor,
            options.AnimationSettingsProvider, options.FilePicker, options.FatalCrashService
        }.OfType<IDisposable>().DistinctBy(value => value, ReferenceEqualityComparer.Instance).ToArray();
        return new LauncherTestGraph(services.BuildServiceProvider(), externalResources);
    }

    private static Task DispatchSynchronously(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static StubRemoteHttpTransport CreateNotFoundTransport() => new(
        _ => throw new HttpRequestException(
            "stub transport answered NotFound",
            null,
            HttpStatusCode.NotFound));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { provider.Dispose(); }
        finally
        {
            foreach (var resource in externalResources.Reverse()) resource.Dispose();
        }
    }
}

/// <summary>测试替身接缝；未指定的项沿用真实注册。</summary>
internal sealed record LauncherTestGraphOptions
{
    public ILauncherStatusSnapshotService? CoreService { get; init; }

    public SavedSettingsTestRig? SavedSettings { get; init; }

    public HttpClientFactory? HttpClientFactory { get; init; }

    public RemoteImageStore? ImageCache { get; init; }

    public ResourcePanelUidService? ResourcePanelUidService { get; init; }

    public ResourcePanelApiClient? ResourcePanelApiClient { get; init; }

    public ToastService? ToastService { get; init; }

    public ILauncherUpdateService? LauncherUpdateService { get; init; }

    public ILauncherSelfUpdateService? LauncherSelfUpdateService { get; init; }

    public IWindowsLauncherUpdateApplier? LauncherUpdateApplier { get; init; }

    public IGameOperationExecutor? OperationExecutor { get; init; }

    public ISystemAnimationSettingsProvider? AnimationSettingsProvider { get; init; }

    public Func<Action, Task>? DispatchToUi { get; init; }

    public Func<TimeSpan, CancellationToken, Task>? ToastDelayAsync { get; init; }

    public IFilePickerService? FilePicker { get; init; }

    public Action<string?>? OpenExternalUrl { get; init; }

    public Action<string>? OpenDirectory { get; init; }

    public IFatalCrashService? FatalCrashService { get; init; }
}
