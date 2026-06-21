using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using System.Text.Json;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LocalInstallationStateStoreTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string gamePath;
    private readonly LocalInstallationStateStore store = new();

    public LocalInstallationStateStoreTests()
    {
        gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
    }

    [Fact]
    public async Task ReadAsync_WhenInstallationDirectoryDoesNotExist_ReturnsNotInstalled()
    {
        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.NotInstalled, state.Kind);
        Assert.Equal(Path.GetFullPath(gamePath), state.GamePath);
        Assert.Null(state.GameConfig);
        Assert.Null(state.Manifest);
    }

    [Fact]
    public async Task CommitAsync_WhenDataIsValid_WritesAndReadsValidInstallationState()
    {
        Directory.CreateDirectory(gamePath);
        var commit = new LocalInstallationStateCommit(
            "1.2.3",
            "manifest.json",
            "BlueArchive",
            ["--test"],
            [new LocalInstallationFile("BlueArchive.exe", 4, 1234)]);

        var committed = await store.CommitAsync(gamePath, commit);
        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.Valid, committed.Kind);
        Assert.Equal(LocalInstallationStateKind.Valid, state.Kind);
        var config = Assert.IsType<GameLauncherConfig>(state.GameConfig);
        var manifest = Assert.IsType<LocalManifest>(state.Manifest);
        Assert.Equal("1.2.3", config.Version);
        Assert.Equal("1.2.3", manifest.Version);
        Assert.Equal("BlueArchive", config.Name);
        Assert.Equal(["--test"], config.Params);
        var file = Assert.Single(manifest.Files);
        Assert.Equal("BlueArchive.exe", file.Path);
        Assert.Equal("4", file.Size);
        Assert.Equal("1234", file.Hash);
    }

    [Fact]
    public async Task ReadAsync_WhenOnlyOneStateDocumentExists_ReturnsCorruptedWithoutPartialData()
    {
        Directory.CreateDirectory(gamePath);
        await File.WriteAllTextAsync(
            Path.Combine(gamePath, "manifest.json"),
            "{}");

        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.Corrupted, state.Kind);
        Assert.Null(state.GameConfig);
        Assert.Null(state.Manifest);
    }

    [Fact]
    public async Task ReadAsync_WhenJsonIsInvalid_ReturnsCorrupted()
    {
        await CommitValidStateAsync();
        await File.WriteAllTextAsync(Path.Combine(gamePath, "manifest.json"), "{");

        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.Corrupted, state.Kind);
    }

    [Fact]
    public async Task ReadAsync_WhenRequiredPropertyUsesWrongCase_ReturnsCorrupted()
    {
        await CommitValidStateAsync();
        var manifestPath = Path.Combine(gamePath, "manifest.json");
        var manifest = await File.ReadAllTextAsync(manifestPath);
        await File.WriteAllTextAsync(manifestPath, manifest.Replace("\"name\"", "\"Name\"", StringComparison.Ordinal));

        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.Corrupted, state.Kind);
    }

    [Fact]
    public async Task ReadAsync_WhenVcIsInvalid_ReturnsCorrupted()
    {
        await CommitValidStateAsync();
        var configPath = Path.Combine(gamePath, "game-launcher-config.json");
        var config = JsonSerializer.Deserialize<GameLauncherConfig>(await File.ReadAllTextAsync(configPath));
        Assert.NotNull(config);
        config.Vc = "invalid";
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.Corrupted, state.Kind);
    }

    [Fact]
    public async Task ReadAsync_WhenDocumentVersionsDiffer_ReturnsCorrupted()
    {
        await CommitValidStateAsync();
        var configPath = Path.Combine(gamePath, "game-launcher-config.json");
        var config = JsonSerializer.Deserialize<GameLauncherConfig>(await File.ReadAllTextAsync(configPath));
        Assert.NotNull(config);
        config.Version = "9.9.9";
        config.Vc = OfficialHashService.GetGameConfigHash(config);
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.Corrupted, state.Kind);
    }

    [Fact]
    public async Task ReadAsync_WhenStateDocumentCannotBeOpened_ReturnsIoFailure()
    {
        await CommitValidStateAsync();
        await using var locked = new FileStream(
            Path.Combine(gamePath, "manifest.json"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.IoFailure, state.Kind);
        Assert.False(string.IsNullOrWhiteSpace(state.Error));
    }

    [Fact]
    public async Task CommitAsync_WhenTempValidationFails_DoesNotReplaceFormalState()
    {
        await CommitValidStateAsync();
        var failingStore = new LocalInstallationStateStore(
            (path, cancellationToken) => File.WriteAllTextAsync(
                Path.Combine(path, "manifest.json.tmp"),
                "{}",
                cancellationToken));

        var result = await failingStore.CommitAsync(gamePath, CreateCommit("2.0.0"));
        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.Corrupted, result.Kind);
        Assert.Equal(LocalInstallationStateKind.Valid, state.Kind);
        Assert.Equal("1.2.3", state.Manifest?.Version);
    }

    [Fact]
    public async Task CommitAsync_WhenSecondMoveFails_LeavesReadableCorruptedState()
    {
        Directory.CreateDirectory(gamePath);
        var failingStore = new LocalInstallationStateStore(
            (path, _) =>
            {
                Directory.CreateDirectory(Path.Combine(path, "game-launcher-config.json"));
                return Task.CompletedTask;
            });

        var result = await failingStore.CommitAsync(gamePath, CreateCommit("2.0.0"));
        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.IoFailure, result.Kind);
        Assert.Equal(LocalInstallationStateKind.Corrupted, state.Kind);
    }

    [Fact]
    public async Task DeleteAsync_WhenRepeated_ReturnsNotInstalled()
    {
        await CommitValidStateAsync();

        var first = await store.DeleteAsync(gamePath);
        var second = await store.DeleteAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.NotInstalled, first.Kind);
        Assert.Equal(LocalInstallationStateKind.NotInstalled, second.Kind);
        Assert.False(File.Exists(Path.Combine(gamePath, "manifest.json")));
        Assert.False(File.Exists(Path.Combine(gamePath, "game-launcher-config.json")));
    }

    private Task<LocalInstallationState> CommitValidStateAsync()
    {
        Directory.CreateDirectory(gamePath);
        return store.CommitAsync(gamePath, CreateCommit("1.2.3"));
    }

    private static LocalInstallationStateCommit CreateCommit(string version)
    {
        return new LocalInstallationStateCommit(
            version,
            "manifest.json",
            "BlueArchive",
            ["--test"],
            [new LocalInstallationFile("BlueArchive.exe", 4, 1234)]);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
