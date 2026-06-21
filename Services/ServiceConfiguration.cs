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
        services.AddSingleton<GameInstallationPath>();
        services.AddSingleton<LocalInstallationStateStore>();
        services.AddSingleton<Crc64Service>();
        services.AddSingleton<DiskSpaceService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ClickCodeService>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<BestHttpCookieLibraryService>();
        services.AddSingleton<LocalDiagnostics>();
        services.AddSingleton<AuthorizationHeaderFactory>();
        services.AddSingleton<PatchUrlGroupService>();
        services.AddSingleton<SettingsNormalizer>();
        services.AddSingleton<RemoteManifestService>();
        services.AddSingleton<ResourcePanelService>();

        // ── HttpClient factory (shared pool, proxy-aware) ────────────────
        services.AddSingleton<HttpClientFactory>();

        // ── Services with dependencies ────────────────────────────────────
        services.AddSingleton<ProxySettingsService>();
        services.AddSingleton<ManifestValidationService>();
        services.AddSingleton<NoticeStateService>();
        services.AddSingleton<ResourcePanelUidService>();
        services.AddSingleton<LauncherSettingsService>();
        services.AddSingleton<ISettingsEditor, SettingsEditor>();
        services.AddSingleton<SettingsOptionsViewModel>();
        services.AddSingleton<SettingsAppearanceViewModel>();
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

        // ── ViewModels (transient unless explicitly registered otherwise) ─
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<ResourcePanelViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<BackgroundViewModel>();
        services.AddSingleton<RemoteContentViewModel>();
        services.AddSingleton<DialogsViewModel>();
        services.AddSingleton<GameOperationsViewModel>();
        services.AddTransient<ToastHostViewModel>();
        services.AddTransient<WindowChromeViewModel>();
        services.AddTransient<MigrationWizardViewModel>();
        services.AddTransient<MainWindowViewModel>();

        return services;
    }
}
