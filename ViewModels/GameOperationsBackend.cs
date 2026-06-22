using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.ViewModels;

internal interface IGameOperationsBackend
{
    bool IsDownloadRunning { get; }
    bool IsPaused { get; }
    Task<GameLaunchResult> StartGameAsync(LauncherStatusSnapshot snapshot);
    Task<GameOperationResult> InstallOrUpdateAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress);
    Task<GameOperationResult> RepairAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress);
    Task<GameOperationResult> ValidateUninstallAsync(string gamePath);
    Task<GameOperationResult> UninstallAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress);
    Task<GameOperationResult?> ResumePersistedAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken);
    void Stop(bool clearPersistedState);
    void Pause();
    void Resume();
}

internal sealed class GameOperationsBackend(
    GameLaunchService gameLaunchService,
    GameDownloadService gameDownloadService,
    GameUninstallService gameUninstallService) : IGameOperationsBackend
{
    public bool IsDownloadRunning => gameDownloadService.IsRunning;

    public bool IsPaused => gameDownloadService.IsPaused;

    public Task<GameLaunchResult> StartGameAsync(LauncherStatusSnapshot snapshot) =>
        gameLaunchService.StartAsync(snapshot);

    public Task<GameOperationResult> InstallOrUpdateAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress) =>
        gameDownloadService.InstallOrUpdateAsync(snapshot, progress);

    public Task<GameOperationResult> RepairAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress) =>
        gameDownloadService.RepairAsync(snapshot, progress);

    public Task<GameOperationResult> ValidateUninstallAsync(string gamePath) =>
        gameUninstallService.ValidateAsync(gamePath);

    public Task<GameOperationResult> UninstallAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress) =>
        gameUninstallService.UninstallAsync(snapshot, progress);

    public Task<GameOperationResult?> ResumePersistedAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken) =>
        gameDownloadService.ResumePersistedAsync(snapshot, progress, cancellationToken);

    public void Stop(bool clearPersistedState) =>
        gameDownloadService.Stop(clearPersistedState);

    public void Pause() => gameDownloadService.Pause();

    public void Resume() => gameDownloadService.Resume();
}
