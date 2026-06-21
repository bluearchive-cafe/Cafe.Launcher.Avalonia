using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class GameUninstallService
{
    private readonly LocalInstallationStateStore localInstallationStateStore;
    private readonly GameInstallationPath installationPath;
    private readonly LocalDiagnostics diagnostics;
    private readonly LocalizationService localizer;

    public GameUninstallService(
        LocalInstallationStateStore localInstallationStateStore,
        LocalDiagnostics diagnostics,
        LocalizationService localizer,
        GameInstallationPath installationPath)
    {
        this.localInstallationStateStore = localInstallationStateStore;
        this.installationPath = installationPath;
        this.diagnostics = diagnostics;
        this.localizer = localizer;
    }

    public async Task<GameOperationResult> UninstallAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.RuntimeState != LauncherRuntimeState.Ready)
        {
            return Failed(localizer.T("operationUnavailableForCurrentState"), "invalid-state");
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
                    OperationKind = GameOperationKinds.Uninstall,
                    Stage = "uninstall",
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
                ErrorType = "error-system"
            };
        }
    }

    public async Task<GameOperationResult> ValidateAsync(
        string gamePath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(gamePath))
        {
            return Failed(localizer.F("gamePathMissing", gamePath), "uninstall-error");
        }

        if (IsSystemProtectPath(gamePath))
        {
            return Failed(localizer.F("gamePathProtected", gamePath), "uninstall-error");
        }

        if (!string.Equals(Path.GetFileName(Path.GetFullPath(gamePath)), GamePaths.GameFolderName, StringComparison.Ordinal))
        {
            return Failed(localizer.F("gameDirectoryNameInvalid", GamePaths.GameFolderName), "uninstall-error");
        }

        var localGame = await localInstallationStateStore.ReadAsync(gamePath, cancellationToken).ConfigureAwait(false);
        if (localGame.Kind != LocalInstallationStateKind.Valid)
        {
            return Failed(localizer.F("gameConfigMetadataMissing", GamePaths.GameConfigFileName), "uninstall-error");
        }

        if (string.IsNullOrWhiteSpace(localGame.GameConfig?.Version) || string.IsNullOrWhiteSpace(localGame.GameConfig?.Name))
        {
            return Failed(localizer.F("gameConfigMetadataMissing", GamePaths.GameConfigFileName), "uninstall-error");
        }

        if (await ProcessService.IsExeRunningAsync($"{localGame.GameConfig.Name}.exe", cancellationToken))
        {
            return Failed(localizer.F("gameIsRunning", $"{localGame.GameConfig.Name}.exe"), "uninstall-error-running");
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
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        return protectedPaths
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => Path.GetFullPath(item).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Any(item => string.Equals(fullPath, item, StringComparison.OrdinalIgnoreCase));
    }
    private static GameOperationResult Failed(string message, string errorType)
    {
        return new GameOperationResult
        {
            Success = false,
            Message = message,
            ErrorType = errorType
        };
    }
}
