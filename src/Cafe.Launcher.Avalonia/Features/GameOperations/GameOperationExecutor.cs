using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Production adapter for <see cref="IGameOperationExecutor"/>: composes the
/// three operation pipelines (launch, download, uninstall) behind one seam so
/// the journey and its tests cross a single interface.
/// </summary>
internal sealed class GameOperationExecutor(
    GameLaunchService launchService,
    GameDownloadService downloadService,
    GameUninstallService uninstallService) : IGameOperationExecutor
{
    public bool IsDownloadRunning => downloadService.IsRunning;

    public bool IsPaused => downloadService.IsPaused;

    public event Action? IsRunningChanged
    {
        add => downloadService.IsRunningChanged += value;
        remove => downloadService.IsRunningChanged -= value;
    }

    public Task<GameLaunchResult> LaunchAsync(
        LauncherStatusSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        launchService.StartAsync(snapshot, cancellationToken);

    public Task<GameOperationResult> InstallOrUpdateAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default) =>
        downloadService.InstallOrUpdateAsync(snapshot, progress, cancellationToken);

    public Task<GameOperationResult> RepairAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress) =>
        downloadService.RepairAsync(snapshot, progress);

    public Task<GameOperationResult?> ResumePersistedAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken) =>
        downloadService.ResumePersistedAsync(snapshot, progress, cancellationToken);

    public Task<GameOperationResult> ValidateUninstallAsync(string gamePath) =>
        uninstallService.ValidateAsync(gamePath);

    public Task<GameOperationResult> UninstallAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress) =>
        uninstallService.UninstallAsync(snapshot, progress);

    public void Stop(bool clearPersistedState) => downloadService.Stop(clearPersistedState);

    public void Pause() => downloadService.Pause();

    public void Resume() => downloadService.Resume();
}
