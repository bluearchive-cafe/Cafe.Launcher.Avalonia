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
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Composition;

public static class ServiceConfiguration
{
    public static IServiceCollection AddLauncherServices(
        this IServiceCollection services,
        UnifiedLogger? existingLogger = null,
        IFatalCrashService? existingFatalCrashService = null)
    {
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
            services.AddSingleton<UnifiedLogger>();
        services.AddSingleton<LogExportService>();
        services.AddSingleton<LogViewerDialogViewModel>();
        services.AddSingleton<LogExportDialogViewModel>();
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<UnifiedLogger>();
            var localDiagnostics = new LocalDiagnostics(logger);
            LocalDiagnostics.RegisterSharedLogger(logger);
            return localDiagnostics;
        });
        services.AddSingleton<CrashReportStore>();
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
        services.AddSingleton<HttpClientFactory>();
        services.AddSingleton<IRemoteHttpTransport>(sp =>
        {
            // ISettingsEditor 是无依赖单例，在传输构造时一次解析并闭包引用；
            // 代理模式解析不再每次走服务定位。
            var settingsEditor = sp.GetRequiredService<ISettingsEditor>();
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
        services.AddSingleton<ResourcePanelUidService>();
        services.AddSingleton<LauncherSettingsService>();
        services.AddSingleton<WindowsAnimationSettingsProvider>();
        services.AddSingleton<ISettingsEditor, SettingsEditor>();
        // 已保存设置的唯一写入方：依赖编辑器与设置服务，二者都登记在它之前。
        services.AddSingleton<ISavedSettingsWriter, SavedSettingsWriter>();
        services.AddSingleton<SettingsOptionsViewModel>();
        services.AddSingleton(sp => new SettingsAppearanceViewModel(
            sp.GetRequiredService<ISettingsEditor>(),
            Program.ShowHiddenSettings));
        services.AddSingleton<IProcessLauncher, DefaultProcessLauncher>();
        services.AddSingleton<IGameRuntime>(sp => new GameRuntime(
            [GameRunnerDefinition.Native, GameRunnerDefinition.Umu, GameRunnerDefinition.Wine],
            sp.GetRequiredService<IProcessLauncher>(),
            sp.GetRequiredService<IGameProcessTracker>()));
        services.AddSingleton<IGameProcessTracker, GameProcessTracker>();
        // 持久化检查点存储全库单例：下载服务写入/清除，卸载服务清除——
        // 同一文件只允许一个所有者实例。
        services.AddSingleton(sp => DownloadCheckpointStore.CreateDefault());
        services.AddSingleton<GameLaunchService>();
        services.AddSingleton<GameUninstallService>();
        services.AddSingleton<IGameShortcutService, GameShortcutService>();
        services.AddSingleton<IGameOperationExecutor>(sp => new GameOperationExecutor(
            sp.GetRequiredService<GameLaunchService>(),
            sp.GetRequiredService<GameDownloadService>(),
            sp.GetRequiredService<GameUninstallService>()));
        services.AddSingleton<LauncherUpdateService>();
        services.AddSingleton<ILauncherCoreService, LauncherCoreService>();
        services.AddSingleton<IErrorHandlingService, ErrorHandlingService>();

        // ── IDisposable services ─────────────────────────────────────────
        // The container disposes created services in reverse order. This keeps
        // HttpClientFactory alive until all clients and download services are gone.
        services.AddSingleton<LauncherApiClient>();
        services.AddSingleton<ResourcePanelApiClient>();
        services.AddSingleton<ImageCacheService>();
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
        services.AddSingleton<IShellRuntime, ShellLifecycle>();
        services.AddSingleton<MainWindowViewModel>();

        return services;
    }
}
