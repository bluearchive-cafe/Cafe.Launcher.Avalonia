using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DownloadSessionTests
{
    [Fact]
    public void EnsureGamePath_WhenFolderNameMatches_Passes()
    {
        var path = Path.Combine(Path.GetTempPath(), GamePaths.GameFolderName);

        DownloadSession.EnsureGamePath(path);
    }

    [Fact]
    public void EnsureGamePath_WhenFolderNameDiffers_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), "NotTheGameFolder");

        Assert.Throws<InvalidOperationException>(() => DownloadSession.EnsureGamePath(path));
    }

    [Fact]
    public void Failed_MapsMessageErrorCodeAndCounts()
    {
        var result = DownloadSession.Failed("failed", GameOperationErrorCode.Network, affectedFileCount: 3, failedFileCount: 2);

        Assert.False(result.Success);
        Assert.Equal("failed", result.Message);
        Assert.Equal(GameOperationErrorCode.Network, result.ErrorCode);
        Assert.Equal(3, result.AffectedFileCount);
        Assert.Equal(2, result.FailedFileCount);
    }

    [Fact]
    public void CreateProgress_ForDownload_EnablesStopAndDisablesPause()
    {
        var progress = DownloadSession.CreateProgress(GameOperationKind.Download, GameOperationStage.UpdateCheck, 25);

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
        session.Stop();
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

        Assert.True(DownloadSession.LocalInstallationStateMatchesCommit(state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_NotValidState_ReturnsFalse()
    {
        var (state, gameConfig, files) =
            CreateMatchingStateFixture(LocalInstallationStateKind.Corrupted);

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_VersionDiffers_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        gameConfig.GameLatestVersion = "1.73.0";

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_BasisDiffers_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        gameConfig.GameLatestFilePath = "basis-2";

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_ExeNameDiffers_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        gameConfig.GameStartExeName = "Other.exe";

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_LaunchParamsDiffer_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        gameConfig.GameStartParams = ["-op", "lite"];

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_FileCountDiffers_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        files.RemoveAt(files.Count - 1);

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_FileHashDiffers_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        state.Manifest!.Files[0].Hash = "999";

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(state, gameConfig, files));
    }

    [Fact]
    public void LocalInstallationStateMatchesCommit_FileSizeDiffers_ReturnsFalse()
    {
        var (state, gameConfig, files) = CreateMatchingStateFixture();
        state.Manifest!.Files[0].Size = "11";

        Assert.False(DownloadSession.LocalInstallationStateMatchesCommit(state, gameConfig, files));
    }

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
                Tag = GamePaths.GameTag,
                Name = "BlueArchiveOnline_JP",
                Params = ["-op", "full"],
                Version = "1.72.0",
            },
            Manifest = new LocalManifest
            {
                Name = GamePaths.GameTag,
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

    private static DownloadSession CreateSession()
    {
        var diagnostics = new LocalDiagnostics();
        var apiClient = new LauncherApiClient(
            new HttpClientHandler(),
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());
        using var httpClientFactory = new HttpClientFactory(new ProxySettingsService());
        var context = new DownloadSessionContext(
            apiClient,
            new RemoteManifestService(apiClient),
            new FileDownloadService(new Crc64Service(), diagnostics, RemoteHttpUrlValidator.CreateForTesting()),
            new ProxyAwareHttpClientLeaseSource(httpClientFactory, new Uri(ApiConfig.ApiBaseUrl), TimeSpan.FromSeconds(30)),
            new Crc64Service(),
            new LocalInstallationStateStore(),
            new LauncherSettingsService(),
            new DiskSpaceService(),
            diagnostics,
            new LocalizationService(),
            new GameInstallationPath(),
            new DownloadCheckpointStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "checkpoint.json")),
            new GameProcessTracker());
        return new DownloadSession(
            context,
            new LauncherStatusSnapshot { Remote = new LauncherRemoteState() },
            repair: false,
            _ => { },
            CancellationToken.None);
    }
}
