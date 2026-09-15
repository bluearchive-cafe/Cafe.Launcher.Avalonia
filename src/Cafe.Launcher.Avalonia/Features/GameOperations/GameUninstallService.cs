using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

public sealed class GameUninstallService
{
    /// <summary>
    /// 完成文案里最多点名几个删不掉的项目。实际只会是个位数（反作弊留下的目录项），
    /// 上限只是防止病态情况把提示撑爆；完整清单始终留在日志里。
    /// </summary>
    private const int MaxReportedLeftovers = 5;

    private readonly LocalInstallationStateStore localInstallationStateStore;
    private readonly GameInstallationPath installationPath;
    private readonly LocalDiagnostics diagnostics;
    private readonly LocalizationService localizer;
    private readonly DownloadCheckpointStore checkpointStore;
    private readonly IGameProcessTracker gameProcessTracker;
    private readonly IGameShortcutService shortcutService;

    public GameUninstallService(
        LocalInstallationStateStore localInstallationStateStore,
        LocalDiagnostics diagnostics,
        LocalizationService localizer,
        GameInstallationPath installationPath,
        DownloadCheckpointStore checkpointStore,
        IGameProcessTracker gameProcessTracker,
        IGameShortcutService shortcutService)
    {
        this.localInstallationStateStore = localInstallationStateStore;
        this.installationPath = installationPath;
        this.diagnostics = diagnostics;
        this.localizer = localizer;
        this.checkpointStore = checkpointStore;
        this.gameProcessTracker = gameProcessTracker;
        this.shortcutService = shortcutService;
    }

    /// <summary>
    /// 彻底清除会删除的两个目录的实测大小（ADR-030）。展示用；与删除共用同一段目标计算，
    /// 所以对话框里显示多少就是随后会删多少。
    /// </summary>
    public Task<UninstallFootprint> MeasureFootprintAsync(
        LauncherStatusSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var gamePath = installationPath.NormalizeGamePath(snapshot.LocalGame.GamePath ?? "");
        var (gameRoot, managedPrefixRoot) = ResolveCleanupTargets(gamePath);
        return Task.Run(
            () => new UninstallFootprint(
                DirectorySizeProbe.Measure(gameRoot),
                DirectorySizeProbe.Measure(managedPrefixRoot)),
            cancellationToken);
    }

    public async Task<GameOperationResult> UninstallAsync(
        LauncherStatusSnapshot snapshot,
        UninstallScope scope,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (GameOperationPolicy.Decide(GameOperationPolicy.Operation.Uninstall, snapshot.RuntimeState)
            == GameOperationDecision.RejectedForCurrentState)
        {
            return DownloadSession.Failed(localizer.T(LocalizationKeys.OperationUnavailableForCurrentState), GameOperationErrorCode.InvalidState);
        }

        var gamePath = installationPath.NormalizeGamePath(snapshot.LocalGame.GamePath ?? "");
        try
        {
            var validation = await ValidateAsync(gamePath, cancellationToken).ConfigureAwait(false);
            if (!validation.Success)
            {
                return validation;
            }

            var localGame = await localInstallationStateStore.ReadAsync(gamePath, cancellationToken).ConfigureAwait(false);
            var files = localGame.Manifest?.Files ?? [];

            // 预检答的是「点卸载那一刻」的进程状态，而确认框可以一直开着（尺寸统计、用户离开），
            // 这期间从桌面快捷方式或 Steam 把游戏起来，预检的答复就已经过期。删除之前复查同一道
            // 闸门（ADR-032 的「只在整族退出后放行」），命中即按既有消息报出——与路径守卫的执行
            // 边界复查（EnsureCleanupTargetsAreDeletable）同构，且失败经 ConfirmUninstallAsync 的
            // ShowOperationResult 落地，不是静默（ADR-027）。
            var gameRunning = await FindRunningGameFailureAsync(localGame.GameConfig, cancellationToken)
                .ConfigureAwait(false);
            if (gameRunning is not null)
            {
                return gameRunning;
            }

            // 彻底清除的守卫先行（ADR-030）：拒绝就什么都不删，别留下半删状态。
            if (scope == UninstallScope.ThoroughCleanup)
            {
                try
                {
                    EnsureCleanupTargetsAreDeletable(gamePath);
                }
                catch (InvalidOperationException exception)
                {
                    await diagnostics.ErrorAsync(
                        "GameUninstall",
                        "Thorough cleanup was refused by the path guards.",
                        exception,
                        CancellationToken.None).ConfigureAwait(false);
                    // 报本地化的拒绝理由，守卫那句英文说明只留在日志里（2026-09-15 复核轮）：
                    // GamePathValidator/DirectoryTreeDeleter 抛的是仓库自己写的英文，
                    // 套进「卸载失败：{0}」就会让本地化界面显示英文开发文档。
                    return DownloadSession.Failed(
                        localizer.F(LocalizationKeys.UninstallRefusedByPathGuard, gamePath),
                        GameOperationErrorCode.System);
                }
            }
            // AUD-PERF-007：逐文件回调经百分比门控去重后抵达 UI 线程。
            var progressGate = new PercentProgressGate();
            for (var i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = GamePathValidator.GetSafePath(gamePath, files[i].Path);
                try
                {
                    File.Delete(filePath);
                }
                catch (FileNotFoundException)
                {
                    // Already gone — not an error
                }

                var percent = files.Count > 0 ? (int)Math.Round((i + 1) * 100d / files.Count) : 100;
                if (progressGate.ShouldDeliver(percent))
                {
                    progress(new GameOperationProgress
                    {
                        OperationKind = GameOperationKind.Uninstall,
                        Stage = GameOperationStage.Uninstalling,
                        Progress = percent,
                        IsRunning = true
                    });
                }
            }

            var deletedState = await localInstallationStateStore.DeleteAsync(
                gamePath,
                cancellationToken).ConfigureAwait(false);
            if (deletedState.Kind == LocalInstallationStateKind.IoFailure)
            {
                throw new IOException(deletedState.Error);
            }

            // The download resume marker lives in LOCALAPPDATA and is not under the game
            // directory, so the manifest-driven file deletion above never touches it. Remove
            // it best-effort so a finished uninstall leaves no stale resume state behind.
            try
            {
                checkpointStore.Clear();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of the resume marker; preserve uninstall success.
            }

            var leftovers = new List<string>();
            if (scope == UninstallScope.ThoroughCleanup)
            {
                leftovers.AddRange(
                    await DeleteThoroughCleanupTargetsAsync(gamePath, cancellationToken).ConfigureAwait(false));
            }

            // 快捷方式只在卸载成功之后删（ADR-030）：中途失败会提前返回，快捷方式因此保留下来。
            // 桌面本来就没有、或系统不支持，都不算失败。
            await DeleteDesktopShortcutAsync(snapshot).ConfigureAwait(false);

            await diagnostics.MessageAsync(
                "GameUninstall",
                $"Game uninstall completed.{Environment.NewLine}path: {gamePath}{Environment.NewLine}files: {files.Count}{Environment.NewLine}leftovers: {leftovers.Count}",
                cancellationToken).ConfigureAwait(false);

            // 落在两个删除目标之外的 Prefix 不会被删（ADR-030）：成功文案必须说出来，
            // 否则「彻底清除」看起来做了它没做的事。
            var keptPrefixPath = scope == UninstallScope.ThoroughCleanup
                ? ResolveKeptPrefixPath(snapshot, gamePath)
                : null;
            if (leftovers.Count > 0)
            {
                // 删不掉的项目不改变「游戏已卸载」这件事（manifest 与两个状态文件都已删除），
                // 但它们确实留在盘上，必须点名——否则「彻底清除」一样说得比做得多。
                await diagnostics.WarningAsync(
                    "GameUninstall",
                    $"Thorough cleanup left {leftovers.Count} item(s) behind:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, leftovers),
                    cancellationToken).ConfigureAwait(false);
            }

            return new GameOperationResult
            {
                Success = true,
                Message = BuildCompletionMessage(localizer, leftovers, keptPrefixPath),
                AffectedFileCount = files.Count + 2
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await diagnostics.ErrorAsync(
                "GameUninstall",
                "Uninstalling the game failed.",
                exception,
                CancellationToken.None).ConfigureAwait(false);
            return new GameOperationResult
            {
                Success = false,
                Message = localizer.F(LocalizationKeys.UninstallFailed, exception.Message),
                ErrorCode = GameOperationErrorCode.System
            };
        }
    }

    /// <summary>
    /// 彻底清除的两个目标（ADR-030）：整棵安装目录，与本启动器托管的兼容子树
    /// （&lt;compatibilityRoot&gt;/&lt;gameId&gt;，覆盖该游戏各运行器的默认前缀）。
    /// 测量与删除都走这里，保证「显示多少就删多少」。
    /// </summary>
    private static (string GameRoot, string ManagedPrefixRoot) ResolveCleanupTargets(string gamePath) =>
        (gamePath, GameCompatibilityPaths.GetDefaultGameCompatibilityRoot(GameRuntimeIds.BlueArchiveJapan));

    /// <summary>
    /// 用户自定义且落在受管子树之外的 Prefix（ADR-030）：保留不删，并在成功文案里回报。
    /// 它是用户自选的任意目录，可能与别的程序共用——删它是另一件事，不该由卸载顺手做掉。
    /// </summary>
    /// <remarks>
    /// 只有「彻底清除确实没动它」才配得上「已保留」这句。<c>PrefixPath</c> 是自由文本，用户可以
    /// 把它填进安装目录里（便携安装），而安装目录整棵正是彻底清除的删除目标之一：树都删完了
    /// 再报「已保留：&lt;该路径&gt;」，是被同一次操作当场证伪的一句话。
    /// </remarks>
    private static string? ResolveKeptPrefixPath(LauncherStatusSnapshot snapshot, string gamePath)
    {
        var prefixPath = GameRuntimeConfiguration.FromSettings(snapshot.Settings.GameRuntime).PrefixPath;
        if (string.IsNullOrWhiteSpace(prefixPath))
        {
            return null;
        }

        // 彻底清除的两个删除目标：安装目录整棵、受管兼容子树整棵（ResolveCleanupTargets）。
        var (gameRoot, managedPrefixRoot) = ResolveCleanupTargets(gamePath);
        if (DirectoryTreeDeleter.IsUnder(prefixPath, gameRoot))
        {
            return null;
        }

        return DirectoryTreeDeleter.IsUnder(prefixPath, managedPrefixRoot) ? null : prefixPath;
    }

    /// <summary>
    /// 彻底清除的预检守卫（ADR-030）：游戏根必须是真实目录且不是链接，受管兼容子树必须落在
    /// 受管根内。规则与 <see cref="DeleteThoroughCleanupTargetsAsync"/> 删除时复查的一致，
    /// 提前跑一次是为了「拒绝就什么都不删」。
    /// </summary>
    private static void EnsureCleanupTargetsAreDeletable(string gamePath)
    {
        var (gameRoot, managedPrefixRoot) = ResolveCleanupTargets(gamePath);
        _ = GamePathValidator.GetSafePath(gameRoot, ".");
        DirectoryTreeDeleter.EnsureDeletable(
            managedPrefixRoot,
            GameCompatibilityPaths.GetDefaultCompatibilityRoot());
    }

    /// <summary>
    /// 删掉彻底清除的两个目标，返回删不掉的项目。Windows 上确实存在用户态删不掉的目录项
    /// （Blue Archive 反作弊留下的 <c>Xigncode:{GUID}</c>：列得出来、打不开、无 8.3 短名），
    /// 所以这里的结果由调用方如实上报，而不是当成调用方的错误抛出去。
    /// </summary>
    private async Task<IReadOnlyList<string>> DeleteThoroughCleanupTargetsAsync(
        string gamePath,
        CancellationToken cancellationToken)
    {
        var (gameRoot, managedPrefixRoot) = ResolveCleanupTargets(gamePath);
        var compatibilityRoot = GameCompatibilityPaths.GetDefaultCompatibilityRoot();

        // 整棵删除走线程池：安装目录与 Prefix 都可能有上万条目，不能占着 UI 线程。
        return await Task.Run(
            () =>
            {
                try
                {
                    // 先过游戏目录自己的守卫（顺带拒绝 reparse point 根），再删它本身。
                    var safeGameRoot = GamePathValidator.GetSafePath(gameRoot, ".");
                    var leftover = new List<string>(DirectoryTreeDeleter.Delete(safeGameRoot, gameRoot));
                    leftover.AddRange(DirectoryTreeDeleter.Delete(managedPrefixRoot, compatibilityRoot));
                    return (IReadOnlyList<string>)leftover;
                }
                catch (InvalidOperationException exception)
                {
                    // 预检之后路径又变了（竞争）：折算成 IO 失败，交给既有的失败呈现，
                    // 否则它会冒泡成调用方只记日志的匿名异常。文案就地本地化：守卫抛的是
                    // 仓库自己的英文说明，不该经「卸载失败：{0}」呈给用户（2026-09-15 复核轮），
                    // 原文仍随内部异常进日志。
                    throw new IOException(
                        localizer.F(LocalizationKeys.UninstallRefusedByPathGuard, gamePath),
                        exception);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 彻底清除的完成文案（ADR-030）：两个事实各说各的，缺一不可——删不掉的项目是实际缺口，
    /// 「Prefix 主动保留」是设计内说明，但它可能占着几十 GB，用户看不见就得自己去找目录。
    /// 残留清单点名（上限 <see cref="MaxReportedLeftovers"/>，完整清单始终留在日志里）。
    /// </summary>
    internal static string BuildCompletionMessage(
        LocalizationService localizer,
        IReadOnlyList<string> leftovers,
        string? keptPrefixPath)
    {
        if (leftovers.Count > 0)
        {
            var listed = string.Join(Environment.NewLine, leftovers.Take(MaxReportedLeftovers));
            return keptPrefixPath is null
                ? localizer.F(LocalizationKeys.UninstallCompletedWithLeftovers, listed)
                : localizer.F(
                    LocalizationKeys.UninstallCompletedWithLeftoversKeptPrefix,
                    listed,
                    keptPrefixPath);
        }

        return keptPrefixPath is null
            ? localizer.T(LocalizationKeys.UninstallCompleted)
            : localizer.F(LocalizationKeys.UninstallCompletedKeptPrefix, keptPrefixPath);
    }

    /// <summary>
    /// 桌面快捷方式的删除是 best-effort（ADR-030）：桌面本来就没有、或系统不支持都不算失败，
    /// 失败只记日志——它不该让一次已经完成的卸载变成失败。
    /// </summary>
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

    public async Task<GameOperationResult> ValidateAsync(
        string gamePath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(gamePath))
        {
            return DownloadSession.Failed(localizer.F(LocalizationKeys.GamePathMissing, gamePath), GameOperationErrorCode.Uninstall);
        }

        if (IsSystemProtectPath(gamePath))
        {
            return DownloadSession.Failed(localizer.F(LocalizationKeys.GamePathProtected, gamePath), GameOperationErrorCode.Uninstall);
        }

        try
        {
            DownloadSession.EnsureGamePath(gamePath);
        }
        catch (InvalidOperationException)
        {
            return DownloadSession.Failed(localizer.F(LocalizationKeys.GameDirectoryNameInvalid, GamePaths.GameFolderName), GameOperationErrorCode.Uninstall);
        }

        var localGame = await localInstallationStateStore.ReadAsync(gamePath, cancellationToken).ConfigureAwait(false);
        if (localGame.Kind != LocalInstallationStateKind.Valid)
        {
            return DownloadSession.Failed(localizer.F(LocalizationKeys.GameConfigMetadataMissing, GamePaths.GameConfigFileName), GameOperationErrorCode.Uninstall);
        }

        if (string.IsNullOrWhiteSpace(localGame.GameConfig?.Version) || string.IsNullOrWhiteSpace(localGame.GameConfig?.Name))
        {
            return DownloadSession.Failed(localizer.F(LocalizationKeys.GameConfigMetadataMissing, GamePaths.GameConfigFileName), GameOperationErrorCode.Uninstall);
        }

        // 卸载会删掉整个安装目录，因此闸门要认整族进程，而不是只认配置里那个宿主：反作弊宿主
        // （名字是宿主名的同族变体）在强杀游戏后仍会占着目录，只认宿主就会放行（ADR-032）。
        var gameRunning = await FindRunningGameFailureAsync(localGame.GameConfig, cancellationToken)
            .ConfigureAwait(false);
        if (gameRunning is not null)
        {
            return gameRunning;
        }

        return new GameOperationResult
        {
            Success = true,
            Message = localizer.F(LocalizationKeys.ReadyToUninstall, localGame.Manifest?.Files.Count ?? 0),
            AffectedFileCount = (localGame.Manifest?.Files.Count ?? 0) + 2
        };
    }

    /// <summary>
    /// 「游戏是不是在跑」这道闸门（ADR-032）在本类的入口：预检与删除前的复查共用它，正文在
    /// <see cref="RunningGameGate"/>，与下载／安装／修复侧的判据、报法不会分叉。返回 null 表示
    /// 放行；配置里没有可用名字时不拦（无可识别的判据，闸门不做无根据的拒绝）。
    /// </summary>
    private async Task<GameOperationResult?> FindRunningGameFailureAsync(
        GameLauncherConfig? gameConfig,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameConfig?.Name))
        {
            return null;
        }

        return await RunningGameGate.FindFailureAsync(
            gameProcessTracker,
            localizer,
            LocalizationKeys.GameIsRunning,
            GameProcessNames.FromLaunchConfiguration(gameConfig.Name, gameConfig.Params),
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
