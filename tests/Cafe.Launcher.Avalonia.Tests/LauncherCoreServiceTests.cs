using System.Net;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;

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
        var transport = CreateLauncherStateTransport("/api/launcher/game/config");
        var service = await CreateServiceAsync(transport);

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
        var transport = CreateLauncherStateTransport("/api/launcher/operations/resource");
        var service = await CreateServiceAsync(transport);

        var snapshot = await service.LoadAsync();

        Assert.Equal(LauncherRuntimeState.Ready, snapshot.RuntimeState);
        Assert.NotNull(snapshot.Remote.GameConfig);
        Assert.Null(snapshot.Remote.OperationsResource);
        Assert.NotNull(snapshot.Remote.SocialMediaResource);
    }

    [Fact]
    public async Task LoadAsync_WhenCancellationIsRequested_PropagatesCancellation()
    {
        var service = await CreateServiceAsync(new StubRemoteHttpTransport(
            _ => new OperationCanceledException("simulated canceled remote read")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.LoadAsync(cancellation.Token));
    }

    [Fact]
    public async Task LoadAsync_WhenRemoteReadsKeepFailing_DegradesWithinBudgetWithoutCancellation()
    {
        // 守卫（启动预算）：远端读取持续失败（真实传输下表现为 3×30s 超时 + 退避，
        // 最坏 ~92s 才降级）时，整体预算到点后快照必须以降级态返回，而不是把
        // 失败抛出或继续挂在调用方 token 上。调用方 token 全程未取消。
        var service = await CreateServiceAsync(
            new StubRemoteHttpTransport(_ => new TaskCanceledException("simulated stalled remote read")),
            remoteStateBudget: TimeSpan.FromMilliseconds(250));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var snapshot = await service.LoadAsync();

        stopwatch.Stop();
        Assert.Equal(LauncherRuntimeState.RemoteUnavailable, snapshot.RuntimeState);
        Assert.Null(snapshot.Remote.GameConfig);
        Assert.True(snapshot.Remote.BaseConfig is null);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task LoadAsync_WhenSettingsDocumentHasNoGamePath_ReturnsEffectiveDefaultGamePath()
    {
        var service = await CreateServiceAsync(
            CreateLauncherStateTransport("/api/launcher/never"),
            useEmptySettingsDocument: true);
        var expectedPath = new GameInstallationPath().GetDefaultGamePath();

        var snapshot = await service.LoadAsync();

        Assert.Equal(expectedPath, snapshot.Settings.GamePath);
        Assert.Equal(expectedPath, snapshot.LocalGame.GamePath);
    }

    private async Task<LauncherCoreService> CreateServiceAsync(
        StubRemoteHttpTransport transport,
        bool useEmptySettingsDocument = false,
        TimeSpan? remoteStateBudget = null)
    {
        var store = new LocalInstallationStateStore();
        var settingsPath = Path.Combine(tempDir, "settings.json");
        var settingsService = new LauncherSettingsService( TestDataRoot.ForFile(settingsPath) );
        if (useEmptySettingsDocument)
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(settingsPath, "{}");
        }
        else
        {
            var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
            Directory.CreateDirectory(gamePath);
            var committed = await store.CommitAsync(
                gamePath,
                new LocalInstallationStateCommit(
                    "2.0.0",
                    "manifest.json",
                    "BlueArchive",
                    [],
                    []));
            Assert.Equal(LocalInstallationStateKind.Valid, committed.Kind);
            await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        }

        var apiClient = new LauncherApiClient(
            transport,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());
        return new LauncherCoreService(
            apiClient,
            store,
            new GameInstallationPath(),
            settingsService,
            new LocalDiagnostics(),
            remoteStateBudget ?? LauncherCoreService.DefaultRemoteStateBudget);
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

    /// <summary>
    /// 按 URL 应答的状态快照替身：<paramref name="failingPath"/> 上的请求以 500 形态
    /// 失败，其余端点返回 code 200 的空数据 envelope（game/config 返回完整版本数据）。
    /// </summary>
    private static StubRemoteHttpTransport CreateLauncherStateTransport(string failingPath) =>
        new(uri =>
        {
            if (uri.AbsolutePath == failingPath)
            {
                return new HttpRequestException(
                    $"simulated server failure for {failingPath}",
                    null,
                    HttpStatusCode.InternalServerError);
            }

            var data = uri.AbsolutePath switch
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
            return $$"""{"code":200,"data":{{data}}}""";
        });
}
