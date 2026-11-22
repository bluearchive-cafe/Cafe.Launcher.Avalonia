using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.GameRuntime;

namespace Cafe.Launcher.Core.Models;

/// <summary>
/// 一个固定游戏的长期服务与私有路径。切换查看条目不会替换或释放此对象；
/// 生命周期结束时仅释放构造时明确移交所有权的资源。
/// </summary>
public sealed class LauncherGameContext : IDisposable
{
    private readonly IDisposable[] ownedResources;
    private readonly IGameRuntime? runtime;
    private readonly INoticeStateStore? noticeStateStore;
    private int disposed;

    /// <summary>绑定固定游戏身份与接口依赖；共享依赖不应列入拥有的资源。</summary>
    internal LauncherGameContext(
        YostarGameProfile profile,
        GameDataPaths paths,
        ILauncherApiClient apiClient,
        ILocalInstallationStateStore localInstallationStateStore,
        IGameInstallationPath installationPath,
        ILauncherStatusSnapshotService coreService,
        IGameProcessTracker processTracker,
        IReadOnlyList<IDisposable> ownedResources,
        RunnerOutputCapture? runnerOutputCapture = null,
        CompatibilityEnvironmentPrecheck? compatibilityEnvironmentPrecheck = null,
        PrefixMetadataStore? prefixMetadataStore = null,
        IGameRuntime? runtime = null,
        INoticeStateStore? noticeStateStore = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(localInstallationStateStore);
        ArgumentNullException.ThrowIfNull(installationPath);
        ArgumentNullException.ThrowIfNull(coreService);
        ArgumentNullException.ThrowIfNull(processTracker);
        ArgumentNullException.ThrowIfNull(ownedResources);
        if (!string.Equals(profile.RuntimeId, paths.GameId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Game profile and private paths must belong to the same game.", nameof(paths));
        }

        Profile = profile;
        Paths = paths;
        ApiClient = apiClient;
        LocalInstallationStateStore = localInstallationStateStore;
        InstallationPath = installationPath;
        CoreService = coreService;
        ProcessTracker = processTracker;
        RunnerOutputCapture = runnerOutputCapture;
        CompatibilityEnvironmentPrecheck = compatibilityEnvironmentPrecheck;
        PrefixMetadataStore = prefixMetadataStore;
        this.runtime = runtime;
        this.noticeStateStore = noticeStateStore;
        this.ownedResources = ownedResources.ToArray();
        foreach (var resource in this.ownedResources)
        {
            ArgumentNullException.ThrowIfNull(resource);
        }
    }

    /// <summary>此上下文固定绑定的稳定游戏身份。</summary>
    public string GameId => Profile.RuntimeId;

    /// <summary>此上下文的官方协议与运行时档案。</summary>
    public YostarGameProfile Profile { get; }

    /// <summary>同一全局数据根中此游戏的私有状态路径。</summary>
    public GameDataPaths Paths { get; }

    /// <summary>此游戏的远程协议接口。</summary>
    public ILauncherApiClient ApiClient { get; }

    /// <summary>此游戏本地安装状态的唯一读写入口。</summary>
    public ILocalInstallationStateStore LocalInstallationStateStore { get; }

    /// <summary>此游戏的安装路径规范化接口。</summary>
    public IGameInstallationPath InstallationPath { get; }

    /// <summary>聚合此游戏本地与远程状态的加载接口。</summary>
    public ILauncherStatusSnapshotService CoreService { get; }

    /// <summary>此游戏独有的进程跟踪实例，不随查看条目切换。</summary>
    public IGameProcessTracker ProcessTracker { get; }

    /// <summary>绑定此游戏进程跟踪与诊断状态的运行环境；生产工厂必须提供完整运行环境。</summary>
    public IGameRuntime Runtime => runtime
        ?? throw new InvalidOperationException("The game context has no composed runtime.");

    /// <summary>此游戏独有的公告已读状态存储。</summary>
    public INoticeStateStore NoticeStateStore => noticeStateStore
        ?? throw new InvalidOperationException("The game context has no composed notice store.");

    /// <summary>此游戏自己的运行器输出捕获；只供 Core 工厂与运行环境使用。</summary>
    internal RunnerOutputCapture? RunnerOutputCapture { get; }

    /// <summary>此游戏自己的兼容环境诊断状态。</summary>
    internal CompatibilityEnvironmentPrecheck? CompatibilityEnvironmentPrecheck { get; }

    /// <summary>此游戏自己的前缀元数据存储，兼容前缀位置仍由运行环境决定。</summary>
    internal PrefixMetadataStore? PrefixMetadataStore { get; }

    /// <summary>按移交顺序的逆序释放拥有的资源；重复释放无作用，单个失败不跳过其余资源。</summary>
    public void Dispose() => DisposeOwnedResources(preserveProcessTracker: false);

    /// <summary>工厂错误地复用其他上下文的跟踪器时，只清理新资源，保留已被登记的跟踪器。</summary>
    internal void DisposePreservingProcessTracker() => DisposeOwnedResources(preserveProcessTracker: true);

    private void DisposeOwnedResources(bool preserveProcessTracker)
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        List<Exception>? failures = null;
        for (var index = ownedResources.Length - 1; index >= 0; index--)
        {
            if (preserveProcessTracker && ReferenceEquals(ownedResources[index], ProcessTracker))
            {
                continue;
            }

            try
            {
                ownedResources[index].Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("Game context resource disposal failed.", failures);
        }
    }
}
