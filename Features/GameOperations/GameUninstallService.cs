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

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

public sealed class GameUninstallService
{
    private readonly LocalInstallationStateStore localInstallationStateStore;
    private readonly GameInstallationPath installationPath;
    private readonly LocalDiagnostics diagnostics;
    private readonly LocalizationService localizer;
    private readonly DownloadCheckpointStore checkpointStore;

    public GameUninstallService(
        LocalInstallationStateStore localInstallationStateStore,
        LocalDiagnostics diagnostics,
        LocalizationService localizer,
        GameInstallationPath installationPath)
        : this(
            localInstallationStateStore,
            diagnostics,
            localizer,
            installationPath,
            DownloadCheckpointStore.CreateDefault())
    {
    }

    internal GameUninstallService(
        LocalInstallationStateStore localInstallationStateStore,
        LocalDiagnostics diagnostics,
        LocalizationService localizer,
        GameInstallationPath installationPath,
        DownloadCheckpointStore checkpointStore)
    {
        this.localInstallationStateStore = localInstallationStateStore;
        this.installationPath = installationPath;
        this.diagnostics = diagnostics;
        this.localizer = localizer;
        this.checkpointStore = checkpointStore;
    }

    public async Task<GameOperationResult> UninstallAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.RuntimeState != LauncherRuntimeState.Ready)
        {
            return DownloadSession.Failed(localizer.T("operationUnavailableForCurrentState"), GameOperationErrorCode.InvalidState);
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

                progress(new GameOperationProgress
                {
                    OperationKind = GameOperationKind.Uninstall,
                    Stage = GameOperationStage.Uninstalling,
                    Progress = files.Count > 0 ? (int)Math.Round((i + 1) * 100d / files.Count) : 100,
                    IsRunning = true
                });
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

            await diagnostics.MessageAsync(
                "Game uninstall completed.",
                $"path: {gamePath}{Environment.NewLine}files: {files.Count}",
                cancellationToken).ConfigureAwait(false);

            return new GameOperationResult
            {
                Success = true,
                Message = localizer.T("uninstallCompleted"),
                AffectedFileCount = files.Count + 2
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await diagnostics.ErrorAsync("Game uninstall failed.", exception, CancellationToken.None).ConfigureAwait(false);
            return new GameOperationResult
            {
                Success = false,
                Message = localizer.F("uninstallFailed", exception.Message),
                ErrorCode = GameOperationErrorCode.System
            };
        }
    }

    public async Task<GameOperationResult> ValidateAsync(
        string gamePath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(gamePath))
        {
            return DownloadSession.Failed(localizer.F("gamePathMissing", gamePath), GameOperationErrorCode.Uninstall);
        }

        if (IsSystemProtectPath(gamePath))
        {
            return DownloadSession.Failed(localizer.F("gamePathProtected", gamePath), GameOperationErrorCode.Uninstall);
        }

        try
        {
            DownloadSession.EnsureGamePath(gamePath);
        }
        catch (InvalidOperationException)
        {
            return DownloadSession.Failed(localizer.F("gameDirectoryNameInvalid", GamePaths.GameFolderName), GameOperationErrorCode.Uninstall);
        }

        var localGame = await localInstallationStateStore.ReadAsync(gamePath, cancellationToken).ConfigureAwait(false);
        if (localGame.Kind != LocalInstallationStateKind.Valid)
        {
            return DownloadSession.Failed(localizer.F("gameConfigMetadataMissing", GamePaths.GameConfigFileName), GameOperationErrorCode.Uninstall);
        }

        if (string.IsNullOrWhiteSpace(localGame.GameConfig?.Version) || string.IsNullOrWhiteSpace(localGame.GameConfig?.Name))
        {
            return DownloadSession.Failed(localizer.F("gameConfigMetadataMissing", GamePaths.GameConfigFileName), GameOperationErrorCode.Uninstall);
        }

        if (await ProcessService.IsExeRunningAsync($"{localGame.GameConfig.Name}.exe", cancellationToken))
        {
            return DownloadSession.Failed(localizer.F("gameIsRunning", $"{localGame.GameConfig.Name}.exe"), GameOperationErrorCode.GameRunning);
        }

        return new GameOperationResult
        {
            Success = true,
            Message = localizer.F("readyToUninstall", localGame.Manifest?.Files.Count ?? 0),
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
