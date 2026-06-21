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
    private readonly LocalizationService localizer;

    public ManifestValidationService(LauncherApiClient apiClient, LocalizationService localizer)
    {
        this.apiClient = apiClient;
        this.localizer = localizer;
    }

    public async Task<ManifestValidationResult> ValidateAsync(
        string gamePath,
        LocalInstallationState localGame,
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
                Message = localizer.T("launchCheckSkipped")
            };
        }

        if (launchCheckMode == LaunchCheckModes.RemoteManifest)
        {
            var remoteManifestResult = await GetRemoteManifestFilesAsync(
                localGame,
                patchUrlGroup,
                proxyMode,
                cancellationToken).ConfigureAwait(false);
            return remoteManifestResult.Files is null
                ? Failed(remoteManifestResult.Message)
                : ValidateFiles(gamePath, remoteManifestResult.Files);
        }

        if (localGame.Kind == LocalInstallationStateKind.NotInstalled)
        {
            return Failed(localizer.F("localManifestMissing", localGame.ManifestPath));
        }

        if (localGame.Manifest is null)
        {
            return Failed(localizer.F("localManifestUnreadable", localGame.ManifestPath));
        }

        return ValidateFiles(gamePath, localGame.Manifest.Files);
    }

    private ManifestValidationResult ValidateFiles(string gamePath, IReadOnlyList<ManifestFile> files)
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
                ? localizer.T("manifestValidationPassed")
                : localizer.F(
                    "manifestValidationFailed",
                    fileCounts.MissingFileCount,
                    fileCounts.SizeMismatchFileCount)
        };
    }

    private async Task<(IReadOnlyList<ManifestFile>? Files, string Message)> GetRemoteManifestFilesAsync(
        LocalInstallationState localGame,
        string patchUrlGroup,
        string proxyMode,
        CancellationToken cancellationToken)
    {
        var version = localGame.Manifest?.Version;
        var basis = localGame.Manifest?.Basis;
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(basis))
        {
            return (null, localizer.T("localManifestMetadataMissing"));
        }

        ManifestUrlResponse manifestUrl;
        try
        {
            manifestUrl = await apiClient.GetManifestUrlAsync(
                version,
                basis,
                patchUrlGroup,
                proxyMode,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return (null, localizer.F("remoteManifestUrlRequestFailed", exception.Message));
        }

        if (string.IsNullOrWhiteSpace(manifestUrl.Url))
        {
            return (null, localizer.T("remoteManifestUrlEmpty"));
        }

        try
        {
            var remoteManifest = await apiClient.GetRemoteManifestAsync(
                manifestUrl.Url,
                proxyMode,
                cancellationToken).ConfigureAwait(false);
            return (remoteManifest.File, "");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return (null, localizer.F("remoteManifestDownloadFailed", exception.Message));
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
