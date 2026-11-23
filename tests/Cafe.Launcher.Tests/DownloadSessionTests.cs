using System.Text.Json;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.UI.Services.GameRuntime;
using Cafe.Launcher.Testing;
using Xunit;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Services.Auth;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

public sealed class DownloadSessionTests
{
    [Fact]
    public void EnsureGamePath_WhenFolderNameMatches_Passes()
    {
        var path = Path.Combine(Path.GetTempPath(), LauncherProfiles.BlueArchiveJapan.GameFolderName);

        GamePathValidator.EnsureGameDirectoryName(path, LauncherProfiles.BlueArchiveJapan.GameFolderName);
    }

    [Fact]
    public void EnsureGamePath_WhenFolderNameDiffers_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), "NotTheGameFolder");

        Assert.Throws<InvalidOperationException>(() => GamePathValidator.EnsureGameDirectoryName(path, LauncherProfiles.BlueArchiveJapan.GameFolderName));
    }

    [Fact]
    public void Failed_MapsMessageErrorCodeAndCounts()
    {
        var result = GameOperationOutcomes.Failed("failed", GameOperationErrorCode.Network, affectedFileCount: 3, failedFileCount: 2);

        Assert.False(result.Success);
        Assert.Equal("failed", result.Message);
        Assert.Equal(GameOperationErrorCode.Network, result.ErrorCode);
        Assert.Equal(3, result.AffectedFileCount);
        Assert.Equal(2, result.FailedFileCount);
    }

    [Fact]
    public void CreateProgress_ForDownload_EnablesStopAndDisablesPause()
    {
        var progress = GameOperationProgressFactory.CreateProgress(GameOperationKind.Download, GameOperationStage.UpdateCheck, 25);

        Assert.Equal(GameOperationKind.Download, progress.OperationKind);
        Assert.Equal(GameOperationStage.UpdateCheck, progress.Stage);
        Assert.Equal(25, progress.Progress);
        Assert.True(progress.IsRunning);
        Assert.True(progress.CanStop);
        Assert.False(progress.CanPause);
    }

    [Fact]
    public void Pause_ThenResume_TogglesPausedState()
    {
        using var session = CreateSession();

        Assert.False(session.IsPaused);
        session.Pause();
        Assert.True(session.IsPaused);
        session.Resume();
        Assert.False(session.IsPaused);
    }

    [Fact]
    public void Stop_CancelsSessionToken()
    {
        using var session = CreateSession();

        Assert.False(session.CancellationTokenSource.IsCancellationRequested);
        session.Stop(DownloadStopReason.ApplicationExit);
        Assert.True(session.CancellationTokenSource.IsCancellationRequested);
    }

    [Fact]
    public void Dispose_IsIdempotentAndReleasesPauseGate()
    {
        var session = CreateSession();

        session.Pause();
        Assert.True(session.IsPaused);

        session.Dispose();
        session.Dispose();

        Assert.False(session.IsPaused);
    }

    /// <summary>
    /// Builds a session over real lightweight services. Lifecycle tests never call
    /// RunAsync, so the network-backed collaborators are only constructed, not used.
    /// </summary>

    [Fact]
    public void LocalInstallationStateMatchesCommit_FullyMatchingState_ReturnsTrue()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();

        Assert.True(DownloadSession.LocalInstallationStateMatchesCommit(LauncherProfiles.BlueArchiveJapan.Tag, state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_NotValidState_ReturnsFalse()
    {
        var (state, gameConfig, files) =
            CreateMatchingStateFixture(LocalInstallationStateKind.Corrupted);

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(LauncherProfiles.BlueArchiveJapan.Tag, state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_VersionDiffers_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        gameConfig.GameLatestVersion = "1.73.0";

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(LauncherProfiles.BlueArchiveJapan.Tag, state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_BasisDiffers_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        gameConfig.GameLatestFilePath = "basis-2";

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(LauncherProfiles.BlueArchiveJapan.Tag, state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_ExeNameDiffers_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        gameConfig.GameStartExeName = "Other.exe";

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(LauncherProfiles.BlueArchiveJapan.Tag, state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_LaunchParamsDiffer_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        gameConfig.GameStartParams = ["-op", "lite"];

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(LauncherProfiles.BlueArchiveJapan.Tag, state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_FileCountDiffers_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        files.RemoveAt(files.Count - 1);

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(LauncherProfiles.BlueArchiveJapan.Tag, state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_FileHashDiffers_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        state.Manifest!.Files[0].Hash = "999";

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(LauncherProfiles.BlueArchiveJapan.Tag, state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_FileSizeDiffers_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        state.Manifest!.Files[0].Size = "11";

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(LauncherProfiles.BlueArchiveJapan.Tag, state, gameConfig, files));
    }

    [Theory]
    [InlineData("blue-archive-jp", "stella-sora-cn", "same")]
    [InlineData("blue-archive-jp", "stella-sora-cn", "parent")]
    [InlineData("blue-archive-jp", "stella-sora-cn", "child")]
    [InlineData("stella-sora-cn", "blue-archive-jp", "same")]
    [InlineData("stella-sora-cn", "blue-archive-jp", "parent")]
    [InlineData("stella-sora-cn", "blue-archive-jp", "child")]
    public async Task RunAndResume_ManuallyOverlappingSettings_LeaveAllGameFilesAndCheckpointBytesUntouched(
        string targetId, string otherId, string relation)
    {
        using var directory = TestDirectory.Create();
        var target = LauncherProfiles.GetSupportedGame(targetId);
        var otherPaths = new GameInstallationPath(LauncherProfiles.GetSupportedGame(otherId));
        var outer = otherPaths.NormalizeGamePath(directory.Sub("outer"));
        var gamePath = new GameInstallationPath(target).NormalizeGamePath(relation == "parent" ? outer : directory.Sub("install"));
        Directory.CreateDirectory(gamePath);
        var sentinel = Path.Combine(gamePath, "sentinel.bin");
        byte[] original = [1, 7, 9, 23];
        await File.WriteAllBytesAsync(sentinel, original);
        var settings = new LauncherSettings();
        settings.GetGameSettings(targetId).GamePath = gamePath;
        settings.GetGameSettings(otherId).GamePath = relation switch
        {
            "parent" => outer,
            "child" => otherPaths.NormalizeGamePath(gamePath),
            _ => gamePath
        };
        // Simulate a hand-edited document, bypassing the UI and the only production writer.
        await File.WriteAllTextAsync(directory.DataRoot.SettingsPath, SettingsDocumentCodec.Serialize(settings));
        var paths = new GameDataPaths(directory.DataRoot, targetId);
        var checkpoint = new DownloadCheckpointStore(paths);
        var transport = new StubRemoteHttpTransport();
        var context = CreateContext(target, checkpoint, new LauncherSettingsService(directory.DataRoot), transport: transport);
        using (var session = DownloadSessionFactory.Create(context, DownloadSnapshot(targetId, settings),
            DownloadOperationProfile.ForDownload(), _ => { }, CancellationToken.None))
        {
            var result = await session.RunAsync();
            Assert.False(result.Success);
            Assert.Equal(new LocalizationService().T(LocalizationKeys.GamePathConflictsWithAnotherGame), result.Message);
        }
        Assert.False(File.Exists(paths.DownloadStatePath));
        await checkpoint.SaveAsync(IdentifiedState(targetId, gamePath));
        var checkpointBytes = await File.ReadAllBytesAsync(paths.DownloadStatePath);

        using var resumed = await DownloadSessionFactory.TryCreateForResumeAsync(context,
            DownloadSnapshot(targetId, settings), _ => { }, CancellationToken.None);

        Assert.Null(resumed);
        Assert.Equal(checkpointBytes, await File.ReadAllBytesAsync(paths.DownloadStatePath));
        Assert.Equal(original, await File.ReadAllBytesAsync(sentinel));
        Assert.Single(Directory.GetFiles(gamePath, "*", SearchOption.AllDirectories));
        Assert.Empty(transport.RequestedUris);
    }

    [Fact]
    public async Task Resume_IdentifiedStellaCheckpoint_UsesItsSnapshotSettingsInsteadOfBlueArchiveAliases()
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.StellaSoraChina.RuntimeId);
        var store = new DownloadCheckpointStore(paths);
        var gamePath = new GameInstallationPath(LauncherProfiles.StellaSoraChina).NormalizeGamePath(directory.Sub("stella-install"));
        var settings = new LauncherSettings { GamePath = directory.Sub("ba-install"), PatchUrlGroup = PatchUrlGroups.Cafe };
        settings.GetGameSettings(paths.GameId).GamePath = gamePath;
        var snapshot = DownloadSnapshot(paths.GameId, settings);
        await store.SaveAsync(IdentifiedState(paths.GameId, gamePath));
        var context = CreateContext(LauncherProfiles.StellaSoraChina, store);

        using var session = await DownloadSessionFactory.TryCreateForResumeAsync(context, snapshot, _ => { }, CancellationToken.None);

        Assert.NotNull(session);
        Assert.True(File.Exists(paths.DownloadStatePath));
        Assert.Equal(PatchUrlGroups.Cafe, snapshot.Settings.PatchUrlGroup);
        Assert.Equal(PatchUrlGroups.Official, snapshot.GameSettings.PatchUrlGroup);
    }

    [Fact]
    public async Task Resume_ContextSnapshotIdentityMismatch_DoesNotConsumeTheCheckpoint()
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.StellaSoraChina.RuntimeId);
        var store = new DownloadCheckpointStore(paths);
        await store.SaveAsync(IdentifiedState(paths.GameId, directory.Sub("stella")));
        var original = await File.ReadAllBytesAsync(paths.DownloadStatePath);
        var context = CreateContext(LauncherProfiles.StellaSoraChina, store);

        var result = await DownloadSessionFactory.TryCreateForResumeAsync(context,
            DownloadSnapshot(LauncherProfiles.BlueArchiveJapan.RuntimeId, new LauncherSettings()), _ => { }, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(original, await File.ReadAllBytesAsync(paths.DownloadStatePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resume_LegacyRootCheckpoint_RequiresVerifiedBlueArchiveInstallation(bool installed)
    {
        using var directory = TestDirectory.Create();
        var store = new DownloadCheckpointStore(directory.DataRoot);
        var gamePath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan).NormalizeGamePath(directory.Sub("install"));
        var localStore = new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan);
        if (installed)
        {
            Directory.CreateDirectory(gamePath);
            await localStore.CommitAsync(gamePath, new LocalInstallationStateCommit("0.9", "old-basis", "test-entry", [], []));
        }

        var legacy = new DownloadTaskState { Version = "1.0", Basis = "new-basis", GamePath = gamePath };
        await store.SaveAsync(legacy);
        var original = await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath);
        var context = CreateContext(LauncherProfiles.BlueArchiveJapan, store, localInstallationStateStore: localStore);

        using var session = await DownloadSessionFactory.TryCreateForResumeAsync(context,
            DownloadSnapshot(LauncherProfiles.BlueArchiveJapan.RuntimeId, new LauncherSettings { GamePath = gamePath }), _ => { }, CancellationToken.None);

        Assert.Equal(installed, session is not null);
        Assert.Equal(original, await File.ReadAllBytesAsync(directory.DataRoot.DownloadStatePath));
    }

    [Fact]
    public async Task Run_StellaSession_WritesOnlyItsPathAndExplicitCheckpointIdentity()
    {
        using var directory = TestDirectory.Create();
        var paths = new GameDataPaths(directory.DataRoot, LauncherProfiles.StellaSoraChina.RuntimeId);
        var store = new DownloadCheckpointStore(paths);
        var baPath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan).NormalizeGamePath(directory.Sub("ba-install"));
        var stellaPath = new GameInstallationPath(LauncherProfiles.StellaSoraChina).NormalizeGamePath(directory.Sub("stella-install"));
        var settings = new LauncherSettings { GamePath = baPath, PatchUrlGroup = PatchUrlGroups.Cafe };
        settings.GetGameSettings(paths.GameId).GamePath = stellaPath;
        var transport = new StubRemoteHttpTransport(uri => uri.PathAndQuery.Contains("/game/config/json", StringComparison.Ordinal)
            ? """{"code":200,"data":{"url":"https://manifest.example.invalid/manifest.json"}}"""
            : """{"source":"/synthetic-offline-test","file":[]}""");
        var context = CreateContext(LauncherProfiles.StellaSoraChina, store, new FixedSettingsService(settings), transport: transport);
        DownloadTaskState? checkpoint = null;
        using var session = DownloadSessionFactory.Create(context, DownloadSnapshot(paths.GameId, settings),
            DownloadOperationProfile.ForDownload(), _ =>
            {
                if (File.Exists(paths.DownloadStatePath))
                {
                    checkpoint = JsonSerializer.Deserialize<DownloadTaskState>(File.ReadAllText(paths.DownloadStatePath));
                }
            }, CancellationToken.None);

        var result = await session.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.NotNull(checkpoint);
        Assert.Equal(2, checkpoint.FormatVersion);
        Assert.Equal(paths.GameId, checkpoint.GameId);
        Assert.Equal(stellaPath, checkpoint.GamePath);
        Assert.Equal(PatchUrlGroups.Official, checkpoint.PatchUrlGroup);
        Assert.False(Directory.Exists(baPath));
        var local = await context.ILocalInstallationStateStore.ReadAsync(stellaPath);
        Assert.Equal(LocalInstallationStateKind.Valid, local.Kind);
        Assert.Equal(LauncherProfiles.StellaSoraChina.Tag, local.GameConfig?.Tag);
        Assert.DoesNotContain(transport.RequestedUris, uri => uri.Host == "api-launcher-jp.yo-star.com");
    }

    [Theory]
    [InlineData(PatchUrlGroups.Official, PatchUrlGroups.Cafe)]
    [InlineData(PatchUrlGroups.Cafe, PatchUrlGroups.Official)]
    public async Task Run_WhenSourceChangedSinceSnapshot_UsesMatchingCdn(string previousSource, string savedSource)
    {
        using var directory = TestDirectory.Create();
        var settings = new LauncherSettings
        {
            GamePath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan).NormalizeGamePath(directory.Sub("install")),
            PatchUrlGroup = savedSource
        };
        var officialCdn = LauncherProfiles.BlueArchiveJapan.OfficialPackageBaseUrl;
        var transport = new StubRemoteHttpTransport(uri => uri.AbsolutePath switch
        {
            "/api/launcher/advanced/game/download/cdn" => JsonSerializer.Serialize(new { code = 200, data = new CdnConfigResponse { PrimaryCdn = officialCdn, BackUpCdn = officialCdn } }),
            "/api/launcher/game/config/json" => """{"code":200,"data":{"url":"https://manifest.example.invalid/manifest.json"}}""",
            _ => """{"source":"/synthetic-offline-test","file":[{"path":"probe.bin","size":"1","hash":"0"}]}"""
        });
        var checkpoint = new DownloadCheckpointStore(new GameDataPaths(directory.DataRoot, LauncherProfiles.BlueArchiveJapan.RuntimeId));
        var context = CreateContext(LauncherProfiles.BlueArchiveJapan, checkpoint, new FixedSettingsService(settings), transport: transport);
        var downloads = new StubFileDownloadService((_, _, _) => throw new IOException("Stop before downloading files."));
        context = context with { FileDownloadService = downloads };
        var previous = settings.DeepClone();
        previous.PatchUrlGroup = previousSource;
        var snapshot = DownloadSnapshot(LauncherProfiles.BlueArchiveJapan.RuntimeId, previous);
        snapshot.Remote.CdnConfig = new CdnConfigResponse { PrimaryCdn = "https://old.example.invalid", BackUpCdn = "https://old-backup.example.invalid" };
        using var session = DownloadSessionFactory.Create(context, snapshot, DownloadOperationProfile.ForDownload(), _ => { }, CancellationToken.None);

        await session.RunAsync();

        var request = Assert.Single(downloads.Requests);
        Assert.Equal(savedSource == PatchUrlGroups.Official ? officialCdn : "https://" + LauncherProfiles.Cafe.CafePackageHost, request.CdnConfig.PrimaryCdn);
        Assert.Contains(transport.RequestedUris, uri => uri.AbsolutePath == "/api/launcher/advanced/game/download/cdn");
    }

    private static LauncherStatusSnapshot DownloadSnapshot(string gameId, LauncherSettings settings) => new()
    {
        GameId = gameId,
        Settings = settings,
        Remote = new LauncherRemoteState
        {
            GameConfig = new GameConfigResponse { GameLatestVersion = "1.0", GameLatestFilePath = "new-basis", GameStartExeName = "test-entry" },
            CdnConfig = new CdnConfigResponse { PrimaryCdn = "https://cdn.example.invalid", BackUpCdn = "https://backup.example.invalid" }
        }
    };

    private static DownloadTaskState IdentifiedState(string gameId, string gamePath) => new()
    {
        FormatVersion = 2, GameId = gameId, Version = "1.0", Basis = "new-basis", GamePath = gamePath
    };

    private static (LocalInstallationState State, GameConfigResponse GameConfig, List<ManifestFile> Files)
        CreateMatchingStateFixture(
            LocalInstallationStateKind kind = LocalInstallationStateKind.Valid)
    {
        // 提交侧与本地状态侧使用独立的列表实例，避免用例原地修改时互相污染。
        var files = new List<ManifestFile>
        {
            new() { Path = "bin/game.exe", Hash = "123", Size = "10" },
            new() { Path = "data/pak.zip", Hash = "456", Size = "20" },
        };
        var state = new LocalInstallationState
        {
            Kind = kind,
            GameConfig = new GameLauncherConfig
            {
                Tag = LauncherProfiles.BlueArchiveJapan.Tag,
                Name = "BlueArchiveOnline_JP",
                Params = ["-op", "full"],
                Version = "1.72.0",
            },
            Manifest = new LocalManifest
            {
                Name = LauncherProfiles.BlueArchiveJapan.Tag,
                Version = "1.72.0",
                Basis = "basis-1",
                Files =
                [
                    new() { Path = "bin/game.exe", Hash = "123", Size = "10" },
                    new() { Path = "data/pak.zip", Hash = "456", Size = "20" },
                ],
            },
        };
        var gameConfig = new GameConfigResponse
        {
            GameLatestVersion = "1.72.0",
            GameLatestFilePath = "basis-1",
            GameStartExeName = "BlueArchiveOnline_JP",
            GameStartParams = ["-op", "full"],
        };
        return (state, gameConfig, files);
    }

    private static DownloadSession CreateSession() => new(
        CreateContext(), new LauncherStatusSnapshot { Remote = new LauncherRemoteState() },
        DownloadOperationProfile.ForDownload(), _ => { }, CancellationToken.None);

    private static DownloadSessionContext CreateContext(YostarGameProfile? gameProfile = null,
        DownloadCheckpointStore? checkpointStore = null, ILauncherSettingsService? settingsService = null,
        ILocalInstallationStateStore? localInstallationStateStore = null, StubRemoteHttpTransport? transport = null)
    {
        gameProfile ??= LauncherProfiles.BlueArchiveJapan;
        var diagnostics = new LocalDiagnostics();
        var apiClient = new LauncherApiClient(gameProfile,
            transport ?? new StubRemoteHttpTransport(),
            new AuthorizationHeaderFactory(gameProfile),
            new PatchUrlGroupService(gameProfile, LauncherProfiles.Cafe));
        return new DownloadSessionContext(gameProfile,
            apiClient,
            new RemoteManifestService(apiClient),
            new FileDownloadService(new Crc64Service(), diagnostics),
            new StubDownloadTransportSource(),
            new Crc64Service(),
            localInstallationStateStore ?? new LocalInstallationStateStore(gameProfile),
            settingsService ?? new LauncherSettingsService(TestDataRoot.ForCurrentProcess()),
            new DiskSpaceService(),
            diagnostics,
            new LocalizationService(),
            new GameInstallationPath(gameProfile),
            checkpointStore ?? new DownloadCheckpointStore(TestDataRoot.ForCurrentProcess()),
            TestGameProcessTracker.None());
    }

    private sealed class FixedSettingsService(LauncherSettings settings) : ILauncherSettingsService
    {
        public Task<LauncherSettings> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings.DeepClone());
    }
}
