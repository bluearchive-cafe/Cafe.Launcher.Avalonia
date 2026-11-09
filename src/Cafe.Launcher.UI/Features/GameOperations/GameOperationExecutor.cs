using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Features.GameOperations;

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
        GameOperationAttempt attempt,
        CancellationToken cancellationToken = default) =>
        launchService.StartAsync(attempt, cancellationToken);

    public Task<GameOperationResult> InstallOrUpdateAsync(
        GameOperationAttempt attempt,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default) =>
        downloadService.InstallOrUpdateAsync(attempt, progress, cancellationToken);

    public Task<GameOperationResult> RepairAsync(
        GameOperationAttempt attempt,
        Action<GameOperationProgress> progress) =>
        downloadService.RepairAsync(attempt, progress);

    public Task<GameOperationResult?> ResumePersistedAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken) =>
        downloadService.ResumePersistedAsync(snapshot, progress, cancellationToken);

    public Task<GameOperationResult> ValidateUninstallAsync(string gamePath) =>
        uninstallService.ValidateAsync(gamePath);

    public Task<UninstallFootprint> MeasureUninstallFootprintAsync(LauncherStatusSnapshot snapshot) =>
        uninstallService.MeasureFootprintAsync(snapshot);

    public Task<GameOperationResult> UninstallAsync(
        GameOperationAttempt attempt,
        UninstallScope scope,
        Action<GameOperationProgress> progress) =>
        uninstallService.UninstallAsync(attempt, scope, progress);

    public void Stop(DownloadStopReason reason) => downloadService.Stop(reason);

    public void Pause() => downloadService.Pause();

    public void Resume() => downloadService.Resume();
}
