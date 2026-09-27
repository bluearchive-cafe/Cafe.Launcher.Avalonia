using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Auth;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Services.Update;

namespace Cafe.Launcher.Core.Composition;

/// <summary>
/// Registers services owned by the launcher Core boundary. The host must call
/// this before registering a presentation layer so Microsoft DI releases UI
/// objects first during shutdown.
/// </summary>
public static class LauncherCoreServiceCollectionExtensions
{
    /// <param name="launcherDataRoot">
    /// The single process data root, resolved once by the composition root and injected here.
    /// Core never resolves it itself: <c>TestUserDataIsolationTests</c> keeps that resolution
    /// confined to the declared pre-DI sites (ADR-025).
    /// </param>
    public static IServiceCollection AddLauncherCore(
        this IServiceCollection services,
        LauncherBuildIdentity buildIdentity,
        LauncherDataRoot launcherDataRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(buildIdentity);
        ArgumentNullException.ThrowIfNull(launcherDataRoot);

        services.TryAddSingleton(buildIdentity);
        services.TryAddSingleton(launcherDataRoot);
        // These services already have a stable, presentation-free dependency
        // closure. Keeping their registration here is significant: UI services
        // registered afterwards are disposed first by the Microsoft DI container.
        // 实现与接口映射到同一实例：这三个服务都持有状态（磁盘空间缓存、按路径的引用计数信号量），
        // 各自注册两份会让表现层拿到另一个实例。
        // 诊断门面：实现从容器里的 UnifiedLogger 惰性构造（宿主/表现层在各自登记阶段把它放进来），
        // 共享静态缝的唯一登记所有方也在这里——AddLauncherCore 是整张对象图的起点，只调用一次。
        services.TryAddSingleton(sp =>
        {
            // 进程日志器通常由组合根在 pre-DI 阶段建好并登记；Core-only 容器（测试、幂等性守卫）
            // 没有它，此时按数据根与身份自建一个，语义与表现层的兜底一致。
            var logger = sp.GetService<UnifiedLogger>()
                ?? new UnifiedLogger(
                    sp.GetRequiredService<LauncherDataRoot>().Root,
                    sp.GetRequiredService<LauncherBuildIdentity>());
            LocalDiagnostics.RegisterSharedLogger(logger);
            return new LocalDiagnostics(logger);
        });
        services.TryAddSingleton<ILauncherDiagnostics>(sp => sp.GetRequiredService<LocalDiagnostics>());        services.TryAddSingleton<Crc64Service>();
        services.TryAddSingleton<ICrc64Service>(sp => sp.GetRequiredService<Crc64Service>());
        services.TryAddSingleton<DiskSpaceService>();
        services.TryAddSingleton<IDiskSpaceService>(sp => sp.GetRequiredService<DiskSpaceService>());
        services.TryAddSingleton<LocalInstallationStateStore>();
        services.TryAddSingleton<ILocalInstallationStateStore>(sp => sp.GetRequiredService<LocalInstallationStateStore>());
        services.TryAddSingleton<AuthorizationHeaderFactory>();
        services.TryAddSingleton<BestHttpCookieLibraryService>();
        services.TryAddSingleton<PatchUrlGroupService>();
        services.TryAddSingleton<RemoteHttpUrlValidator>();
        services.TryAddSingleton<IRemoteHttpUrlValidator>(sp => sp.GetRequiredService<RemoteHttpUrlValidator>());
        services.TryAddSingleton(sp => new LauncherSettingsService(
            sp.GetRequiredService<LauncherDataRoot>(),
            sp.GetService<ILauncherDiagnostics>(),
            buildIdentity: buildIdentity));
        // 已保存设置的唯一写入方。草稿所有者由表现层登记（ISettingsDraftOwner）——写入方不认识
        // 具体编辑器，UI 线程编排留在实现方。
        services.TryAddSingleton<ISavedSettingsWriter, SavedSettingsWriter>();
        services.TryAddSingleton<ILauncherSettingsService>(sp => sp.GetRequiredService<LauncherSettingsService>());
        // 代理解析与连接池属于后端：HttpClientFactory 的注册仍留在组合根，因为它的
        // 偏好闭包读的是表现层的设置快照（ADR-028 的按使用时机拉取）。
        services.TryAddSingleton<ProxySettingsService>();
        services.TryAddSingleton<NoticeStateService>();
        services.TryAddSingleton<SystemAnimationSettingsProvider>();
        services.TryAddSingleton<ILauncherCoreService, LauncherCoreService>();
        services.TryAddSingleton(sp => new ImageCacheService(
            sp.GetRequiredService<IRemoteHttpTransport>(),
            sp.GetRequiredService<Crc64Service>(),
            sp.GetRequiredService<LauncherDataRoot>(),
            sp.GetRequiredService<ILauncherDiagnostics>(),
            sp.GetRequiredService<LauncherBuildIdentity>()));        // 自更新：检查、宿主信息、下载器、应用器与自更新服务。应用器先于自更新服务注册：
        // 可用性判定（本机是否带 helper）由应用器回答。
        services.TryAddSingleton<LauncherUpdateService>();
        services.TryAddSingleton<ILauncherUpdateHostInfoProvider, LauncherUpdateHostInfoProvider>();
        services.TryAddSingleton<ILauncherUpdateDownloader, LauncherUpdateDownloader>();
        services.TryAddSingleton<IWindowsLauncherUpdateApplier, WindowsLauncherUpdateApplier>();
        services.TryAddSingleton(sp => new LauncherSelfUpdateService(
            sp.GetRequiredService<ILauncherUpdateDownloader>(),
            sp.GetRequiredService<ILauncherUpdateHostInfoProvider>(),
            sp.GetRequiredService<IWindowsLauncherUpdateApplier>(),
            sp.GetRequiredService<LauncherDataRoot>(),
            sp.GetRequiredService<ILauncherDiagnostics>()));        services.TryAddSingleton<IFileDownloadService, FileDownloadService>();
        // 游戏运行时：进程启动、跟踪、兼容预检、前缀元数据与会话运行器定义。
        services.TryAddSingleton<GameInstallationPath>();
        services.TryAddSingleton<IProcessLauncher, DefaultProcessLauncher>();
        services.TryAddSingleton<RunnerOutputCapture>();
        services.TryAddSingleton<CompatibilityEnvironmentPrecheck>();
        services.TryAddSingleton<PrefixMetadataStore>();
        services.TryAddSingleton<IGameProcessTracker, GameProcessTracker>();
        services.TryAddSingleton<IGameRuntime>(sp => new GameRuntime(
            [GameRunnerDefinition.Native, GameRunnerDefinition.Umu, GameRunnerDefinition.Wine],
            sp.GetRequiredService<IProcessLauncher>(),
            sp.GetRequiredService<IGameProcessTracker>(),
            sp.GetRequiredService<RunnerOutputCapture>(),
            sp.GetRequiredService<CompatibilityEnvironmentPrecheck>(),
            sp.GetRequiredService<PrefixMetadataStore>()));
        return services;
    }
}
