using System;
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
                    return DownloadSession.Failed(
                        localizer.F(LocalizationKeys.UninstallFailed, exception.Message),
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

            if (scope == UninstallScope.ThoroughCleanup)
            {
                await DeleteThoroughCleanupTargetsAsync(gamePath, cancellationToken).ConfigureAwait(false);
            }

            // 快捷方式只在卸载成功之后删（ADR-030）：中途失败会提前返回，快捷方式因此保留下来。
            // 桌面本来就没有、或系统不支持，都不算失败。
            await DeleteDesktopShortcutAsync(snapshot).ConfigureAwait(false);

            await diagnostics.MessageAsync(
                "GameUninstall",
                $"Game uninstall completed.{Environment.NewLine}path: {gamePath}{Environment.NewLine}files: {files.Count}",
                cancellationToken).ConfigureAwait(false);

            // 自定义到受管子树之外的 Prefix 不会被删（ADR-030）：成功文案必须说出来，
            // 否则「彻底清除」看起来做了它没做的事。
            var keptPrefixPath = scope == UninstallScope.ThoroughCleanup
                ? ResolveKeptPrefixPath(snapshot)
                : null;
            return new GameOperationResult
            {
                Success = true,
                Message = keptPrefixPath is null
                    ? localizer.T(LocalizationKeys.UninstallCompleted)
                    : localizer.F(LocalizationKeys.UninstallCompletedKeptPrefix, keptPrefixPath),
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
    private static string? ResolveKeptPrefixPath(LauncherStatusSnapshot snapshot)
    {
        var prefixPath = GameRuntimeConfiguration.FromSettings(snapshot.Settings.GameRuntime).PrefixPath;
        if (string.IsNullOrWhiteSpace(prefixPath))
        {
            return null;
        }

        var managedRoot = GameCompatibilityPaths.GetDefaultGameCompatibilityRoot(GameRuntimeIds.BlueArchiveJapan);
        return DirectoryTreeDeleter.IsUnder(prefixPath, managedRoot) ? null : prefixPath;
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

    private async Task DeleteThoroughCleanupTargetsAsync(string gamePath, CancellationToken cancellationToken)
    {
        var (gameRoot, managedPrefixRoot) = ResolveCleanupTargets(gamePath);
        var compatibilityRoot = GameCompatibilityPaths.GetDefaultCompatibilityRoot();

        // 整棵删除走线程池：安装目录与 Prefix 都可能有上万条目，不能占着 UI 线程。
        await Task.Run(
            () =>
            {
                try
                {
                    // 先过游戏目录自己的守卫（顺带拒绝 reparse point 根），再删它本身。
                    var safeGameRoot = GamePathValidator.GetSafePath(gameRoot, ".");
                    DirectoryTreeDeleter.Delete(safeGameRoot, gameRoot);
                    DirectoryTreeDeleter.Delete(managedPrefixRoot, compatibilityRoot);
                }
                catch (InvalidOperationException exception)
                {
                    // 预检之后路径又变了（竞争）：折算成 IO 失败，交给既有的失败呈现，
                    // 否则它会冒泡成调用方只记日志的匿名异常。
                    throw new IOException(exception.Message, exception);
                }
            },
            cancellationToken).ConfigureAwait(false);
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

        if (await gameProcessTracker.IsGameRunningAsync($"{localGame.GameConfig.Name}.exe", cancellationToken))
        {
            return DownloadSession.Failed(localizer.F(LocalizationKeys.GameIsRunning, $"{localGame.GameConfig.Name}.exe"), GameOperationErrorCode.GameRunning);
        }

        return new GameOperationResult
        {
            Success = true,
            Message = localizer.F(LocalizationKeys.ReadyToUninstall, localGame.Manifest?.Files.Count ?? 0),
            AffectedFileCount = (localGame.Manifest?.Files.Count ?? 0) + 2
        };
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
