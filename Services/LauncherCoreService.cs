using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public interface ILauncherCoreService
{
    Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class LauncherCoreService : ILauncherCoreService
{
    private readonly LauncherApiClient apiClient;
    private readonly LocalGameStateService localGameStateService;
    private readonly LauncherSettingsService settingsService;

    public LauncherCoreService(
        LauncherApiClient apiClient,
        LocalGameStateService localGameStateService,
        LauncherSettingsService settingsService)
    {
        this.apiClient = apiClient;
        this.localGameStateService = localGameStateService;
        this.settingsService = settingsService;
    }

    public async Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.ReadAsync(cancellationToken);
        var gameConfigTask = apiClient.GetGameConfigAsync(cancellationToken);
        var baseConfigTask = apiClient.GetBaseConfigAsync(cancellationToken);
        var cdnConfigTask = apiClient.GetCdnConfigAsync(settings.PatchUrlGroup, cancellationToken);
        var operationsResourceTask = ReadOptionalAsync(
            () => apiClient.GetOperationsResourceAsync(cancellationToken));
        var socialMediaResourceTask = ReadOptionalAsync(
            () => apiClient.GetSocialMediaResourceAsync(cancellationToken));
        var installationConfigTask = ReadOptionalAsync(
            () => apiClient.GetInstallationConfigAsync(cancellationToken));
        var localGameTask = localGameStateService.ReadAsync(settings.GamePath, cancellationToken);

        await Task.WhenAll(gameConfigTask, baseConfigTask, cdnConfigTask, localGameTask);
        await Task.WhenAll(operationsResourceTask, socialMediaResourceTask, installationConfigTask);

        var localGame = await localGameTask;
        var gameConfig = await gameConfigTask;
        var localVersion = localGame.GameConfig?.Version;
        var isInstalled = !string.IsNullOrWhiteSpace(localVersion);
        var needsUpdate = isInstalled && VersionComparer.Compare(localVersion, gameConfig.GameLatestVersion) == -1;
        var belowLowestVersion = isInstalled && VersionComparer.Compare(localVersion, gameConfig.GameLowestVersion) == -1;

        return new LauncherStatusSnapshot
        {
            Settings = settings,
            LocalGame = localGame,
            Remote = new LauncherRemoteState
            {
                GameConfig = gameConfig,
                BaseConfig = await baseConfigTask,
                CdnConfig = await cdnConfigTask,
                OperationsResource = await operationsResourceTask,
                SocialMediaResource = await socialMediaResourceTask,
                InstallationConfig = await installationConfigTask
            },
            IsInstalled = isInstalled,
            NeedsUpdate = needsUpdate,
            BelowLowestVersion = belowLowestVersion,
            UserStatus = ResolveUserStatus(isInstalled, needsUpdate, belowLowestVersion),
            CheckedAt = System.DateTimeOffset.Now
        };
    }

    private static string ResolveUserStatus(bool isInstalled, bool needsUpdate, bool belowLowestVersion)
    {
        if (!isInstalled)
        {
            return "Game is not installed.";
        }

        if (belowLowestVersion)
        {
            return "Game version is below the required lowest version.";
        }

        if (needsUpdate)
        {
            return "Game update is available.";
        }

        return "Game is ready.";
    }

    private static async Task<T?> ReadOptionalAsync<T>(System.Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return default;
        }
    }
}
