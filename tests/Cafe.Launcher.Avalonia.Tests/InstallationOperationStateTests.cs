using System.Net.Http;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class InstallationOperationStateTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    static InstallationOperationStateTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public async Task StartAsync_WhenInstallationStateIsCorrupted_BlocksLaunch()
    {
        using var apiClient = new LauncherApiClient(
            new HttpClientHandler(),
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());
        var localizer = new LocalizationService();
        var service = new GameLaunchService(
            new ManifestValidationService(apiClient, new RemoteManifestService(apiClient), localizer),
            new ClickCodeService(),
            localizer);

        var result = await service.StartAsync(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.Corrupted
        });

        Assert.False(result.Success);
        Assert.Equal(localizer.T("corruptedInstallationState"), result.Message);
    }

    [Fact]
    public async Task StartAsync_WhenInstallationIsReady_StartsConfiguredExecutable()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        const string executableName = "CafeLauncherProcessTest";
        File.Copy(
            Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is not configured."),
            Path.Combine(gamePath, $"{executableName}.exe"));
        var store = new LocalInstallationStateStore();
        var localGame = await store.CommitAsync(
            gamePath,
            new LocalInstallationStateCommit(
                "1.0.0",
                "manifest.json",
                executableName,
                ["/c", "exit", "0"],
                []));
        Assert.Equal(LocalInstallationStateKind.Valid, localGame.Kind);
        using var apiClient = new LauncherApiClient(
            new HttpClientHandler(),
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());
        var localizer = new LocalizationService();
        var service = new GameLaunchService(
            new ManifestValidationService(apiClient, new RemoteManifestService(apiClient), localizer),
            new ClickCodeService(),
            localizer);
        var snapshot = new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.Ready,
            LocalGame = localGame,
            Settings = new LauncherSettings { LaunchCheckMode = LaunchCheckModes.None }
        };

        var result = await service.StartAsync(snapshot);
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(executableName))
        {
            using (process)
            {
                await process.WaitForExitAsync();
            }
        }

        Assert.True(result.Success);
        Assert.True(result.Validation.Success);
    }

    [Fact]
    public async Task ValidateAsync_WhenInstallationStateIsCorrupted_BlocksUninstall()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        await File.WriteAllTextAsync(Path.Combine(gamePath, "manifest.json"), "{}");
        var service = new GameUninstallService(
            new LocalInstallationStateStore(),
            new LocalDiagnostics(),
            new LocalizationService(),
            new GameInstallationPath());

        var result = await service.ValidateAsync(gamePath);

        Assert.False(result.Success);
        Assert.Equal("uninstall-error", result.ErrorType);
    }

    [Fact]
    public async Task UninstallAsync_WhenInstallationStateIsValid_DeletesStateThroughStore()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        var managedPath = Path.Combine(gamePath, "data", "managed.bin");
        var unknownPath = Path.Combine(gamePath, "unknown.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(managedPath)!);
        await File.WriteAllTextAsync(managedPath, "managed");
        await File.WriteAllTextAsync(unknownPath, "unknown");
        var store = new LocalInstallationStateStore();
        var committed = await store.CommitAsync(
            gamePath,
            new LocalInstallationStateCommit(
                "1.0.0",
                "manifest.json",
                $"CafeLauncherTest{Guid.NewGuid():N}",
                [],
                [new LocalInstallationFile("data/managed.bin", new FileInfo(managedPath).Length, "0")]));
        Assert.Equal(LocalInstallationStateKind.Valid, committed.Kind);
        var service = new GameUninstallService(
            store,
            new LocalDiagnostics(),
            new LocalizationService(),
            new GameInstallationPath());
        var snapshot = new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.Ready,
            LocalGame = committed
        };

        var result = await service.UninstallAsync(snapshot, _ => { });
        var state = await store.ReadAsync(gamePath);

        Assert.True(result.Success);
        Assert.Equal(LocalInstallationStateKind.NotInstalled, state.Kind);
        Assert.False(File.Exists(managedPath));
        Assert.True(File.Exists(unknownPath));
    }

    [Fact]
    public async Task RepairAsync_WhenRuntimeStateDoesNotAllowRepair_ReturnsInvalidState()
    {
        using var apiClient = new LauncherApiClient(
            new HttpClientHandler(),
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());
        using var service = new GameDownloadService(
            new GameDownloadService.Dependencies(
                apiClient,
                new RemoteManifestService(apiClient),
                new FileDownloadService(
                    new Crc64Service(),
                    new LocalDiagnostics(),
                    RemoteHttpUrlValidator.CreateForTesting()),
                new LocalInstallationStateStore(),
                new LauncherSettingsService(Path.Combine(tempDir, "settings.json")),
                new ProxySettingsService(),
                new Crc64Service(),
                new DiskSpaceService(),
                new LocalDiagnostics(),
                new LocalizationService(),
                new GameInstallationPath()));

        var result = await service.RepairAsync(
            new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled },
            _ => { });

        Assert.False(result.Success);
        Assert.Equal("invalid-state", result.ErrorType);
    }

    [Fact]
    public async Task UninstallAsync_WhenRuntimeStateIsNotReady_ReturnsInvalidState()
    {
        var service = new GameUninstallService(
            new LocalInstallationStateStore(),
            new LocalDiagnostics(),
            new LocalizationService(),
            new GameInstallationPath());

        var result = await service.UninstallAsync(
            new LauncherStatusSnapshot
            {
                RuntimeState = LauncherRuntimeState.RemoteUnavailable
            },
            _ => { });

        Assert.False(result.Success);
        Assert.Equal("invalid-state", result.ErrorType);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
