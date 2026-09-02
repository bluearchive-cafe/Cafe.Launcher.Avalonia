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
        UnifiedLogger? existingLogger = null)
    {
        // ── Leaf services (parameterless constructors, no deps) ──────────
        services.AddSingleton<GameInstallationPath>();
        services.AddSingleton<LocalInstallationStateStore>();
        services.AddSingleton<Crc64Service>();
        services.AddSingleton<DiskSpaceService>();
        services.AddSingleton<SystemCultureSnapshot>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ClickCodeService>();
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
        services.AddSingleton<LocalDiagnostics>();
        services.AddSingleton<SetupWizardViewModel>();
        services.AddSingleton<AuthorizationHeaderFactory>();
        services.AddSingleton<RemoteHttpUrlValidator>();
        services.AddSingleton<PatchUrlGroupService>();
        services.AddSingleton<RemoteManifestService>();
        services.AddSingleton<IFileDownloadService, FileDownloadService>();
        services.AddSingleton<ResourcePanelService>();

        // ── HttpClient factory (shared pool, proxy-aware) ────────────────
        services.AddSingleton<HttpClientFactory>();
        services.AddSingleton<IFilePickerService, WindowFilePickerService>();

        // ── Services with dependencies ────────────────────────────────────
        services.AddSingleton<ProxySettingsService>();
        services.AddSingleton<ManifestValidationService>();
        services.AddSingleton<NoticeStateService>();
        services.AddSingleton<ResourcePanelUidService>();
        services.AddSingleton<LauncherSettingsService>();
        services.AddSingleton<WindowsAnimationSettingsProvider>();
        services.AddSingleton<ISettingsEditor, SettingsEditor>();
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
            sp.GetRequiredService<Crc64Service>(),
            sp.GetRequiredService<DiskSpaceService>(),
            sp.GetRequiredService<LocalDiagnostics>(),
            sp.GetRequiredService<LocalizationService>(),
            sp.GetRequiredService<GameInstallationPath>(),
            sp.GetRequiredService<IGameProcessTracker>()));

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
