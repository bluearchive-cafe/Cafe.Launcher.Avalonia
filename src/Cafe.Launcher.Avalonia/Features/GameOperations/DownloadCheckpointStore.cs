using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>Persists and clears resumable game download state atomically.</summary>
public sealed class DownloadCheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Indented;
    private readonly string filePath;
    private readonly string temporaryFilePath;

    public DownloadCheckpointStore(LauncherDataRoot dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        filePath = dataRoot.DownloadStatePath;
        temporaryFilePath = filePath + ".tmp";
    }

    /// <summary>Reads the current checkpoint, or returns <see langword="null"/> when none is usable.</summary>
    public async Task<DownloadTaskState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return await AtomicJsonFileStore.ReadAsync<DownloadTaskState>(
                filePath,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (StorageFailure.IsRecoverableOrInvalidJson(exception))
        {
            return null;
        }
    }

    /// <summary>Atomically replaces the current checkpoint with the supplied state.</summary>
    public async Task SaveAsync(DownloadTaskState state, CancellationToken cancellationToken = default)
    {
        await AtomicJsonFileStore.WriteAsync(
            filePath,
            state,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes both the committed checkpoint and a leftover temporary checkpoint.</summary>
    public void Clear()
    {
        DeleteIfPresent(filePath);
        DeleteIfPresent(temporaryFilePath);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
