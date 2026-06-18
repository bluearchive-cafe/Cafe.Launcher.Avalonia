using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Services;

public static class ServiceConfiguration
{
    public static IServiceCollection AddLauncherServices(this IServiceCollection services)
    {
        // ── Leaf services (parameterless constructors, no deps) ──────────
        services.AddSingleton<LocalGameStateService>();
        services.AddSingleton<Crc64Service>();
        services.AddSingleton<DiskSpaceService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ClickCodeService>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<BestHttpCookieLibraryService>();
        services.AddSingleton<LocalDiagnostics>();
        services.AddSingleton<AuthorizationHeaderFactory>();
        services.AddSingleton<PatchUrlGroupService>();

        // ── HttpClient factory (shared pool, proxy-aware) ────────────────
        services.AddSingleton<HttpClientFactory>();

        // ── Services with dependencies ────────────────────────────────────
        services.AddSingleton<ProxySettingsService>();
        services.AddSingleton<ManifestValidationService>();
        services.AddSingleton<ExternalLinkService>();
        services.AddSingleton<NoticeStateService>();
        services.AddSingleton<DownloadStateService>();
        services.AddSingleton<ResourcePanelUidService>();
        services.AddSingleton<LauncherSettingsService>();
        services.AddSingleton<GameLaunchService>();
        services.AddSingleton<GameUninstallService>();
        services.AddSingleton<LauncherUpdateService>();
        services.AddSingleton<ILauncherCoreService, LauncherCoreService>();

        // ── IDisposable services (register in reverse-dispose order) ─────
        // Dispose order (1st → 4th): GameDownloadService, ImageCacheService,
        //   ResourcePanelApiClient, LauncherApiClient
        // Register in reverse so LauncherApiClient is disposed first:
        services.AddSingleton<LauncherApiClient>();          // IDisposable — disposes 1st
        services.AddSingleton<ResourcePanelApiClient>();      // IDisposable — disposes 2nd
        services.AddSingleton<ImageCacheService>();           // IDisposable — disposes 3rd
        services.AddSingleton<GameDownloadService>();         // IDisposable — disposes 4th (last)

        // ── Migration services ────────────────────────────────────────────
        services.AddSingleton<OldLauncherDetectionService>();

        // ── ViewModels (Transient ─ each resolution creates a fresh instance) ─
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ResourcePanelViewModel>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<BackgroundViewModel>();
        services.AddTransient<RemoteContentViewModel>();
        services.AddTransient<DialogsViewModel>();
        services.AddTransient<GameOperationsViewModel>();
        services.AddTransient<ToastHostViewModel>();
        services.AddTransient<WindowChromeViewModel>();
        services.AddTransient<MigrationWizardViewModel>();
        services.AddTransient<MainWindowViewModel>();

        return services;
    }
}
