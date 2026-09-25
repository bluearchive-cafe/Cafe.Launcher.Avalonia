using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Cafe.Launcher.Avalonia.Services.Update;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Composition;

public static class ServiceConfiguration
{
    /// <summary>
    /// 注册全部启动器服务。
    /// </summary>
    /// <param name="launcherDataRoot">
    /// 显式数据根：缺省时按进程解析（生产路径）。测试传它来让整张对象图——登记项与
    /// 闭包中捕获的那些（日志、崩溃快照、设置、下载检查点）——都落在同一个隔离目录里；
    /// 只替换 DI 登记项而漏掉闭包，会让测试写进真实用户数据目录。
    /// </param>
    public static IServiceCollection AddLauncherServices(
        this IServiceCollection services,
        UnifiedLogger? existingLogger = null,
        IFatalCrashService? existingFatalCrashService = null,
        LauncherDataRoot? launcherDataRoot = null)
    {
        // 进程根在这里解析一次，其余登记项与所有消费方共用这一个实例——
        // 「数据放哪」不再是各模块各自读一次的进程级静态。
        var dataRoot = launcherDataRoot ?? LauncherDataRoot.ForCurrentProcess();
        services.AddSingleton(dataRoot);

        // ── Leaf services (parameterless constructors, no deps) ──────────
        services.AddSingleton<GameInstallationPath>();
        services.AddSingleton<LocalInstallationStateStore>();
        services.AddSingleton<Crc64Service>();
        services.AddSingleton<DiskSpaceService>();
        services.AddSingleton<SystemCultureSnapshot>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<BestHttpCookieLibraryService>();

        // Reuse the pre-DI logger when provided so there is a single Serilog
        // pipeline for the entire process (crash handling + application logging).
        if (existingLogger is not null)
            services.AddSingleton(existingLogger);
        else
            services.AddSingleton(_ => new UnifiedLogger(dataRoot.Root));
        services.AddSingleton<GraphicsInfoProbe>();
        services.AddSingleton<ProtonBuildDiscovery>();
        services.AddSingleton<LogExportService>();
        services.AddSingleton<LogViewerDialogViewModel>();
        services.AddSingleton<LogExportDialogViewModel>();
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<UnifiedLogger>();
            var localDiagnostics = new LocalDiagnostics(logger);
            // 本组合根是共享静态缝的唯一登记所有方（R2-c12）：先注册者胜，
            // 后续容器（多容器测试）不改绑，也不得在其他文件登记（有源守卫）。
            LocalDiagnostics.RegisterSharedLogger(logger);
            return localDiagnostics;
        });
        services.AddSingleton(_ => new CrashReportStore(
            dataRoot,
            CrashReportStore.DefaultFallbackDirectory));
        services.AddSingleton<ICrashReportLocator>(sp => sp.GetRequiredService<CrashReportStore>());
        services.AddSingleton<ICrashReporterLauncher, CrashReporterLauncher>();
        if (existingFatalCrashService is not null)
        {
            services.AddSingleton(existingFatalCrashService);
        }
        else
        {
            services.AddSingleton<IFatalCrashService, FatalCrashService>();
        }
        services.AddSingleton<SetupWizardViewModel>();
        services.AddSingleton<AuthorizationHeaderFactory>();
        services.AddSingleton<RemoteHttpUrlValidator>();
        services.AddSingleton<PatchUrlGroupService>();
        services.AddSingleton<RemoteManifestService>();
        services.AddSingleton<IFileDownloadService, FileDownloadService>();
        services.AddSingleton<ResourcePanelService>();

        // ── HttpClient factory (shared pool, proxy-aware) ────────────────
        services.AddSingleton(sp =>
        {
            // HTTP/2 偏好与代理模式同源：都按使用时机读编辑器的已保存快照，
            // 于是调用方不必「记得推」，也不会有租约用到过期的开关（ADR-028）。
            var settingsEditor = sp.GetRequiredService<SettingsEditor>();
            return new HttpClientFactory(
                sp.GetRequiredService<ProxySettingsService>(),
                () => settingsEditor.GetSavedSnapshot().EnableHttp2);
        });
        services.AddSingleton<IRemoteHttpTransport>(sp =>
        {
            // SettingsEditor 是无依赖单例，在传输构造时一次解析并闭包引用；
            // 代理模式解析不再每次走服务定位。
            var settingsEditor = sp.GetRequiredService<SettingsEditor>();
            return new RemoteHttpTransport(
                sp.GetRequiredService<HttpClientFactory>(),
                sp.GetRequiredService<RemoteHttpUrlValidator>(),
                // 代理模式解析自设置编辑器的已保存快照——与各调用方此前传入的
                // snapshot.ProxyMode 同源；options.ProxyMode 仍可按调用覆盖。
                () => settingsEditor.GetSavedSnapshot().ProxyMode);
        });
        services.AddSingleton<WindowFilePickerService>();
        services.AddSingleton<IFilePickerService>(sp =>
            sp.GetRequiredService<WindowFilePickerService>());
        services.AddSingleton<WindowMetricsService>();
        services.AddSingleton<IWindowMetricsService>(sp =>
            sp.GetRequiredService<WindowMetricsService>());

        // ── Services with dependencies ────────────────────────────────────
        services.AddSingleton<ProxySettingsService>();
        services.AddSingleton<ManifestValidationService>();
        services.AddSingleton<NoticeStateService>();
        services.AddSingleton(sp => new ResourcePanelUidService(
            sp.GetRequiredService<BestHttpCookieLibraryService>(),
            sp.GetRequiredService<LauncherSettingsService>(),
            sp.GetRequiredService<ISavedSettingsWriter>(),
            sp.GetRequiredService<LocalDiagnostics>()));
        services.AddSingleton(sp => new LauncherSettingsService(
            dataRoot,
            sp.GetRequiredService<LocalDiagnostics>()));
        services.AddSingleton<SystemAnimationSettingsProvider>();
        services.AddSingleton<SettingsEditor>();
        // 已保存设置的唯一写入方：依赖编辑器与设置服务，二者都登记在它之前。
        services.AddSingleton<ISavedSettingsWriter, SavedSettingsWriter>();
        services.AddSingleton<SettingsOptionsViewModel>();
        // 主题应用器登记在设置外观 VM 之前：容器按登记逆序释放，它的退订要晚于消费它的 VM。
        services.AddSingleton<ThemeApplier>();
        services.AddSingleton(sp => new SettingsAppearanceViewModel(
            sp.GetRequiredService<SettingsEditor>(),
            sp.GetRequiredService<ThemeApplier>(),
            sp.GetRequiredService<LocalDiagnostics>(),
            Program.ShowHiddenSettings));
        services.AddSingleton<IProcessLauncher, DefaultProcessLauncher>();
        services.AddSingleton<RunnerOutputCapture>();
        services.AddSingleton<CompatibilityEnvironmentPrecheck>();
        services.AddSingleton<PrefixMetadataStore>();
        services.AddSingleton<IGameRuntime>(sp => new GameRuntime(
            [GameRunnerDefinition.Native, GameRunnerDefinition.Umu, GameRunnerDefinition.Wine],
            sp.GetRequiredService<IProcessLauncher>(),
            sp.GetRequiredService<IGameProcessTracker>(),
            sp.GetRequiredService<RunnerOutputCapture>(),
            sp.GetRequiredService<CompatibilityEnvironmentPrecheck>(),
            sp.GetRequiredService<PrefixMetadataStore>()));
        services.AddSingleton<IGameProcessTracker, GameProcessTracker>();
        // 会话看护订阅进程跟踪器的退出事件：登记在跟踪器之后，容器逆序释放时看护先于
        // 跟踪器析构，退订不会落在已释放的订阅源上。
        services.AddSingleton<IGameSessionMonitor, GameSessionMonitor>();
        // 持久化检查点存储全库单例：下载服务写入/清除，卸载服务清除——
        // 同一文件只允许一个所有者实例。
        services.AddSingleton(_ => new DownloadCheckpointStore(dataRoot));
        services.AddSingleton<GameLaunchService>();
        services.AddSingleton<GameUninstallService>();
        services.AddSingleton<IGameShortcutService, GameShortcutService>();
        services.AddSingleton<IGameOperationExecutor>(sp => new GameOperationExecutor(
            sp.GetRequiredService<GameLaunchService>(),
            sp.GetRequiredService<GameDownloadService>(),
            sp.GetRequiredService<GameUninstallService>()));
        services.AddSingleton<LauncherUpdateService>();
        services.AddSingleton<ILauncherUpdateHostInfoProvider, LauncherUpdateHostInfoProvider>();
        services.AddSingleton<ILauncherUpdateDownloader, LauncherUpdateDownloader>();
        // 应用器先于自更新服务注册：可用性判定（本机是否带 helper）由应用器回答，
        // 自更新服务据此决定是应用内下载还是回退发布页。
        services.AddSingleton<IWindowsLauncherUpdateApplier, WindowsLauncherUpdateApplier>();
        services.AddSingleton(sp => new LauncherSelfUpdateService(
            sp.GetRequiredService<ILauncherUpdateDownloader>(),
            sp.GetRequiredService<ILauncherUpdateHostInfoProvider>(),
            sp.GetRequiredService<IWindowsLauncherUpdateApplier>(),
            dataRoot,
            sp.GetRequiredService<LocalDiagnostics>()));

        services.AddSingleton<ILauncherCoreService, LauncherCoreService>();
        services.AddSingleton<IErrorHandlingService, ErrorHandlingService>();

        // ── IDisposable services ─────────────────────────────────────────
        // The container disposes created services in reverse order. This keeps
        // HttpClientFactory alive until all clients and download services are gone.
        services.AddSingleton<LauncherApiClient>(sp => new LauncherApiClient(
            sp.GetRequiredService<IRemoteHttpTransport>(),
            sp.GetRequiredService<AuthorizationHeaderFactory>(),
            sp.GetRequiredService<PatchUrlGroupService>(),
            sp.GetRequiredService<LocalDiagnostics>()));
        services.AddSingleton<ResourcePanelApiClient>();
        services.AddSingleton<ImageCacheService>(sp => new ImageCacheService(
            sp.GetRequiredService<IRemoteHttpTransport>(),
            sp.GetRequiredService<Crc64Service>(),
            dataRoot,
            sp.GetRequiredService<LocalDiagnostics>()));
        services.AddSingleton(sp => new GameDownloadService(
            sp.GetRequiredService<LauncherApiClient>(),
            sp.GetRequiredService<RemoteManifestService>(),
            sp.GetRequiredService<IFileDownloadService>(),
            sp.GetRequiredService<LocalInstallationStateStore>(),
            sp.GetRequiredService<LauncherSettingsService>(),
            sp.GetRequiredService<HttpClientFactory>(),
            sp.GetRequiredService<RemoteHttpUrlValidator>(),
            sp.GetRequiredService<Crc64Service>(),
            sp.GetRequiredService<DiskSpaceService>(),
            sp.GetRequiredService<LocalDiagnostics>(),
            sp.GetRequiredService<LocalizationService>(),
            sp.GetRequiredService<GameInstallationPath>(),
            sp.GetRequiredService<IGameProcessTracker>(),
            sp.GetRequiredService<DownloadCheckpointStore>()));

        // ── ViewModels (all singleton — single-window desktop app) ─────────
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ResourcePanelViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<BackgroundViewModel>();
        services.AddSingleton<RemoteContentViewModel>();
        services.AddSingleton<DialogsViewModel>();
        services.AddSingleton(sp => new GameOperationsViewModel(
            sp.GetRequiredService<IGameOperationExecutor>(),
            sp.GetRequiredService<IGameShortcutService>(),
            sp.GetRequiredService<IGameSessionMonitor>(),
            sp.GetRequiredService<LocalizationService>(),
            sp.GetRequiredService<ToastService>(),
            sp.GetRequiredService<LocalDiagnostics>(),
            sp.GetRequiredService<ShellViewModel>(),
            sp.GetRequiredService<DialogsViewModel>(),
            sp.GetRequiredService<IErrorHandlingService>()));
        services.AddSingleton<DebugViewModel>();
        services.AddSingleton<IGameOperationActivity>(sp =>
            sp.GetRequiredService<GameOperationsViewModel>());
        services.AddSingleton<ToastHostViewModel>();
        services.AddSingleton<WindowChromeViewModel>();
        services.AddSingleton<ModalHostViewModel>();
        services.AddSingleton<ShellPresentationFamily>();
        services.AddSingleton<ShellLifecycle>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ISystemTrayActions, SystemTrayActions>();

        return services;
    }
}
