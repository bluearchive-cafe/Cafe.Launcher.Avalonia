using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Persists in-progress download task state so the user can resume after restart.
/// Mirrors the original Electron launcher's localStorage download-task persistence.
/// </summary>
public sealed class DownloadStateService
{
    private readonly string stateFilePath;

    public DownloadStateService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LauncherConstants.ProductName);
        stateFilePath = Path.Combine(folder, "download_state.json");
    }

    internal DownloadStateService(string stateFilePath)
    {
        this.stateFilePath = stateFilePath;
    }

    public async Task SaveAsync(DownloadTaskState state, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(stateFilePath)!);
        await File.WriteAllTextAsync(stateFilePath, json, cancellationToken);
    }

    public async Task<DownloadTaskState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(stateFilePath))
            return null;
        try
        {
            var json = await File.ReadAllTextAsync(stateFilePath, cancellationToken);
            return JsonSerializer.Deserialize<DownloadTaskState>(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        // File.Delete is intentionally synchronous here — the file is small and the
        // operation completes in microseconds on modern storage.
        if (File.Exists(stateFilePath))
            File.Delete(stateFilePath);
    }
}
