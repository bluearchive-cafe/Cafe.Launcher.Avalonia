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
        services.AddTransient<ISettingsEditor, SettingsEditor>();
        services.AddTransient<SettingsOptionsViewModel>();
        services.AddTransient<SettingsAppearanceViewModel>();
        services.AddSingleton<GameLaunchService>();
        services.AddSingleton<GameUninstallService>();
        services.AddSingleton<LauncherUpdateService>();
        services.AddSingleton<ILauncherCoreService, LauncherCoreService>();

        // ── IDisposable services ─────────────────────────────────────────
        // The container disposes created services in reverse order. This keeps
        // HttpClientFactory alive until all clients and download services are gone.
        services.AddSingleton<LauncherApiClient>();
        services.AddSingleton<ResourcePanelApiClient>();
        services.AddSingleton<ImageCacheService>();
        services.AddSingleton<GameDownloadService>();

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
