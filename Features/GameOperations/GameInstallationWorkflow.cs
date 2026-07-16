using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>Adapts the download service to the installation presentation workflow.</summary>
internal sealed class GameInstallationWorkflow(GameDownloadService service) : IGameInstallationWorkflow
{
    /// <inheritdoc />
    public bool IsRunning => service.IsRunning;
    /// <inheritdoc />
    public bool IsPaused => service.IsPaused;
    /// <inheritdoc />
    public Task<GameOperationResult> InstallOrUpdateAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress) => service.InstallOrUpdateAsync(snapshot, progress);
    /// <inheritdoc />
    public Task<GameOperationResult> RepairAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress) => service.RepairAsync(snapshot, progress);
    /// <inheritdoc />
    public Task<GameOperationResult?> ResumePersistedAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken) =>
        service.ResumePersistedAsync(snapshot, progress, cancellationToken);
    /// <inheritdoc />
    public void Stop(bool clearPersistedState) => service.Stop(clearPersistedState);
    /// <inheritdoc />
    public void Pause() => service.Pause();
    /// <inheritdoc />
    public void Resume() => service.Resume();
}
