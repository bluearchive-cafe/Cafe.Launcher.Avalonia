using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class ManifestValidationService
{
    private readonly LauncherApiClient apiClient;

    public ManifestValidationService(LauncherApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    public async Task<ManifestValidationResult> ValidateAsync(
        string gamePath,
        LocalGameState localGame,
        string launchCheckMode,
        string patchUrlGroup,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        if (launchCheckMode == LaunchCheckModes.None)
        {
            return new ManifestValidationResult
            {
                Success = true,
                Message = "Launch check skipped."
            };
        }

        if (launchCheckMode == LaunchCheckModes.RemoteManifest)
        {
            var remoteManifestResult = await GetRemoteManifestFilesAsync(
                localGame,
                patchUrlGroup,
                proxyMode,
                cancellationToken);
            return remoteManifestResult.Files is null
                ? Failed(remoteManifestResult.Message)
                : ValidateFiles(gamePath, remoteManifestResult.Files);
        }

        if (!localGame.ManifestExists)
        {
            return Failed($"Local manifest.json does not exist: {localGame.ManifestPath}");
        }

        if (localGame.Manifest is null)
        {
            return Failed($"Local manifest.json could not be read: {localGame.ManifestPath}");
        }

        return ValidateFiles(gamePath, localGame.Manifest.Files);
    }

    private static ManifestValidationResult ValidateFiles(string gamePath, IReadOnlyList<ManifestFile> files)
    {
        var fileCounts = CountDamagedFiles(gamePath, files);
        var damagedCount = fileCounts.MissingFileCount + fileCounts.SizeMismatchFileCount;
        return new ManifestValidationResult
        {
            Success = damagedCount == 0,
            DamagedFileCount = damagedCount,
            MissingFileCount = fileCounts.MissingFileCount,
            SizeMismatchFileCount = fileCounts.SizeMismatchFileCount,
            Message = damagedCount == 0
                ? "Manifest validation passed."
                : $"Manifest validation failed. Missing files: {fileCounts.MissingFileCount}. Size mismatches: {fileCounts.SizeMismatchFileCount}."
        };
    }

    private async Task<(IReadOnlyList<ManifestFile>? Files, string Message)> GetRemoteManifestFilesAsync(
        LocalGameState localGame,
        string patchUrlGroup,
        string proxyMode,
        CancellationToken cancellationToken)
    {
        var version = localGame.Manifest?.Version;
        var basis = localGame.Manifest?.Basis;
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(basis))
        {
            return (null, "Local manifest.json does not contain version or basis.");
        }

        ManifestUrlResponse manifestUrl;
        try
        {
            manifestUrl = await apiClient.GetManifestUrlAsync(
                version,
                basis,
                patchUrlGroup,
                proxyMode,
                cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return (null, $"Remote manifest URL request failed: {exception.Message}");
        }

        if (string.IsNullOrWhiteSpace(manifestUrl.Url))
        {
            return (null, "Remote manifest URL is empty.");
        }

        try
        {
            var remoteManifest = await apiClient.GetRemoteManifestAsync(
                manifestUrl.Url,
                proxyMode,
                cancellationToken);
            return (remoteManifest.File, "");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return (null, $"Remote manifest download failed: {exception.Message}");
        }
    }

    private static (int MissingFileCount, int SizeMismatchFileCount) CountDamagedFiles(
        string gamePath,
        IReadOnlyList<ManifestFile> files)
    {
        var missingFileCount = 0;
        var sizeMismatchFileCount = 0;

        foreach (var fileItem in files)
        {
            var filePath = GamePathValidator.GetSafePath(gamePath, fileItem.Path);
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                missingFileCount++;
                continue;
            }

            if (fileInfo.Length != FileSizeFormatter.ParseSize(fileItem.Size))
            {
                sizeMismatchFileCount++;
            }
        }

        return (missingFileCount, sizeMismatchFileCount);
    }

    private static ManifestValidationResult Failed(string message)
    {
        return new ManifestValidationResult
        {
            Success = false,
            Message = message
        };
    }
}
