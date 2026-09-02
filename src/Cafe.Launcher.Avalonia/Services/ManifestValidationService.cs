using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class ManifestValidationService
{
    private readonly LauncherApiClient apiClient;
    private readonly RemoteManifestService remoteManifestService;
    private readonly LocalizationService localizer;

    public ManifestValidationService(
        LauncherApiClient apiClient,
        RemoteManifestService remoteManifestService,
        LocalizationService localizer)
    {
        this.apiClient = apiClient;
        this.remoteManifestService = remoteManifestService;
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
                Message = localizer.T(LocalizationKeys.LaunchCheckSkipped)
            };
        }

        if (launchCheckMode == LaunchCheckModes.RemoteManifest)
        {
            var version = localGame.Manifest?.Version;
            var basis = localGame.Manifest?.Basis;
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(basis))
            {
                return Failed(localizer.T(LocalizationKeys.LocalManifestMetadataMissing));
            }

            try
            {
                var remoteManifest = await remoteManifestService.GetRequiredManifestAsync(
                    version, basis, patchUrlGroup, proxyMode, cancellationToken).ConfigureAwait(false);
                return ValidateFiles(gamePath, remoteManifest.File);
            }
            catch (Exception exception)
                when (exception is InvalidOperationException
                    or HttpRequestException
                    or TaskCanceledException)
            {
                // Fail open, matching the official launcher (its getCurrentManifestFiles
                // returns [] on error, so checkStat passes). When the remote manifest for
                // the locally recorded version/basis can't be obtained — empty URL, network
                // failure, or the build was de-listed after a server re-pack — allow the
                // launch instead of blocking it. Blocking here would otherwise produce an
                // unfixable "launch blocked / nothing to repair" loop, because repair targets
                // the latest manifest while this check targets the local-basis manifest.
                return new ManifestValidationResult
                {
                    Success = true,
                    Message = localizer.T(LocalizationKeys.LaunchCheckSkipped)
                };
            }
        }

        if (localGame.Kind == LocalInstallationStateKind.NotInstalled)
        {
            return Failed(localizer.F(LocalizationKeys.LocalManifestMissing, localGame.ManifestPath));
        }

        if (localGame.Manifest is null)
        {
            return Failed(localizer.F(LocalizationKeys.LocalManifestUnreadable, localGame.ManifestPath));
        }

        return await Task.Run(() => ValidateFiles(gamePath, localGame.Manifest.Files)).ConfigureAwait(false);
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
                ? localizer.T(LocalizationKeys.ManifestValidationPassed)
                : localizer.F(
                    "manifestValidationFailed",
                    fileCounts.MissingFileCount,
                    fileCounts.SizeMismatchFileCount)
        };
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

            if (fileInfo.Length != fileItem.SizeBytes)
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
