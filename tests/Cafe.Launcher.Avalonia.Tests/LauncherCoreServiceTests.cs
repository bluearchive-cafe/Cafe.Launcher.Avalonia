using System.Net;
using System.Net.Http;
using System.Text;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherCoreServiceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(LocalInstallationStateKind.NotInstalled, LauncherRuntimeState.NotInstalled)]
    [InlineData(LocalInstallationStateKind.Corrupted, LauncherRuntimeState.Corrupted)]
    [InlineData(LocalInstallationStateKind.IoFailure, LauncherRuntimeState.IoFailure)]
    public void ResolveRuntimeState_WhenLocalStateIsNotValid_PreservesLocalClassification(
        LocalInstallationStateKind localKind,
        LauncherRuntimeState expected)
    {
        var state = LauncherCoreService.ResolveRuntimeState(
            new LocalInstallationState { Kind = localKind },
            CreateGameConfig());

        Assert.Equal(expected, state);
    }

    [Theory]
    [InlineData(null, LauncherRuntimeState.Corrupted)]
    [InlineData("0.9.0", LauncherRuntimeState.BelowLowestVersion)]
    [InlineData("1.5.0", LauncherRuntimeState.UpdateAvailable)]
    [InlineData("2.0.0", LauncherRuntimeState.Ready)]
    public void ResolveRuntimeState_WhenLocalStateIsValid_UsesRemoteVersionPriority(
        string? localVersion,
        LauncherRuntimeState expected)
    {
        var state = LauncherCoreService.ResolveRuntimeState(
            new LocalInstallationState
            {
                Kind = LocalInstallationStateKind.Valid,
                GameConfig = new GameLauncherConfig { Version = localVersion }
            },
            CreateGameConfig());

        Assert.Equal(expected, state);
    }

    [Fact]
    public async Task LoadAsync_WhenGameConfigFails_PreservesLocalAndOtherRemoteState()
    {
        var handler = new LauncherStateHandler("/api/launcher/game/config");
        var service = await CreateServiceAsync(handler);

        var snapshot = await service.LoadAsync();

        Assert.Equal(LauncherRuntimeState.RemoteUnavailable, snapshot.RuntimeState);
        Assert.Equal(LocalInstallationStateKind.Valid, snapshot.LocalGame.Kind);
        Assert.Null(snapshot.Remote.GameConfig);
        Assert.NotNull(snapshot.Remote.BaseConfig);
        Assert.NotNull(snapshot.Remote.CdnConfig);
        Assert.NotNull(snapshot.Remote.OperationsResource);
        Assert.NotNull(snapshot.Remote.SocialMediaResource);
        Assert.NotNull(snapshot.Remote.InstallationConfig);
    }

    [Fact]
    public async Task LoadAsync_WhenOptionalRemoteCallFails_RemainsReady()
    {
        var handler = new LauncherStateHandler("/api/launcher/operations/resource");
        var service = await CreateServiceAsync(handler);

        var snapshot = await service.LoadAsync();

        Assert.Equal(LauncherRuntimeState.Ready, snapshot.RuntimeState);
        Assert.NotNull(snapshot.Remote.GameConfig);
        Assert.Null(snapshot.Remote.OperationsResource);
        Assert.NotNull(snapshot.Remote.SocialMediaResource);
    }

    [Fact]
    public async Task LoadAsync_WhenCancellationIsRequested_PropagatesCancellation()
    {
        var service = await CreateServiceAsync(new CancellationHandler());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.LoadAsync(cancellation.Token));
    }

    private async Task<LauncherCoreService> CreateServiceAsync(HttpMessageHandler handler)
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        var store = new LocalInstallationStateStore();
        var committed = await store.CommitAsync(
            gamePath,
            new LocalInstallationStateCommit(
                "2.0.0",
                "manifest.json",
                "BlueArchive",
                [],
                []));
        Assert.Equal(LocalInstallationStateKind.Valid, committed.Kind);

        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        var apiClient = new LauncherApiClient(
            handler,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());
        return new LauncherCoreService(
            apiClient,
            store,
            new GameInstallationPath(),
            settingsService,
            new LocalDiagnostics());
    }

    private static GameConfigResponse CreateGameConfig()
    {
        return new GameConfigResponse
        {
            GameLowestVersion = "1.0.0",
            GameLatestVersion = "2.0.0"
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class LauncherStateHandler : HttpMessageHandler
    {
        private readonly string failingPath;

        public LauncherStateHandler(string failingPath)
        {
            this.failingPath = failingPath;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path == failingPath)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            var data = path switch
            {
                "/api/launcher/game/config" => """
                    {
                      "game_latest_version": "2.0.0",
                      "game_latest_file_path": "manifest.json",
                      "game_start_exe_name": "BlueArchive",
                      "game_start_params": [],
                      "game_lowest_version": "1.0.0"
                    }
                    """,
                _ => "{}"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"code":200,"data":{{data}}}""",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
