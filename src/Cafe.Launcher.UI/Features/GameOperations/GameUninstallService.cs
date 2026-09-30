using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.UI.Features.GameOperations;

internal sealed class GameUninstallService
{
    /// <summary>
    /// 完成文案里最多点名几个删不掉的项目。实际只会是个位数（反作弊留下的目录项），
    /// 上限只是防止病态情况把提示撑爆；完整清单始终留在日志里。
    /// </summary>
    private const int MaxReportedLeftovers = 5;

    private readonly YostarGameProfile gameProfile;
    private readonly ILocalInstallationStateStore localInstallationStateStore;
    private readonly IGameInstallationPath installationPath;
    private readonly ILauncherDiagnostics diagnostics;
    private readonly LocalizationService localizer;
    private readonly DownloadCheckpointStore checkpointStore;
    private readonly IGameProcessTracker gameProcessTracker;
    private readonly IGameShortcutService shortcutService;

    public GameUninstallService(
        YostarGameProfile gameProfile,
        ILocalInstallationStateStore localInstallationStateStore,
        ILauncherDiagnostics diagnostics,
        LocalizationService localizer,
        IGameInstallationPath installationPath,
        DownloadCheckpointStore checkpointStore,
        IGameProcessTracker gameProcessTracker,
        IGameShortcutService shortcutService)
    {
        this.gameProfile = gameProfile;
        this.localInstallationStateStore = localInstallationStateStore;
        this.installationPath = installationPath;
        this.diagnostics = diagnostics;
        this.localizer = localizer;
        this.checkpointStore = checkpointStore;
        this.gameProcessTracker = gameProcessTracker;
        this.shortcutService = shortcutService;
    }

    /// <summary>确认框展示的安装目录与受管兼容环境大小；展示统计不指导文件删除。</summary>
    public Task<UninstallFootprint> MeasureFootprintAsync(
        LauncherStatusSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var (gameRoot, managedPrefixRoot) = ResolveCleanupTargets(
            installationPath.NormalizeGamePath(snapshot.LocalGame.GamePath ?? ""));
        return Task.Run(() => new UninstallFootprint(
            DirectorySizeProbe.Measure(gameRoot), DirectorySizeProbe.Measure(managedPrefixRoot),
            ResolveKeptPrefixPath(snapshot, gameRoot, UninstallScope.GameDirectory),
            ResolveKeptPrefixPath(snapshot, gameRoot, UninstallScope.GameDirectoryAndManagedCompatibility)), cancellationToken);
    }

    /// <summary>删除整个游戏目录，可额外清除受管兼容环境；进度覆盖清单外资源及目录项。</summary>
    public async Task<GameOperationResult> UninstallAsync(
        LauncherStatusSnapshot snapshot,
        UninstallScope scope,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (GameOperationPolicy.Decide(GameOperationPolicy.Operation.Uninstall, snapshot.RuntimeState)
            == GameOperationDecision.RejectedForCurrentState)
        {
            return GameOperationRejections.UnavailableResult(localizer);
        }

        var gamePath = installationPath.NormalizeGamePath(snapshot.LocalGame.GamePath ?? "");
        try
        {
            var (failure, state) = await ValidateLocalInstallationAsync(gamePath, cancellationToken).ConfigureAwait(false);
            if (failure is not null)
            {
                return failure;
            }

            // 所有目标先过守卫：额外清理目标被拒绝时，游戏目录也不能先删一半。
            _ = GamePathValidator.GetSafePath(gamePath, ".");
            var targets = ResolveDeletionTargets(gamePath, scope);
            foreach (var target in targets)
            {
                DirectoryTreeDeleter.EnsureDeletable(target.Path, target.AllowedRoot);
            }

            progress(GameOperationProgressFactory.CreateProgress(
                GameOperationKind.Uninstall, GameOperationStage.UninstallScanning, 0));
            var plan = await Task.Run(
                () => DirectoryTreeDeleter.CreatePlan(targets, cancellationToken), cancellationToken).ConfigureAwait(false);

            // 扫描也可能耗时数秒；真正删除之前再检查整族进程，避免使用确认或扫描前的过期答复。
            var gameRunning = await FindRunningGameFailureAsync(state!.GameConfig, gamePath, cancellationToken)
                .ConfigureAwait(false);
            if (gameRunning is not null)
            {
                return gameRunning;
            }

            var gate = new PercentProgressGate();
            void ReportDeletion(DirectoryDeletionProgress update)
            {
                // 95% 以前是目录树处理；最后 5% 留给续传标记与快捷方式，终态前不显示 100%。
                var percent = update.TotalEntries == 0 ? 95 : (int)(update.ProcessedEntries * 95L / update.TotalEntries);
                if (gate.ShouldDeliver(percent))
                {
                    progress(new GameOperationProgress
                    {
                        OperationKind = GameOperationKind.Uninstall,
                        Stage = GameOperationStage.Uninstalling,
                        Progress = percent,
                        ProcessedEntryCount = update.ProcessedEntries,
                        TotalEntryCount = update.TotalEntries,
                        IsRunning = true
                    });
                }
            }

            // 扫描与逐项删除均在线程池执行，不占用 UI 线程；删除时复查属性，不跟随链接。
            var removal = await Task.Run(() => plan.Delete(ReportDeletion, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            progress(GameOperationProgressFactory.CreateProgress(
                GameOperationKind.Uninstall, GameOperationStage.UninstallCleanup, 95));
            try
            {
                checkpointStore.Clear();
            }
            catch (Exception exception) when (StorageFailure.IsRecoverable(exception))
            {
                await diagnostics.WarningAsync("GameUninstall",
                    $"Failed to clear the download resume marker: {exception.Message}", CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await DeleteDesktopShortcutAsync(snapshot).ConfigureAwait(false);
            var keptPrefixPath = ResolveKeptPrefixPath(snapshot, gamePath, scope);
            if (removal.Leftovers.Count > 0)
            {
                await diagnostics.WarningAsync("GameUninstall",
                    $"Uninstall left {removal.Leftovers.Count} item(s) behind:"
                    + Environment.NewLine + string.Join(Environment.NewLine, removal.Leftovers), CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await diagnostics.MessageAsync("GameUninstall",
                $"Game uninstall completed.{Environment.NewLine}path: {gamePath}{Environment.NewLine}scope: {scope}"
                + $"{Environment.NewLine}entries processed: {plan.TotalEntries}"
                + $"{Environment.NewLine}files removed: {removal.RemovedFiles} ({removal.RemovedBytes} bytes)"
                + $"{Environment.NewLine}leftovers: {removal.Leftovers.Count}", CancellationToken.None).ConfigureAwait(false);
            progress(new GameOperationProgress
            {
                OperationKind = GameOperationKind.Uninstall,
                Stage = GameOperationStage.UninstallCleanup,
                Progress = 100,
                ProcessedEntryCount = plan.TotalEntries,
                TotalEntryCount = plan.TotalEntries,
                IsRunning = true
            });
            return new GameOperationResult
            {
                Success = true,
                Message = BuildCompletionMessage(localizer, removal.Leftovers, keptPrefixPath),
                AffectedFileCount = removal.RemovedFiles,
                AffectedBytes = removal.RemovedBytes,
                UninstallDetails = new UninstallResultDetails(removal.Leftovers.ToArray(), keptPrefixPath)
            };
        }
        catch (InvalidOperationException exception)
        {
            await diagnostics.ErrorAsync("GameUninstall", "Cleanup was refused by the path guards.",
                exception, CancellationToken.None).ConfigureAwait(false);
            return GameOperationOutcomes.Failed(
                localizer.F(LocalizationKeys.UninstallRefusedByPathGuard, gamePath), GameOperationErrorCode.System);
        }
        catch (Exception exception) when (StorageFailure.IsRecoverable(exception))
        {
            await diagnostics.ErrorAsync("GameUninstall", "Uninstalling the game failed.",
                exception, CancellationToken.None).ConfigureAwait(false);
            return GameOperationOutcomes.Failed(
                localizer.F(LocalizationKeys.UninstallFailed, exception.Message), GameOperationErrorCode.System);
        }
    }

    private (string GameRoot, string ManagedPrefixRoot) ResolveCleanupTargets(string gamePath) =>
        (gamePath, GameCompatibilityPaths.GetDefaultGameCompatibilityRoot(gameProfile.RuntimeId));

    private IReadOnlyList<DirectoryDeletionTarget> ResolveDeletionTargets(string gamePath, UninstallScope scope)
    {
        var (gameRoot, managedPrefixRoot) = ResolveCleanupTargets(gamePath);
        return scope switch
        {
            UninstallScope.GameDirectory => [new DirectoryDeletionTarget(gameRoot, gameRoot)],
            UninstallScope.GameDirectoryAndManagedCompatibility =>
            [
                new DirectoryDeletionTarget(gameRoot, gameRoot),
                new DirectoryDeletionTarget(managedPrefixRoot, GameCompatibilityPaths.GetDefaultCompatibilityRoot())
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
        };
    }

    private string? ResolveKeptPrefixPath(LauncherStatusSnapshot snapshot, string gamePath, UninstallScope scope)
    {
        var prefixPath = GameRuntimeConfiguration.FromSettings(snapshot.Settings.GameRuntime).PrefixPath;
        if (string.IsNullOrWhiteSpace(prefixPath) || DirectoryTreeDeleter.IsUnder(prefixPath, gamePath))
        {
            return null;
        }

        var (_, managedPrefixRoot) = ResolveCleanupTargets(gamePath);
        return scope == UninstallScope.GameDirectoryAndManagedCompatibility
            && DirectoryTreeDeleter.IsUnder(prefixPath, managedPrefixRoot) ? null : prefixPath;
    }

    /// <summary>完成结果分别说明无法删除的项目与主动保留的自定义 Prefix；全部路径进日志。</summary>
    internal static string BuildCompletionMessage(
        LocalizationService localizer, IReadOnlyList<string> leftovers, string? keptPrefixPath)
    {
        if (leftovers.Count > 0)
        {
            var listed = string.Join(Environment.NewLine, leftovers.Take(MaxReportedLeftovers));
            return keptPrefixPath is null
                ? localizer.F(LocalizationKeys.UninstallCompletedWithLeftovers, listed)
                : localizer.F(LocalizationKeys.UninstallCompletedWithLeftoversKeptPrefix, listed, keptPrefixPath);
        }

        return keptPrefixPath is null
            ? localizer.T(LocalizationKeys.UninstallCompleted)
            : localizer.F(LocalizationKeys.UninstallCompletedKeptPrefix, keptPrefixPath);
    }

    private async Task DeleteDesktopShortcutAsync(LauncherStatusSnapshot snapshot)
    {
        try
        {
            var result = await shortcutService.DeleteDesktopShortcutAsync(snapshot).ConfigureAwait(false);
            if (result.Status is GameShortcutStatus.Deleted or GameShortcutStatus.NotFound)
            {
                await diagnostics.MessageAsync(
                    "GameUninstall",
                    $"Desktop shortcut removal: {result.Status} ({result.Detail})",
                    CancellationToken.None).ConfigureAwait(false);
                return;
            }

            await diagnostics.WarningAsync(
                "GameUninstall",
                $"Desktop shortcut removal did not complete: {result.Status} ({result.Detail})").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await diagnostics.WarningAsync(
                "GameUninstall",
                $"Desktop shortcut removal failed: {exception.Message}").ConfigureAwait(false);
        }
    }

    /// <summary>卸载的本地预检；损坏的元数据不会指导删除，整目录范围由确认框明确告知。</summary>
    public async Task<GameOperationResult> ValidateAsync(string gamePath, CancellationToken cancellationToken = default)
    {
        var (failure, state) = await ValidateLocalInstallationAsync(gamePath, cancellationToken).ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        var gameRunning = await FindRunningGameFailureAsync(state!.GameConfig, gamePath, cancellationToken)
            .ConfigureAwait(false);
        return gameRunning ?? new GameOperationResult { Success = true };
    }

    private async Task<(GameOperationResult? Failure, LocalInstallationState? LocalGame)> ValidateLocalInstallationAsync(
        string gamePath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(gamePath))
        {
            return (GameOperationOutcomes.Failed(localizer.F(LocalizationKeys.GamePathMissing, gamePath), GameOperationErrorCode.Uninstall), null);
        }

        if (IsSystemProtectPath(gamePath))
        {
            return (GameOperationOutcomes.Failed(localizer.F(LocalizationKeys.GamePathProtected, gamePath), GameOperationErrorCode.Uninstall), null);
        }

        try
        {
            GamePathValidator.EnsureGameDirectoryName(gamePath, gameProfile.GameFolderName);
        }
        catch (InvalidOperationException)
        {
            return (GameOperationOutcomes.Failed(localizer.F(LocalizationKeys.GameDirectoryNameInvalid, gameProfile.GameFolderName), GameOperationErrorCode.Uninstall), null);
        }

        var localGame = await localInstallationStateStore.ReadAsync(gamePath, cancellationToken).ConfigureAwait(false);
        if (localGame.Kind == LocalInstallationStateKind.IoFailure)
        {
            return (GameOperationOutcomes.Failed(
                localizer.F(LocalizationKeys.UninstallFailed, localGame.Error),
                GameOperationErrorCode.System), null);
        }

        if (localGame.Kind == LocalInstallationStateKind.Corrupted)
        {
            return (null, localGame);
        }

        if (localGame.Kind != LocalInstallationStateKind.Valid)
        {
            return (GameOperationOutcomes.Failed(localizer.F(LocalizationKeys.GameConfigMetadataMissing, LauncherPaths.GameConfigFileName), GameOperationErrorCode.Uninstall), null);
        }

        if (string.IsNullOrWhiteSpace(localGame.GameConfig?.Version) || string.IsNullOrWhiteSpace(localGame.GameConfig?.Name))
        {
            return (GameOperationOutcomes.Failed(localizer.F(LocalizationKeys.GameConfigMetadataMissing, LauncherPaths.GameConfigFileName), GameOperationErrorCode.Uninstall), null);
        }

        return (null, localGame);
    }

    /// <summary>
    /// 「游戏是不是在跑」这道闸门（ADR-032）在本类的入口：预检与删除前的复查共用它，正文在
    /// <see cref="RunningGameGate"/>，与下载／安装／修复侧的判据、报法不会分叉。返回 null 表示
    /// 放行；本地配置不可用时从游戏档案取得已知家族，不依赖联网也不跳过检查。
    /// </summary>
    private async Task<GameOperationResult?> FindRunningGameFailureAsync(
        GameLauncherConfig? gameConfig,
        string installDirectory,
        CancellationToken cancellationToken)
    {
        // 元数据损坏时也要能离线检查整族进程；可信的游戏档案提供已知宿主与客户端名，
        // 不从损坏的 JSON 猜名字，更不能因取不到远端配置而跳过运行检查。
        var launchConfig = string.IsNullOrWhiteSpace(gameConfig?.Name)
            ? new GameLauncherConfig
            {
                Name = gameProfile.GameLauncherExecutableFileName,
                Params = [gameProfile.GameExecutableFileName]
            }
            : gameConfig;
        var query = RunningGameGate.ResolveQuery(launchConfig, null, installDirectory);
        return await RunningGameGate.FindFailureAsync(
            gameProcessTracker,
            localizer,
            LocalizationKeys.GameIsRunning,
            query,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSystemProtectPath(string path)
    {
        // Port of v1.7.2 isSystemProtectPath (out/main/index.js:612-658).
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // v1.7.2: statSync fails → path does not exist → protect (index.js:645-648).
        if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
        {
            return true;
        }

        // v1.7.2: drive root regex /^[a-zA-Z]:\\$/ → protect (index.js:651-653).
        var root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var protectedPaths = new[]
        {
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.GetTempPath(),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("SystemDrive") ?? "",
            Environment.GetEnvironmentVariable("SystemRoot") ?? "",
        };

        // v1.7.2: app.getPath("home") parent dir (line 627).
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return protectedPaths
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => Path.GetFullPath(item).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Any(item => string.Equals(fullPath, item, StringComparison.OrdinalIgnoreCase))
            || (userProfile.Length > 0
                && string.Equals(
                    fullPath,
                    Path.GetFullPath(Path.GetDirectoryName(userProfile)!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase));
    }
}
