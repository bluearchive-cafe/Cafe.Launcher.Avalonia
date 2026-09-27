using System;
using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.UI.Features.Diagnostics;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Features.ResourcePanel;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Features.SetupWizard;
using Cafe.Launcher.UI.Features.Shell;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Services.Diagnostics;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Diagnostics;

namespace Cafe.Launcher.UI.Composition;

/// <summary>
/// 表现层（UI 程序集）的服务登记入口。宿主只调用本方法与 Core 的 <c>AddLauncherCore</c>，
/// 不再逐个命名表现层内部类型——那些类型对宿主保持 <c>internal</c>，
/// 「宿主引用 UI 内部实现」这类耦合在编译期就不可能再出现。
/// </summary>
public static class LauncherPresentationServiceRegistrations
{
    /// <summary>
    /// 登记表现层服务。调用顺序：宿主先 <c>AddLauncherCore</c>（Core 服务先入容器，
    /// 逆序释放时 UI 先析构），再登记宿主自有服务（如崩溃报告进程拉起器），最后调用本方法。
    /// </summary>
    /// <param name="launcherDataRoot">组合根解析一次的数据根。</param>
    /// <param name="buildIdentity">宿主注入的构建标识（版本/提交/构建时间/配置）。</param>
    /// <param name="existingLogger">pre-DI 阶段建立的日志器：整进程共用同一条 Serilog 管道。</param>
    /// <param name="existingFatalCrashService">pre-DI 阶段的致命崩溃服务（测试与辅助宿主可传入）。</param>
    /// <param name="showHiddenSettings">是否显示隐藏设置分区（宿主解析命令行后传入）。</param>
    public static IServiceCollection AddLauncherPresentationServices(
        this IServiceCollection services,
        LauncherDataRoot launcherDataRoot,
        LauncherBuildIdentity buildIdentity,
        UnifiedLogger? existingLogger = null,
        IFatalCrashService? existingFatalCrashService = null,
        bool showHiddenSettings = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(launcherDataRoot);
        ArgumentNullException.ThrowIfNull(buildIdentity);

        var dataRoot = launcherDataRoot;

        // ── Leaf services (parameterless constructors, no deps) ──────────
        services.AddSingleton<SystemCultureSnapshot>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ToastService>();

        // Reuse the pre-DI logger when provided so there is a single Serilog
        // pipeline for the entire process (crash handling + application logging).
        if (existingLogger is not null)
            services.AddSingleton(existingLogger);
        else
            services.AddSingleton(_ => new UnifiedLogger(dataRoot.Root, buildIdentity));
        services.AddSingleton<GraphicsInfoProbe>();
        services.AddSingleton<ProtonBuildDiscovery>();
        services.AddSingleton<LogExportService>();
        services.AddSingleton<LogViewerDialogViewModel>();
        services.AddSingleton<LogExportDialogViewModel>();

        services.AddSingleton(_ => new CrashReportStore(
            dataRoot,
            CrashReportStore.DefaultFallbackDirectory,
            buildIdentity));
        services.AddSingleton<ICrashReportLocator>(sp => sp.GetRequiredService<CrashReportStore>());
        if (existingFatalCrashService is not null)
        {
            services.AddSingleton(existingFatalCrashService);
        }
        else
        {
            services.AddSingleton<IFatalCrashService, FatalCrashService>();
        }
        services.AddSingleton<SetupWizardViewModel>();
        services.AddSingleton<RemoteManifestService>();
        services.AddSingleton<ResourcePanelService>();

        services.AddSingleton<WindowFilePickerService>();
        services.AddSingleton<IFilePickerService>(sp =>
            sp.GetRequiredService<WindowFilePickerService>());
        services.AddSingleton<WindowMetricsService>();
        services.AddSingleton<IWindowMetricsService>(sp =>
            sp.GetRequiredService<WindowMetricsService>());

        // ── Services with dependencies ────────────────────────────────────
        services.AddSingleton<ManifestValidationService>();
        services.AddSingleton(sp => new ResourcePanelUidService(
            sp.GetRequiredService<BestHttpCookieLibraryService>(),
            sp.GetRequiredService<ILauncherSettingsService>(),
            sp.GetRequiredService<ISavedSettingsWriter>(),
            sp.GetRequiredService<ILauncherDiagnostics>()));
        services.AddSingleton(new PresentationOptions(showHiddenSettings));
        services.AddSingleton<SettingsEditor>();
        // 设置草稿所有者：Core 的写入协调器只认这个窄接缝，不认识 SettingsEditor 本身
        // （UI 线程编排留在编辑器里）。
        services.AddSingleton<ISettingsDraftOwner>(sp => sp.GetRequiredService<SettingsEditor>());
        services.AddSingleton<SettingsOptionsViewModel>();
        // 主题应用器登记在设置外观 VM 之前：容器按登记逆序释放，它的退订要晚于消费它的 VM。
        services.AddSingleton<ThemeApplier>();
        services.AddSingleton(sp => new SettingsAppearanceViewModel(
            sp.GetRequiredService<SettingsEditor>(),
            sp.GetRequiredService<ThemeApplier>(),
            sp.GetRequiredService<ILauncherDiagnostics>(),
            showHiddenSettings));
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

        services.AddSingleton<IErrorHandlingService, ErrorHandlingService>();

        // ── IDisposable services ─────────────────────────────────────────
        // The container disposes created services in reverse order. This keeps
        // HttpClientFactory alive until all clients and download services are gone.
        services.AddSingleton<ResourcePanelApiClient>();
        services.AddSingleton(sp => new GameDownloadService(
            sp.GetRequiredService<ILauncherApiClient>(),
            sp.GetRequiredService<RemoteManifestService>(),
            sp.GetRequiredService<IFileDownloadService>(),
            sp.GetRequiredService<ILocalInstallationStateStore>(),
            sp.GetRequiredService<ILauncherSettingsService>(),
            sp.GetRequiredService<IRemoteHttpClientLeaseSource>(),
            sp.GetRequiredService<IRemoteHttpUrlValidator>(),
            sp.GetRequiredService<ICrc64Service>(),
            sp.GetRequiredService<IDiskSpaceService>(),
            sp.GetRequiredService<ILauncherDiagnostics>(),
            sp.GetRequiredService<LocalizationService>(),
            sp.GetRequiredService<IGameInstallationPath>(),
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
            sp.GetRequiredService<ILauncherDiagnostics>(),
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
