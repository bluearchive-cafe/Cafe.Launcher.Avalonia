using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Testing;
using Xunit;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Tests;

public sealed class DownloadCheckpointStoreTests
{
    [Fact]
    public async Task Clear_GamePaths_OnlyClearsTheSelectedGamesCheckpoint()
    {
        using var directory = TestDirectory.Create();
        var legacy = new DownloadCheckpointStore(directory.DataRoot);
        var baPaths = new GameDataPaths(directory.DataRoot, "blue-archive-jp");
        var stellaPaths = new GameDataPaths(directory.DataRoot, "stella-sora-cn");
        var ba = new DownloadCheckpointStore(baPaths);
        var stella = new DownloadCheckpointStore(stellaPaths);
        await legacy.SaveAsync(new DownloadTaskState { Version = "legacy-version" });
        await ba.SaveAsync(new DownloadTaskState { FormatVersion = 2, GameId = baPaths.GameId, Version = "ba-version" });
        Assert.Null(await stella.LoadAsync());
        await stella.SaveAsync(new DownloadTaskState { FormatVersion = 2, GameId = stellaPaths.GameId, Version = "stella-version" });
        await File.WriteAllTextAsync(baPaths.DownloadStatePath + ".tmp", "{}");
        await File.WriteAllTextAsync(stellaPaths.DownloadStatePath + ".tmp", "{}");

        ba.Clear();

        Assert.Null(await ba.LoadAsync());
        Assert.False(File.Exists(baPaths.DownloadStatePath + ".tmp"));
        Assert.Equal("legacy-version", (await legacy.LoadAsync())?.Version);
        Assert.Equal("stella-version", (await stella.LoadAsync())?.Version);
        Assert.True(File.Exists(directory.DataRoot.DownloadStatePath));
        Assert.True(File.Exists(stellaPaths.DownloadStatePath + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_ValidState_ReplacesCheckpointAtomically()
    {
        var directory = TestDirectory.Create();
        var path = Path.Combine(directory, "download_state.json");
        var store = new DownloadCheckpointStore( TestDataRoot.ForFile(path) );
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
        directory.Dispose();
    }

    [Fact]
    public async Task LoadAsync_MalformedCheckpoint_ReturnsNull()
    {
        var directory = TestDirectory.Create();
        var path = Path.Combine(directory, "download_state.json");
        await File.WriteAllTextAsync(path, "{");
        var store = new DownloadCheckpointStore( TestDataRoot.ForFile(path) );

        var actual = await store.LoadAsync();

        Assert.Null(actual);
        directory.Dispose();
    }

    [Fact]
    public async Task Clear_ExistingCheckpoint_RemovesCheckpointAndTemporaryFile()
    {
        var directory = TestDirectory.Create();
        var path = Path.Combine(directory, "download_state.json");
        await File.WriteAllTextAsync(path, """{"formatVersion":2,"gameId":"blue-archive-jp"}""");
        await File.WriteAllTextAsync(path + ".tmp", "{}");
        var store = new DownloadCheckpointStore( TestDataRoot.ForFile(path) );

        store.Clear();

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        directory.Dispose();
    }

    [Theory]
    [InlineData("""{"formatVersion":2,"gameId":"stella-sora-cn","version":"1"}""")]
    [InlineData("""{"formatVersion":3,"gameId":"blue-archive-jp","version":"1"}""")]
    [InlineData("""{"version":"1","basis":"basis"}""")]
    [InlineData("{")]
    public async Task LoadAndClear_UnrecognizedPrivateCheckpoint_LeaveTheOriginalUntouched(string json)
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.BlueArchiveJapan.RuntimeId);
        Directory.CreateDirectory(paths.Root);
        await File.WriteAllTextAsync(paths.DownloadStatePath, json);
        var bytes = await File.ReadAllBytesAsync(paths.DownloadStatePath);
        var store = new DownloadCheckpointStore(paths);

        Assert.Null(await store.LoadAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new DownloadTaskState
        {
            FormatVersion = 2, GameId = paths.GameId, Version = "replacement"
        }));
        store.Clear();

        Assert.Equal(bytes, await File.ReadAllBytesAsync(paths.DownloadStatePath));
    }

    [Fact]
    public async Task SaveAsync_PrivateStore_RequiresMatchingExplicitGameAndCurrentFormat()
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.StellaSoraChina.RuntimeId);
        var store = new DownloadCheckpointStore(paths);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new DownloadTaskState()));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new DownloadTaskState { FormatVersion = 2, GameId = LauncherProfiles.BlueArchiveJapan.RuntimeId }));
        await store.SaveAsync(new DownloadTaskState { FormatVersion = 2, GameId = paths.GameId, Version = "2.0" });

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(paths.DownloadStatePath));
        Assert.Equal(2, document.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(paths.GameId, document.RootElement.GetProperty("gameId").GetString());
    }

    [Fact]
    public async Task MigrateLegacy_VerifiedBlueArchiveInstallation_WritesIdentifiedStateAndRetainsOriginalBytes()
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.BlueArchiveJapan.RuntimeId);
        var destination = new DownloadCheckpointStore(paths);
        var installationPath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan);
        var gamePath = installationPath.NormalizeGamePath(directory.Sub("install"));
        var localStore = new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan);
        await CommitSyntheticInstallationAsync(localStore, gamePath);
        var original = Encoding.UTF8.GetBytes("\uFEFF" + LegacyJson(gamePath) + "\r\n");
        await File.WriteAllBytesAsync(directory.DataRoot.DownloadStatePath, original);

        var result = await destination.TryMigrateLegacyBlueArchiveAsync(directory.DataRoot, Snapshot(gamePath), localStore, installationPath);

        Assert.Equal(LegacyCheckpointMigrationResult.Migrated, result);
        var state = await destination.LoadAsync();
        Assert.NotNull(state);
        Assert.Equal(2, state.FormatVersion);
        Assert.Equal(paths.GameId, state.GameId);
        Assert.Equal("1.0", state.Version);
        Assert.True(state.IsRepair);
        Assert.True(File.Exists(paths.DownloadMigrationMarkerPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath));
        destination.Clear();
        Assert.Equal(original, await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath));
        Assert.True(File.Exists(paths.DownloadMigrationMarkerPath));
        Assert.Equal(LegacyCheckpointMigrationResult.AlreadyMigrated,
            await destination.TryMigrateLegacyBlueArchiveAsync(directory.DataRoot, Snapshot(gamePath), localStore, installationPath));
        Assert.False(File.Exists(paths.DownloadStatePath));
    }

    [Fact]
    public async Task MigrateLegacy_ExistingPrivateCheckpoint_TakesPrecedenceWithoutOverwritingEitherFile()
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.BlueArchiveJapan.RuntimeId);
        var destination = new DownloadCheckpointStore(paths);
        await destination.SaveAsync(new DownloadTaskState { FormatVersion = 2, GameId = paths.GameId, Version = "new-state" });
        var privateBytes = await File.ReadAllBytesAsync(paths.DownloadStatePath);
        var legacyBytes = Encoding.UTF8.GetBytes("legacy-original-even-if-unreadable");
        await File.WriteAllBytesAsync(directory.DataRoot.DownloadStatePath, legacyBytes);

        var result = await destination.TryMigrateLegacyBlueArchiveAsync(directory.DataRoot, Snapshot("unused"),
            new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan), new GameInstallationPath(LauncherProfiles.BlueArchiveJapan));

        Assert.Equal(LegacyCheckpointMigrationResult.DestinationExists, result);
        Assert.True(File.Exists(paths.DownloadMigrationMarkerPath));
        Assert.Equal(privateBytes, await File.ReadAllBytesAsync(paths.DownloadStatePath));
        Assert.Equal(legacyBytes, await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath));
        destination.Clear();
        Assert.Equal(LegacyCheckpointMigrationResult.AlreadyMigrated,
            await destination.TryMigrateLegacyBlueArchiveAsync(directory.DataRoot, Snapshot("unused"),
                new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan), new GameInstallationPath(LauncherProfiles.BlueArchiveJapan)));
        Assert.False(File.Exists(paths.DownloadStatePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrateLegacy_MissingOrWrongLocalIdentity_DoesNotGuessFromBlueArchiveDirectory(bool wrongGame)
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.BlueArchiveJapan.RuntimeId);
        var destination = new DownloadCheckpointStore(paths);
        var installationPath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan);
        var gamePath = installationPath.NormalizeGamePath(directory.Sub("install"));
        if (wrongGame)
        {
            await CommitSyntheticInstallationAsync(new LocalInstallationStateStore(LauncherProfiles.StellaSoraChina), gamePath);
        }

        var original = Encoding.UTF8.GetBytes(LegacyJson(gamePath));
        await File.WriteAllBytesAsync(directory.DataRoot.DownloadStatePath, original);

        var result = await destination.TryMigrateLegacyBlueArchiveAsync(directory.DataRoot, Snapshot(gamePath),
            new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan), installationPath);

        Assert.Equal(LegacyCheckpointMigrationResult.IdentityUnverified, result);
        Assert.False(File.Exists(paths.DownloadStatePath));
        new DownloadCheckpointStore(directory.DataRoot).Clear();
        Assert.Equal(original, await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("basis")]
    [InlineData("source")]
    [InlineData("path")]
    public async Task MigrateLegacy_IncompatibleCheckpoint_LeavesTheLegacyDocument(string mismatch)
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.BlueArchiveJapan.RuntimeId);
        var installationPath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan);
        var gamePath = installationPath.NormalizeGamePath(directory.Sub("install"));
        var snapshot = Snapshot(gamePath);
        if (mismatch == "version") { snapshot.Remote.GameConfig!.GameLatestVersion = "other"; }
        if (mismatch == "basis") { snapshot.Remote.GameConfig!.GameLatestFilePath = "other"; }
        if (mismatch == "source") { snapshot.GameSettings.PatchUrlGroup = PatchUrlGroups.Cafe; }
        if (mismatch == "path") { snapshot.GameSettings.GamePath = directory.Sub("different-install"); }
        var original = Encoding.UTF8.GetBytes(LegacyJson(gamePath));
        await File.WriteAllBytesAsync(directory.DataRoot.DownloadStatePath, original);

        var result = await new DownloadCheckpointStore(paths).TryMigrateLegacyBlueArchiveAsync(directory.DataRoot,
            snapshot, new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan), installationPath);

        Assert.Equal(LegacyCheckpointMigrationResult.IncompatibleCheckpoint, result);
        Assert.False(File.Exists(paths.DownloadStatePath));
        Assert.Equal(original, await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath));
    }

    [Fact]
    public async Task MigrateLegacy_IoFailure_ReturnsFailureAndKeepsTheSource()
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.BlueArchiveJapan.RuntimeId);
        var installationPath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan);
        var gamePath = installationPath.NormalizeGamePath(directory.Sub("install"));
        var localStore = new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan);
        await CommitSyntheticInstallationAsync(localStore, gamePath);
        var original = Encoding.UTF8.GetBytes(LegacyJson(gamePath));
        await File.WriteAllBytesAsync(directory.DataRoot.DownloadStatePath, original);
        await File.WriteAllTextAsync(directory.Sub("games"), "A file blocks creation of the private state directory.");

        var result = await new DownloadCheckpointStore(paths).TryMigrateLegacyBlueArchiveAsync(directory.DataRoot,
            Snapshot(gamePath), localStore, installationPath);

        Assert.Equal(LegacyCheckpointMigrationResult.IoFailure, result);
        Assert.Equal(original, await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath));
    }

    private static string LegacyJson(string gamePath) => JsonSerializer.Serialize(new
    {
        version = "1.0", basis = "new-basis", path = gamePath, repair = true, patchUrlGroup = PatchUrlGroups.Official
    });

    [Theory]
    [InlineData("{")]
    [InlineData("""{"formatVersion":2,"gameId":"blue-archive-jp"}""")]
    [InlineData("""{"formatVersion":1,"gameId":"stella-sora-cn"}""")]
    [InlineData("""{"formatVersion":1,"gameId":"blue-archive-jp","unknownData":true}""")]
    public async Task MigrateLegacy_UnrecognizedMarker_RefusesImportAndPreservesBothOriginals(string marker)
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.BlueArchiveJapan.RuntimeId);
        Directory.CreateDirectory(paths.Root);
        var original = Encoding.UTF8.GetBytes("legacy-source-retained");
        await File.WriteAllBytesAsync(directory.DataRoot.DownloadStatePath, original);
        await File.WriteAllTextAsync(paths.DownloadMigrationMarkerPath, marker);
        var markerBytes = await File.ReadAllBytesAsync(paths.DownloadMigrationMarkerPath);
        var destination = new DownloadCheckpointStore(paths);

        var result = await destination.TryMigrateLegacyBlueArchiveAsync(directory.DataRoot, Snapshot(directory.Sub("install")),
            new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan), new GameInstallationPath(LauncherProfiles.BlueArchiveJapan));

        Assert.Equal(LegacyCheckpointMigrationResult.InvalidMigrationMarker, result);
        destination.Clear();
        Assert.Equal(original, await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath));
        Assert.Equal(markerBytes, await File.ReadAllBytesAsync(paths.DownloadMigrationMarkerPath));
        Assert.False(File.Exists(paths.DownloadStatePath));
    }

    [Fact]
    public async Task MigrateLegacy_MarkerCommitFailure_DoesNotReportTheCopiedCheckpointAsCompleted()
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.BlueArchiveJapan.RuntimeId);
        var installationPath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan);
        var gamePath = installationPath.NormalizeGamePath(directory.Sub("install"));
        var localStore = new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan);
        await CommitSyntheticInstallationAsync(localStore, gamePath);
        var original = Encoding.UTF8.GetBytes(LegacyJson(gamePath));
        await File.WriteAllBytesAsync(directory.DataRoot.DownloadStatePath, original);
        Directory.CreateDirectory(paths.DownloadMigrationMarkerPath);
        var destination = new DownloadCheckpointStore(paths);

        var result = await destination.TryMigrateLegacyBlueArchiveAsync(directory.DataRoot, Snapshot(gamePath), localStore, installationPath);

        Assert.Equal(LegacyCheckpointMigrationResult.IoFailure, result);
        Assert.True(File.Exists(paths.DownloadStatePath));
        Assert.False(File.Exists(paths.DownloadMigrationMarkerPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath));
        Assert.Equal(LegacyCheckpointMigrationResult.IoFailure,
            await destination.TryMigrateLegacyBlueArchiveAsync(directory.DataRoot, Snapshot(gamePath), localStore, installationPath));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"formatVersion":99,"gameId":"blue-archive-jp"}""")]
    [InlineData("""{"FormatVersion":99}""")]
    [InlineData("""{"formatVersion":2,"gameId":"stella-sora-cn"}""")]
    public async Task MigrateLegacy_InvalidOrFutureDocument_IsPreservedByMigrationAndClear(string json)
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.BlueArchiveJapan.RuntimeId);
        await File.WriteAllTextAsync(directory.DataRoot.DownloadStatePath, json);
        var original = await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath);
        var result = await new DownloadCheckpointStore(paths).TryMigrateLegacyBlueArchiveAsync(directory.DataRoot,
            Snapshot(directory.Sub("install")), new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan),
            new GameInstallationPath(LauncherProfiles.BlueArchiveJapan));

        Assert.Equal(LegacyCheckpointMigrationResult.InvalidCheckpoint, result);
        new DownloadCheckpointStore(directory.DataRoot).Clear();
        Assert.Equal(original, await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath));
        Assert.False(File.Exists(paths.DownloadStatePath));
    }

    [Fact]
    public async Task MigrateLegacy_IdentifiedRootFirstInstall_DoesNotRequireAnAlreadyCommittedInstallation()
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.BlueArchiveJapan.RuntimeId);
        var installationPath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan);
        var gamePath = installationPath.NormalizeGamePath(directory.Sub("install"));
        await new DownloadCheckpointStore(directory.DataRoot).SaveAsync(new DownloadTaskState
        {
            FormatVersion = 2, GameId = paths.GameId, Version = "1.0", Basis = "new-basis", GamePath = gamePath
        });
        var original = await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath);

        var result = await new DownloadCheckpointStore(paths).TryMigrateLegacyBlueArchiveAsync(directory.DataRoot,
            Snapshot(gamePath), new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan), installationPath);

        Assert.Equal(LegacyCheckpointMigrationResult.Migrated, result);
        Assert.Equal(original, await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath));
    }

    private static LauncherStatusSnapshot Snapshot(string gamePath) => new()
    {
        GameId = LauncherProfiles.BlueArchiveJapan.RuntimeId,
        GameSettings = new GameSettings { GamePath = gamePath },
        Remote = new LauncherRemoteState { GameConfig = new GameConfigResponse { GameLatestVersion = "1.0", GameLatestFilePath = "new-basis" } }
    };

    private static Task<LocalInstallationState> CommitSyntheticInstallationAsync(ILocalInstallationStateStore store, string gamePath)
    {
        // This is synthetic validated test state, not evidence of a real installation.
        Directory.CreateDirectory(gamePath);
        return store.CommitAsync(gamePath, new LocalInstallationStateCommit("0.9", "old-basis", "test-entry", [], []));
    }
}
