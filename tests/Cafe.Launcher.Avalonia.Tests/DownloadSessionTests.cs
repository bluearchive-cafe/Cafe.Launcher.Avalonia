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
