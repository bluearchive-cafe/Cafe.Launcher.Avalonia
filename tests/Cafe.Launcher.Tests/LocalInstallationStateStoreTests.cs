using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Testing;
using System.Text.Json;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

public sealed class LocalInstallationStateStoreTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();
    private readonly string gamePath;
    private readonly LocalInstallationStateStore store = new(LauncherProfiles.BlueArchiveJapan);

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
            [new LocalInstallationFile("BlueArchive.exe", 4, "1234")]);

        var committed = await store.CommitAsync(gamePath, commit);
        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.Valid, committed.Kind);
        Assert.Equal(LocalInstallationStateKind.Valid, state.Kind);
        var config = Assert.IsType<GameLauncherConfig>(state.GameConfig);
        var manifest = Assert.IsType<LocalManifest>(state.Manifest);
        Assert.False(state.HasDifferentGameIdentity);
        Assert.Equal("1.2.3", config.Version);
        Assert.Equal("1.2.3", manifest.Version);
        Assert.Equal("BlueArchive", config.Name);
        Assert.Equal(["--test"], config.Params ?? []);
        var file = Assert.Single(manifest.Files);
        Assert.Equal("BlueArchive.exe", file.Path);
        Assert.Equal("4", file.Size);
        Assert.Equal("1234", file.Hash);

        // The official launcher hashes these values in wire order.
        using var manifestJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(gamePath, "manifest.json")));
        var manifestRoot = manifestJson.RootElement;
        Assert.Equal(["name", "version", "basis", "vc", "files"], manifestRoot.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["path", "hash", "size", "vc"], manifestRoot.GetProperty("files")[0].EnumerateObject().Select(p => p.Name));

        using var configJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(gamePath, "game-launcher-config.json")));
        Assert.Equal(["tag", "name", "params", "version", "vc"], configJson.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task CommitAsync_WhenStellaProfileStoresNoLaunchParameters_OmitsParamsFromTheConfig()
    {
        // Stella 的 config 没有 params 字段，vc 也按三种字段计算；写出一个空数组
        // 会让校验（以及服务端的身份判断）与服务端格式不一致。
        Directory.CreateDirectory(gamePath);
        var stellaStore = new LocalInstallationStateStore(LauncherProfiles.StellaSoraChina);
        var commit = new LocalInstallationStateCommit(
            "1.2.3",
            "manifest.json",
            "StellaSora",
            [],
            [new LocalInstallationFile("StellaSora.exe", 4, "1234")]);

        var state = await stellaStore.CommitAsync(gamePath, commit);

        Assert.Equal(LocalInstallationStateKind.Valid, state.Kind);
        Assert.Null(Assert.IsType<GameLauncherConfig>(state.GameConfig).Params);
        var json = await File.ReadAllTextAsync(Path.Combine(gamePath, LauncherPaths.GameConfigFileName));
        Assert.DoesNotContain("\"params\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitAsync_WhenStellaProfileReceivesLaunchParameters_ThrowsWithoutWritingState()
    {
        Directory.CreateDirectory(gamePath);
        var stellaStore = new LocalInstallationStateStore(LauncherProfiles.StellaSoraChina);

        await Assert.ThrowsAsync<ArgumentException>(() => stellaStore.CommitAsync(gamePath, CreateCommit("1.2.3")));

        Assert.False(File.Exists(Path.Combine(gamePath, LauncherPaths.ManifestFileName)));
        Assert.False(File.Exists(Path.Combine(gamePath, LauncherPaths.GameConfigFileName)));
    }

    [Fact]
    public async Task ReadAsync_WhenStateBelongsToAnotherGame_ReportsTheIdentityMismatch()
    {
        // 用户可能把 BA 的路径指向 Stella 的安装目录（或反之）：两份文档各自自洽，
        // 但身份不符。表现层要能据此解释，而不是笼统地报「安装损坏」。
        Directory.CreateDirectory(gamePath);
        var stellaStore = new LocalInstallationStateStore(LauncherProfiles.StellaSoraChina);
        var commit = new LocalInstallationStateCommit(
            "1.2.3",
            "manifest.json",
            "StellaSora",
            [],
            [new LocalInstallationFile("StellaSora.exe", 4, "1234")]);
        Assert.Equal(LocalInstallationStateKind.Valid, (await stellaStore.CommitAsync(gamePath, commit)).Kind);

        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.Corrupted, state.Kind);
        Assert.True(state.HasDifferentGameIdentity);
        Assert.Null(state.GameConfig);
        Assert.Null(state.Manifest);
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
    public async Task ReadAsync_WhenOnlyTempDocumentsExist_ReturnsNotInstalled()
    {
        Directory.CreateDirectory(gamePath);
        await File.WriteAllTextAsync(Path.Combine(gamePath, "manifest.json.tmp"), "{}");
        await File.WriteAllTextAsync(Path.Combine(gamePath, "game-launcher-config.json.tmp"), "{}");

        var state = await store.ReadAsync(gamePath);

        Assert.Equal(LocalInstallationStateKind.NotInstalled, state.Kind);
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
        var failingStore = new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan, 
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
        var failingStore = new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan, 
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

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("18446744073709551616")]
    public async Task CommitAsync_WhenCrc64IsNotUnsignedDecimal_ThrowsWithoutWritingState(string crc64)
    {
        Directory.CreateDirectory(gamePath);
        var commit = CreateCommit("1.2.3") with
        {
            Files = [new LocalInstallationFile("BlueArchive.exe", 4, crc64)]
        };

        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitAsync(gamePath, commit));

        Assert.False(File.Exists(Path.Combine(gamePath, "manifest.json")));
        Assert.False(File.Exists(Path.Combine(gamePath, "game-launcher-config.json")));
        Assert.False(File.Exists(Path.Combine(gamePath, "manifest.json.tmp")));
        Assert.False(File.Exists(Path.Combine(gamePath, "game-launcher-config.json.tmp")));
    }

    [Fact]
    public async Task CommitAsync_WhenResolvedPathsRepeat_ThrowsWithoutWritingState()
    {
        Directory.CreateDirectory(gamePath);
        var commit = CreateCommit("1.2.3") with
        {
            Files =
            [
                new LocalInstallationFile("Data/file.bin", 4, "1"),
                new LocalInstallationFile("data/FILE.bin", 4, "2")
            ]
        };

        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitAsync(gamePath, commit));

        Assert.False(File.Exists(Path.Combine(gamePath, "manifest.json.tmp")));
        Assert.False(File.Exists(Path.Combine(gamePath, "game-launcher-config.json.tmp")));
    }

    [Fact]
    public async Task Operations_SerializePerPathWithoutBlockingDifferentPaths()
    {
        Directory.CreateDirectory(gamePath);
        var tempFilesWritten = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingStore = new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan, 
            async (_, cancellationToken) =>
            {
                tempFilesWritten.TrySetResult();
                await releaseCommit.Task.WaitAsync(cancellationToken);
            });
        var commitTask = blockingStore.CommitAsync(gamePath, CreateCommit("1.2.3"));
        await tempFilesWritten.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var samePathRead = blockingStore.ReadAsync(gamePath);
        var otherPathRead = blockingStore.ReadAsync(Path.Combine(tempDir, "other"));
        var otherState = await otherPathRead.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(samePathRead.IsCompleted);
        Assert.Equal(LocalInstallationStateKind.NotInstalled, otherState.Kind);

        releaseCommit.TrySetResult();
        Assert.Equal(LocalInstallationStateKind.Valid, (await commitTask).Kind);
        Assert.Equal(LocalInstallationStateKind.Valid, (await samePathRead).Kind);
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
            [new LocalInstallationFile("BlueArchive.exe", 4, "1234")]);
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
