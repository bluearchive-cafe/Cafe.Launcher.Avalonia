using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

public interface ILauncherCoreService
{
    Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class LauncherCoreService : ILauncherCoreService
{
    private readonly LauncherApiClient apiClient;
    private readonly LocalInstallationStateStore localInstallationStateStore;
    private readonly GameInstallationPath installationPath;
    private readonly LauncherSettingsService settingsService;
    private readonly LocalDiagnostics diagnostics;

    public LauncherCoreService(
        LauncherApiClient apiClient,
        LocalInstallationStateStore localInstallationStateStore,
        GameInstallationPath installationPath,
        LauncherSettingsService settingsService,
        LocalDiagnostics diagnostics)
    {
        this.apiClient = apiClient;
        this.localInstallationStateStore = localInstallationStateStore;
        this.installationPath = installationPath;
        this.settingsService = settingsService;
        this.diagnostics = diagnostics;
    }

    public async Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.ReadAsync(cancellationToken).ConfigureAwait(false);
        await diagnostics.DebugAsync("LauncherCore", "LoadAsync started", CancellationToken.None).ConfigureAwait(false);
        var gameConfigTask = ReadRemoteAsync(
            "game-config",
            () => apiClient.GetGameConfigAsync(settings.ProxyMode, cancellationToken),
            cancellationToken);
        var baseConfigTask = ReadRemoteAsync(
            "base-config",
            () => apiClient.GetBaseConfigAsync(settings.ProxyMode, cancellationToken),
            cancellationToken);
        var cdnConfigTask = ReadRemoteAsync(
            "cdn-config",
            () => apiClient.GetCdnConfigAsync(
                settings.PatchUrlGroup,
                settings.ProxyMode,
                cancellationToken),
            cancellationToken);
        var operationsResourceTask = ReadRemoteAsync(
            "operations-resource",
            () => apiClient.GetOperationsResourceAsync(settings.ProxyMode, cancellationToken),
            cancellationToken);
        var socialMediaResourceTask = ReadRemoteAsync(
            "social-media-resource",
            () => apiClient.GetSocialMediaResourceAsync(settings.ProxyMode, cancellationToken),
            cancellationToken);
        var installationConfigTask = ReadRemoteAsync(
            "installation-config",
            () => apiClient.GetInstallationConfigAsync(settings.ProxyMode, cancellationToken),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.GamePath))
        {
            settings.GamePath = installationPath.GetDefaultGamePath();
        }

        var localGameTask = localInstallationStateStore.ReadAsync(
            installationPath.NormalizeGamePath(settings.GamePath),
            cancellationToken);

        await Task.WhenAll(
            gameConfigTask,
            baseConfigTask,
            cdnConfigTask,
            operationsResourceTask,
            socialMediaResourceTask,
            installationConfigTask,
            localGameTask).ConfigureAwait(false);

        var localGame = await localGameTask.ConfigureAwait(false);
        var gameConfig = await gameConfigTask.ConfigureAwait(false);
        var baseConfig = await baseConfigTask.ConfigureAwait(false);
        var cdnConfig = await cdnConfigTask.ConfigureAwait(false);
        var operationsResource = await operationsResourceTask.ConfigureAwait(false);
        var socialMediaResource = await socialMediaResourceTask.ConfigureAwait(false);
        var installationConfig = await installationConfigTask.ConfigureAwait(false);
        var runtimeState = ResolveRuntimeState(localGame, gameConfig);
        await diagnostics.DebugAsync(
            "LauncherCore",
            $"API outcomes: gameConfig={gameConfig is not null}, baseConfig={baseConfig is not null}, cdnConfig={cdnConfig is not null}, operations={operationsResource is not null}, socialMedia={socialMediaResource is not null}, installation={installationConfig is not null}", CancellationToken.None).ConfigureAwait(false);
        await diagnostics.DebugAsync("LauncherCore", $"RuntimeState resolved: {runtimeState}", CancellationToken.None).ConfigureAwait(false);

        return new LauncherStatusSnapshot
        {
            Settings = settings,
            LocalGame = localGame,
            Remote = new LauncherRemoteState
            {
                GameConfig = gameConfig,
                BaseConfig = baseConfig,
                CdnConfig = cdnConfig,
                OperationsResource = operationsResource,
                SocialMediaResource = socialMediaResource,
                InstallationConfig = installationConfig
            },
            RuntimeState = runtimeState,
            CheckedAt = System.DateTimeOffset.Now
        };
    }

    internal static LauncherRuntimeState ResolveRuntimeState(
        LocalInstallationState localGame,
        GameConfigResponse? gameConfig)
    {
        if (localGame.Kind != LocalInstallationStateKind.Valid)
        {
            return localGame.Kind switch
            {
                LocalInstallationStateKind.NotInstalled => LauncherRuntimeState.NotInstalled,
                LocalInstallationStateKind.Corrupted => LauncherRuntimeState.Corrupted,
                LocalInstallationStateKind.IoFailure => LauncherRuntimeState.IoFailure,
                _ => LauncherRuntimeState.IoFailure
            };
        }

        if (gameConfig is null
            || string.IsNullOrWhiteSpace(gameConfig.GameLatestVersion)
            || string.IsNullOrWhiteSpace(gameConfig.GameLowestVersion))
        {
            return LauncherRuntimeState.RemoteUnavailable;
        }

        var localVersion = localGame.GameConfig?.Version;
        if (string.IsNullOrWhiteSpace(localVersion))
        {
            return LauncherRuntimeState.Corrupted;
        }

        if (VersionComparer.Compare(localVersion, gameConfig.GameLowestVersion) == -1)
        {
            return LauncherRuntimeState.BelowLowestVersion;
        }

        return VersionComparer.Compare(localVersion, gameConfig.GameLatestVersion) == -1
            ? LauncherRuntimeState.UpdateAvailable
            : LauncherRuntimeState.Ready;
    }

    private async Task<T?> ReadRemoteAsync<T>(
        string operation,
        System.Func<Task<T>> read,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await diagnostics.ErrorAsync(
                $"Launcher remote state read failed: {operation}.",
                exception,
                CancellationToken.None).ConfigureAwait(false);
            return default;
        }
    }
}
