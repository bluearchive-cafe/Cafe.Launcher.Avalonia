using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Computes install, update, and repair plans by diffing the local installation
/// state against the remote manifests.
/// </summary>
internal sealed class ManifestDiffCalculator
{
    private readonly RemoteManifestService remoteManifestService;
    private readonly LocalInstallationStateStore localInstallationStateStore;
    private readonly Crc64Service crc64Service;

    internal ManifestDiffCalculator(
        RemoteManifestService remoteManifestService,
        LocalInstallationStateStore localInstallationStateStore,
        Crc64Service crc64Service)
    {
        this.remoteManifestService = remoteManifestService;
        this.localInstallationStateStore = localInstallationStateStore;
        this.crc64Service = crc64Service;
    }

    internal async Task<DownloadPlan> BuildInstallOrUpdatePlanAsync(
        string gamePath,
        LocalInstallationState localGame,
        GameConfigResponse gameConfig,
        string patchUrlGroup,
        string proxyMode,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        // Current files: best-effort remote fetch matching the local version, fall back to local manifest.
        var currentFiles = localGame.Manifest?.Files ?? [];
        if (localGame.Manifest is not null
            && !string.IsNullOrWhiteSpace(localGame.Manifest.Version)
            && !string.IsNullOrWhiteSpace(localGame.Manifest.Basis))
        {
            var currentManifest = await remoteManifestService.GetOptionalManifestAsync(
                localGame.Manifest.Version,
                localGame.Manifest.Basis,
                patchUrlGroup,
                proxyMode,
                cancellationToken).ConfigureAwait(false);
            if (currentManifest is not null)
            {
                currentFiles = currentManifest.File;
            }
        }

        // Latest manifest: required for diff computation.
        var version = gameConfig.GameLatestVersion ?? "";
        var basis = gameConfig.GameLatestFilePath ?? "";
        var latestManifest = await remoteManifestService.GetRequiredManifestAsync(
            version,
            basis,
            patchUrlGroup,
            proxyMode,
            cancellationToken).ConfigureAwait(false);
        var statDiff = CheckStat(
            currentFiles,
            gamePath,
            value => progress(GameDownloadService.CreateProgress(GameOperationKind.Download, GameOperationStage.UpdateCheck, value)));
        var expected = GameManifestDiff(currentFiles, latestManifest.File);
        var actual = GameResultMerge(expected, new DownloadPlan { NeedDownload = statDiff });

        actual.Source = latestManifest.Source ?? "";
        actual.ManifestFiles = latestManifest.File;
        return actual;
    }

    internal async Task<DownloadPlan> BuildRepairPlanAsync(
        string gamePath,
        GameConfigResponse gameConfig,
        string patchUrlGroup,
        string proxyMode,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var localGame = await localInstallationStateStore.ReadAsync(gamePath, cancellationToken).ConfigureAwait(false);
        var version = gameConfig.GameLatestVersion ?? "";
        var basis = gameConfig.GameLatestFilePath ?? "";
        var latestManifest = await remoteManifestService.GetRequiredManifestAsync(
            version,
            basis,
            patchUrlGroup,
            proxyMode,
            cancellationToken).ConfigureAwait(false);

        var hashDiff = await CheckHashAsync(
            latestManifest.File,
            gamePath,
            value => progress(GameDownloadService.CreateProgress(GameOperationKind.Repair, GameOperationStage.RepairCheck, value)),
            cancellationToken).ConfigureAwait(false);
        var needDelete = localGame.Kind == LocalInstallationStateKind.Valid
            ? GameManifestDiff(localGame.Manifest?.Files ?? [], latestManifest.File).NeedDelete
            : [];
        var actual = new DownloadPlan
        {
            NeedDownload = hashDiff,
            NeedDelete = needDelete
        };

        actual.Source = latestManifest.Source ?? "";
        actual.ManifestFiles = latestManifest.File;

        // Report repair-confirm with diff summary (matches original's repair-confirm progress = -1)
        progress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Repair,
            Stage = GameOperationStage.RepairConfirmation,
            Progress = -1,
            AffectedFileCount = actual.NeedDownload.Count + actual.NeedDelete.Count,
            DownloadedSize = actual.NeedDownload.Sum(f => f.SizeBytes),
            IsRunning = true,
            CanStop = false
        });

        return actual;
    }

    /// <summary>
    /// Hash-based diff of two manifest lists: returns files to download (present
    /// in the new list with a different hash or missing) and files to delete
    /// (present only in the old list).
    /// </summary>
    internal static DownloadPlan GameManifestDiff(IReadOnlyList<ManifestFile> oldList, IReadOnlyList<ManifestFile> newList)
    {
        var needDownload = newList.ToDictionary(file => file.Path, file => file, StringComparer.Ordinal);
        var needDelete = new Dictionary<string, ManifestFile>(StringComparer.Ordinal);

        foreach (var oldFile in oldList)
        {
            if (!needDownload.TryGetValue(oldFile.Path, out var newFile))
            {
                needDelete[oldFile.Path] = oldFile;
            }
            else if (newFile.Hash == oldFile.Hash)
            {
                needDownload.Remove(oldFile.Path);
            }
        }

        return new DownloadPlan
        {
            NeedDownload = needDownload.Values.ToList(),
            NeedDelete = needDelete.Values.ToList()
        };
    }

    /// <summary>
    /// Merges multiple plans into one, deduplicating file paths so no file
    /// appears twice across the merged lists.
    /// </summary>
    internal static DownloadPlan GameResultMerge(params DownloadPlan[] plans)
    {
        var result = new DownloadPlan();
        var processed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in plans.SelectMany(plan => plan.NeedDelete))
        {
            if (processed.Add(file.Path))
            {
                result.NeedDelete.Add(file);
            }
        }

        foreach (var file in plans.SelectMany(plan => plan.NeedDownload))
        {
            if (processed.Add(file.Path))
            {
                result.NeedDownload.Add(file);
            }
        }

        return result;
    }

    /// <summary>
    /// Checks file size against the manifest; files that are missing or have a
    /// different size are added to the diff.
    /// </summary>
    internal static List<ManifestFile> CheckStat(
        IReadOnlyList<ManifestFile> files,
        string gamePath,
        Action<int>? progress)
    {
        var diff = new List<ManifestFile>();
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var filePath = GamePathValidator.GetSafePath(gamePath, file.Path);
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length != file.SizeBytes)
            {
                diff.Add(file);
            }

            progress?.Invoke((int)Math.Round((i + 1) * 100d / files.Count));
        }

        return diff;
    }

    private async Task<List<ManifestFile>> CheckHashAsync(
        IReadOnlyList<ManifestFile> files,
        string gamePath,
        Action<int>? progress,
        CancellationToken cancellationToken)
    {
        var diff = new List<ManifestFile>();
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var filePath = GamePathValidator.GetSafePath(gamePath, file.Path);
            if (!File.Exists(filePath))
            {
                diff.Add(file);
                continue;
            }

            var crc64 = await crc64Service.ComputeFileAsync(filePath, null, cancellationToken).ConfigureAwait(false);
            if (crc64 != file.Hash)
            {
                diff.Add(file);
            }

            progress?.Invoke((int)Math.Round((i + 1) * 100d / files.Count));
        }

        return diff;
    }
}
