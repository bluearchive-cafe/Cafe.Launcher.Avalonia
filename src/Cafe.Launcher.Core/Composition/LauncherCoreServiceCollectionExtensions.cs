using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Core.Composition;

/// <summary>
/// Registers services owned by the launcher Core boundary. The host must call
/// this before registering a presentation layer so Microsoft DI releases UI
/// objects first during shutdown.
/// </summary>
public static class LauncherCoreServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherCore(
        this IServiceCollection services,
        LauncherBuildIdentity buildIdentity,
        LauncherDataRoot? launcherDataRoot = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(buildIdentity);

        var dataRoot = launcherDataRoot ?? LauncherDataRoot.ForCurrentProcess();
        services.TryAddSingleton(buildIdentity);
        services.TryAddSingleton(dataRoot);
        // These services already have a stable, presentation-free dependency
        // closure. Keeping their registration here is significant: UI services
        // registered afterwards are disposed first by the Microsoft DI container.
        services.TryAddSingleton<Crc64Service>();
        services.TryAddSingleton<DiskSpaceService>();
        services.TryAddSingleton<LocalInstallationStateStore>();
        services.TryAddSingleton<AuthorizationHeaderFactory>();
        services.TryAddSingleton<BestHttpCookieLibraryService>();
        services.TryAddSingleton<RemoteHttpUrlValidator>();
        services.TryAddSingleton(sp => new LauncherSettingsService(
            sp.GetRequiredService<LauncherDataRoot>(),
            sp.GetService<ILauncherDiagnostics>()));
        return services;
    }
}
