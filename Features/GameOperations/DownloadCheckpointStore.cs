using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>Persists and clears resumable game download state atomically.</summary>
public sealed class DownloadCheckpointStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Indented;
    private readonly string temporaryFilePath = filePath + ".tmp";

    /// <summary>Creates a store for the launcher's standard per-user checkpoint path.</summary>
    internal static DownloadCheckpointStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LauncherConstants.ProductName,
        GamePaths.DownloadStateFileName));

    /// <summary>Reads the current checkpoint, or returns <see langword="null"/> when none is usable.</summary>
    public async Task<DownloadTaskState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<DownloadTaskState>(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Atomically replaces the current checkpoint with the supplied state.</summary>
    public async Task SaveAsync(DownloadTaskState state, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath) ?? ".";
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(temporaryFilePath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryFilePath, filePath, overwrite: true);
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
