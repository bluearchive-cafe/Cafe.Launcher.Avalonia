using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Services;

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
        services.AddSingleton<EasterEggAudioService>();

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

        // ── Services with dependencies ────────────────────────────────────
        services.AddSingleton<ProxySettingsService>();
        services.AddSingleton<ManifestValidationService>();
        services.AddSingleton<NoticeStateService>();
        services.AddSingleton<ResourcePanelUidService>();
        services.AddSingleton<LauncherSettingsService>();
        services.AddSingleton<WindowsAnimationSettingsProvider>();
        services.AddSingleton<ISettingsEditor, SettingsEditor>();
        services.AddSingleton<SettingsOptionsViewModel>();
        services.AddSingleton<SettingsAppearanceViewModel>();
        services.AddSingleton<GameLaunchService>();
        services.AddSingleton<GameUninstallService>();
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
            sp.GetRequiredService<GameInstallationPath>()));

        // ── ViewModels (all singleton — single-window desktop app) ─────────
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ResourcePanelViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<BackgroundViewModel>();
        services.AddSingleton<RemoteContentViewModel>();
        services.AddSingleton<DialogsViewModel>();
        services.AddSingleton<GameOperationsViewModel>();
        services.AddSingleton<DebugViewModel>();
        services.AddSingleton<ToastHostViewModel>();
        services.AddSingleton<WindowChromeViewModel>();
        services.AddSingleton<ModalHostViewModel>();
        services.AddSingleton<ShellCoordinator>();
        services.AddSingleton<MainWindowViewModel>();

        return services;
    }
}
