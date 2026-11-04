using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Features.ResourcePanel;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Update;

namespace Cafe.Launcher.Tests;

/// <summary>
/// 主窗口 ViewModel 的测试装配：对象图由真实组合根建立
/// （<see cref="LauncherTestGraph"/>），这里只把用例显式指定的替身交给容器。
/// </summary>
/// <remarks>
/// <para><b>所有权。</b>容器持有整张图，<see cref="Dispose"/> 一次释放。调用方传入的
/// 替身、工厂与设置装配同样登记进容器并在此时一并释放——它们都实现了可重复释放，
/// 调用方持有的引用在之后仍然可读（日志探针就是靠这一点）。ViewModel 也由容器持有；
/// 用例提前释放它是安全的，容器释放是幂等的。</para>
/// <para><b>日志活期。</b>设置页、调试面板与日志查看器共用容器里的那一个
/// <see cref="UnifiedLogger"/>；它随容器一起活到用例结束，装配返回后仍可写入。</para>
/// <para><b>真实 DI 的边界。</b>对象图与生产同形——表现层的注册漂移会直接让这些用例变红，
/// 不再由测试 fixture 手工复制。</para>
/// </remarks>
internal sealed class MainWindowTestContext : IDisposable
{
    private readonly LauncherTestGraph graph;
    private bool disposed;

    private MainWindowTestContext(MainWindowViewModel viewModel, LauncherTestGraph graph)
    {
        ViewModel = viewModel;
        this.graph = graph;
        Loggers = [graph.Logger];
    }

    /// <summary>装配好的主窗口 ViewModel；提前释放由容器兜底。</summary>
    public MainWindowViewModel ViewModel { get; }

    /// <summary>
    /// 容器持有的日志器。日志在装配返回之后仍然可写，这条契约需要能直接写一条来验证。
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
        var graph = LauncherTestGraph.Create(
            directory.DataRoot,
            new LauncherTestGraphOptions
            {
                CoreService = coreService,
                SavedSettings = savedSettings ?? new SavedSettingsTestRig(directory.DataRoot),
                HttpClientFactory = httpClientFactory,
                ImageCache = imageCacheService,
                ResourcePanelUidService = resourcePanelUidService,
                ResourcePanelApiClient = resourcePanelApiClient,
                ToastService = toastService,
                LauncherUpdateService = launcherUpdateService,
                LauncherSelfUpdateService = launcherSelfUpdateService,
                LauncherUpdateApplier = launcherUpdateApplier,
                OperationExecutor = gameOperationsBackend,
                AnimationSettingsProvider = systemAnimationSettingsProvider,
                ToastDelayAsync = toastDelayAsync,
                FilePicker = filePickerService
            });
        return new MainWindowTestContext(graph.MainWindow, graph);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        graph.Dispose();
    }
}
