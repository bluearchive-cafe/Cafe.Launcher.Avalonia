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
    private readonly LocalGameStateService localGameStateService;
    private readonly LocalDiagnostics diagnostics;

    public GameUninstallService(LocalGameStateService localGameStateService, LocalDiagnostics diagnostics)
    {
        this.localGameStateService = localGameStateService;
        this.diagnostics = diagnostics;
    }

    public async Task<GameOperationResult> UninstallAsync(
        LauncherStatusSnapshot snapshot,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var gamePath = localGameStateService.NormalizeGamePath(snapshot.LocalGame.GamePath ?? "");
        try
        {
            var validation = await ValidateAsync(gamePath, cancellationToken);
            if (!validation.Success)
            {
                return validation;
            }

            var localGame = await localGameStateService.ReadAsync(gamePath, cancellationToken);
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

            DeleteIfExists(Path.Combine(gamePath, GamePaths.ManifestFileName));
            DeleteIfExists(Path.Combine(gamePath, GamePaths.GameConfigFileName));

            await diagnostics.MessageAsync(
                "Game uninstall completed.",
                $"path: {gamePath}{Environment.NewLine}files: {files.Count}",
                cancellationToken);

            return new GameOperationResult
            {
                Success = true,
                Message = "Uninstall completed.",
                AffectedFileCount = files.Count + 2
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await diagnostics.ErrorAsync("Game uninstall failed.", exception, CancellationToken.None);
            return new GameOperationResult
            {
                Success = false,
                Message = $"Uninstall failed: {exception.Message}",
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
            return Failed($"Game path does not exist: {gamePath}", "uninstall-error");
        }

        if (IsSystemProtectPath(gamePath))
        {
            return Failed($"Game path is protected: {gamePath}", "uninstall-error");
        }

        if (!string.Equals(Path.GetFileName(Path.GetFullPath(gamePath)), GamePaths.GameFolderName, StringComparison.Ordinal))
        {
            return Failed($"Game directory name must be {GamePaths.GameFolderName}.", "uninstall-error");
        }

        var localGame = await localGameStateService.ReadAsync(gamePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(localGame.GameConfig?.Version) || string.IsNullOrWhiteSpace(localGame.GameConfig?.Name))
        {
            return Failed($"{GamePaths.GameConfigFileName} does not contain version or name.", "uninstall-error");
        }

        if (await ProcessService.IsExeRunningAsync($"{localGame.GameConfig.Name}.exe", cancellationToken))
        {
            return Failed($"Game is running: {localGame.GameConfig.Name}.exe", "uninstall-error-running");
        }

        return new GameOperationResult
        {
            Success = true,
            Message = $"Ready to uninstall {localGame.Manifest?.Files.Count ?? 0} files.",
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

    private static void DeleteIfExists(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (FileNotFoundException)
        {
            // Already gone — not an error
        }
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
