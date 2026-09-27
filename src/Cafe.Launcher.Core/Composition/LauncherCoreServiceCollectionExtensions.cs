using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Auth;
using Cafe.Launcher.Core.Services.Diagnostics;

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
        services.TryAddSingleton<Crc64Service>();
        services.TryAddSingleton<DiskSpaceService>();
        services.TryAddSingleton<LocalInstallationStateStore>();
        services.TryAddSingleton<AuthorizationHeaderFactory>();
        services.TryAddSingleton<BestHttpCookieLibraryService>();
        services.TryAddSingleton<PatchUrlGroupService>();
        services.TryAddSingleton<RemoteHttpUrlValidator>();
        services.TryAddSingleton(sp => new LauncherSettingsService(
            sp.GetRequiredService<LauncherDataRoot>(),
            sp.GetService<ILauncherDiagnostics>(),
            buildIdentity: buildIdentity));
        // 已保存设置的唯一写入方。草稿所有者由表现层登记（ISettingsDraftOwner）——写入方不认识
        // 具体编辑器，UI 线程编排留在实现方。
        services.TryAddSingleton<ISavedSettingsWriter, SavedSettingsWriter>();
        return services;
    }
}
