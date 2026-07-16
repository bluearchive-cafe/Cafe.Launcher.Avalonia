using System;
using System.IO;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DownloadCheckpointStoreTests
{
    [Fact]
    public async Task SaveAsync_ValidState_ReplacesCheckpointAtomically()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "download_state.json");
        var store = new DownloadCheckpointStore(path);
        var state = new DownloadTaskState
        {
            Version = "1.0.0",
            Basis = "manifest.json",
            GamePath = Path.Combine(directory, "BlueArchive_JP"),
            IsRepair = true,
            PatchUrlGroup = PatchUrlGroups.Cafe,
            StartedAt = "2026-07-13T00:00:00.0000000+00:00"
        };

        await store.SaveAsync(state);

        var actual = await store.LoadAsync();
        Assert.NotNull(actual);
        Assert.Equal(state.Version, actual.Version);
        Assert.Equal(state.Basis, actual.Basis);
        Assert.Equal(state.GamePath, actual.GamePath);
        Assert.Equal(state.IsRepair, actual.IsRepair);
        Assert.Equal(state.PatchUrlGroup, actual.PatchUrlGroup);
        Assert.Equal(state.StartedAt, actual.StartedAt);
        Assert.False(File.Exists(path + ".tmp"));
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_MalformedCheckpoint_ReturnsNull()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "download_state.json");
        await File.WriteAllTextAsync(path, "{");
        var store = new DownloadCheckpointStore(path);

        var actual = await store.LoadAsync();

        Assert.Null(actual);
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task Clear_ExistingCheckpoint_RemovesCheckpointAndTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "download_state.json");
        await File.WriteAllTextAsync(path, "{}");
        await File.WriteAllTextAsync(path + ".tmp", "{}");
        var store = new DownloadCheckpointStore(path);

        store.Clear();

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Directory.Delete(directory, recursive: true);
    }
}
