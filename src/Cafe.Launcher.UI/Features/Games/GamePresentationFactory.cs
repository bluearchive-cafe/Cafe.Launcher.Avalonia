using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;

namespace Cafe.Launcher.UI.Features.Games;

/// <summary>创建固定游戏的长期呈现族；所有依赖显式传入，不解析容器或修改全局档案。</summary>
internal sealed class GamePresentationFactory(
    IGameContextRegistry contexts,
    GamePresentationDependencies dependencies,
    Func<LauncherGameContext, IGameOperationExecutor>? executorFactory = null,
    Func<LauncherGameContext, IGameSessionMonitor>? sessionMonitorFactory = null)
{
    public GamePresentationContext Create(string gameId)
    {
        var core = contexts.GetOrCreate(gameId);
        var d = dependencies;
        var shell = new ShellViewModel(d.Product, d.Localizer, d.BuildIdentity, d.Environment, d.Toasts);
        var remote = new RemoteContentViewModel(d.Localizer, d.Images, d.Diagnostics);
        var manifests = new RemoteManifestService(core.ApiClient);
        var validation = new ManifestValidationService(core.ApiClient, manifests, d.Localizer);
        var checkpoint = new DownloadCheckpointStore(core.Paths);
        var shortcut = new GameShortcutService(core.Profile, d.Localizer);
        var launch = new GameLaunchService(core.Profile, validation, core.Runtime, d.Localizer);
        var download = new GameDownloadService(core.Profile, core.ApiClient, manifests, d.Files,
            core.LocalInstallationStateStore, d.Settings, d.Transports, d.Crc64, d.DiskSpace,
            d.Diagnostics, d.Localizer, core.InstallationPath, core.ProcessTracker, checkpoint);
        var uninstall = new GameUninstallService(core.Profile, core.LocalInstallationStateStore, d.Diagnostics,
            d.Localizer, core.InstallationPath, checkpoint, core.ProcessTracker, shortcut, d.CompatibilityPaths);
        // Optional typed factories preserve fixed context identity in tests and alternate hosts;
        // production composes each game's own services without resolving a service provider.
        IGameOperationExecutor executor = executorFactory is null
            ? new GameOperationExecutor(launch, download, uninstall)
            : executorFactory(core) ?? throw new InvalidOperationException("The game executor factory returned null.");
        IGameSessionMonitor monitor = sessionMonitorFactory is null
            ? new GameSessionMonitor(core.ProcessTracker)
            : sessionMonitorFactory(core) ?? throw new InvalidOperationException("The game session factory returned null.");
        var operations = new GameOperationsViewModel(gameId, d.FileOperations, executor, shortcut, monitor,
            d.Localizer, d.Toasts, d.Diagnostics, shell, d.Dialogs, d.Errors)
        {
            IsPresentationActive = false
        };
        return new GamePresentationContext(core, shell, remote, operations, download, monitor,
            checkpoint, shortcut, launch, uninstall, manifests, validation, executor);
    }
}

/// <summary>呈现族工厂共享的进程级依赖；游戏相关依赖只由明确的 Core context 提供。</summary>
internal sealed record GamePresentationDependencies(
    LauncherProductProfile Product,
    LauncherBuildIdentity BuildIdentity,
    LocalizationService Localizer,
    INotificationService Toasts,
    ILauncherDiagnostics Diagnostics,
    IErrorHandlingService Errors,
    ILauncherSettingsService Settings,
    IGameFileOperationCoordinator FileOperations,
    IFileDownloadService Files,
    IDownloadTransportSource Transports,
    ICrc64Service Crc64,
    IDiskSpaceService DiskSpace,
    GameCompatibilityPaths CompatibilityPaths,
    IRemoteImageStore Images,
    DialogsViewModel Dialogs,
    PresentationEnvironment Environment);

/// <summary>固定目标的 UI 服务与最近快照；切换页面只改变订阅，不释放这里的任务或会话。</summary>
internal sealed class GamePresentationContext(
    LauncherGameContext core,
    ShellViewModel shell,
    RemoteContentViewModel remoteContent,
    GameOperationsViewModel operations,
    GameDownloadService download,
    IGameSessionMonitor sessionMonitor,
    DownloadCheckpointStore checkpoint,
    GameShortcutService shortcut,
    GameLaunchService launch,
    GameUninstallService uninstall,
    RemoteManifestService manifests,
    ManifestValidationService validation,
    IGameOperationExecutor executor) : IDisposable
{
    private int disposed;
    private readonly object refreshTrackingGate = new();
    private readonly HashSet<Task> pendingRefreshes = [];
    public LauncherGameContext Core { get; } = core;
    public string GameId => Core.GameId;
    public ShellViewModel Shell { get; } = shell;
    public RemoteContentViewModel RemoteContent { get; } = remoteContent;
    public GameOperationsViewModel Operations { get; } = operations;
    public GameDownloadService Download { get; } = download;
    public IGameSessionMonitor SessionMonitor { get; } = sessionMonitor;
    public DownloadCheckpointStore Checkpoint { get; } = checkpoint;
    public GameShortcutService Shortcut { get; } = shortcut;
    public GameLaunchService Launch { get; } = launch;
    public GameUninstallService Uninstall { get; } = uninstall;
    public RemoteManifestService Manifests { get; } = manifests;
    public ManifestValidationService Validation { get; } = validation;
    public IGameOperationExecutor Executor { get; } = executor;
    public LauncherStatusSnapshot? Snapshot { get; set; }
    public SemaphoreSlim RefreshGate { get; } = new(1, 1);
    public CancellationTokenSource Lifetime { get; } = new();

    /// <summary>当前所有已接纳刷新完成的屏障，包含较晚请求已经取消时仍在收尾的旧刷新。</summary>
    public Task PendingRefresh
    {
        get { lock (refreshTrackingGate) return Task.WhenAll(pendingRefreshes.ToArray()); }
    }

    /// <summary>登记刷新后再执行；退出封闭后不新增工作，不能用最后一个已完成请求替代全部收尾。</summary>
    public Task RunRefreshAsync(Func<Task> refresh)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (refreshTrackingGate)
        {
            if (Volatile.Read(ref disposed) != 0 || Lifetime.IsCancellationRequested) return Task.CompletedTask;
            pendingRefreshes.Add(completed.Task);
        }
        return RunTrackedRefreshAsync(refresh, completed);
    }

    private async Task RunTrackedRefreshAsync(Func<Task> refresh, TaskCompletionSource completed)
    {
        try { await refresh(); }
        finally
        {
            lock (refreshTrackingGate)
            {
                pendingRefreshes.Remove(completed.Task);
                completed.TrySetResult();
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Lifetime.Cancel();
        Operations.Dispose();
        Download.Dispose();
        (SessionMonitor as IDisposable)?.Dispose();
        RemoteContent.Dispose();
        Shell.Dispose();
        var pending = PendingRefresh;
        if (pending.IsCompleted) DisposeRefreshResources();
        else _ = pending.ContinueWith(_ => DisposeRefreshResources(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void DisposeRefreshResources()
    {
        RefreshGate.Dispose();
        Lifetime.Dispose();
    }
}
