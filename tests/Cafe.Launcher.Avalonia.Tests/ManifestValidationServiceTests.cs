using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class ManifestValidationServiceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    static ManifestValidationServiceTests()
    {
        TestLocalizationHelper.Initialize();
    }

    public ManifestValidationServiceTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public async Task ValidateAsync_WhenCheckModeIsNone_SucceedsWithoutInstallationState()
    {
        var apiClient = CreateApiClient(new StubRemoteHttpTransport());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            new LocalInstallationState(),
            LaunchCheckModes.None,
            PatchUrlGroups.Official);

        Assert.True(result.Success);
        Assert.Equal(new LocalizationService().T(LocalizationKeys.LaunchCheckSkipped), result.Message);
    }

    [Fact]
    public async Task ValidateAsync_WhenLocalFilesMatch_Succeeds()
    {
        var filePath = Path.Combine(tempDir, "data.bin");
        await File.WriteAllTextAsync(filePath, "1234");
        var apiClient = CreateApiClient(new StubRemoteHttpTransport());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            CreateLocalState(new ManifestFile { Path = "data.bin", Size = "4" }),
            LaunchCheckModes.LocalManifest,
            PatchUrlGroups.Official);

        Assert.True(result.Success);
        Assert.Equal(0, result.DamagedFileCount);
        Assert.False(result.HasDamagedFiles);
    }

    [Fact]
    public async Task ValidateAsync_WhenLocalFilesAreMissingOrWrongSize_ReturnsExactCounts()
    {
        await File.WriteAllTextAsync(Path.Combine(tempDir, "wrong.bin"), "1");
        var apiClient = CreateApiClient(new StubRemoteHttpTransport());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            CreateLocalState(
                new ManifestFile { Path = "missing.bin", Size = "4" },
                new ManifestFile { Path = "wrong.bin", Size = "4" }),
            LaunchCheckModes.LocalManifest,
            PatchUrlGroups.Official);

        Assert.False(result.Success);
        Assert.Equal(2, result.DamagedFileCount);
        Assert.Equal(1, result.MissingFileCount);
        Assert.Equal(1, result.SizeMismatchFileCount);
        Assert.True(result.HasDamagedFiles);
    }

    [Fact]
    public async Task ValidateAsync_WhenLocalInstallationIsNotInstalled_ReturnsFailure()
    {
        var apiClient = CreateApiClient(new StubRemoteHttpTransport());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            new LocalInstallationState
            {
                Kind = LocalInstallationStateKind.NotInstalled,
                ManifestPath = Path.Combine(tempDir, "manifest.json")
            },
            LaunchCheckModes.LocalManifest,
            PatchUrlGroups.Official);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_WhenLocalManifestIsUnreadable_ReturnsFailure()
    {
        var apiClient = CreateApiClient(new StubRemoteHttpTransport());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            new LocalInstallationState
            {
                Kind = LocalInstallationStateKind.Corrupted,
                ManifestPath = Path.Combine(tempDir, "manifest.json")
            },
            LaunchCheckModes.LocalManifest,
            PatchUrlGroups.Official);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_WhenRemoteMetadataIsMissing_ReturnsFailure()
    {
        var apiClient = CreateApiClient(new StubRemoteHttpTransport());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            CreateLocalState(),
            LaunchCheckModes.RemoteManifest,
            PatchUrlGroups.Official);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_WhenRemoteManifestMatches_Succeeds()
    {
        await File.WriteAllTextAsync(Path.Combine(tempDir, "remote.bin"), "1234");
        var apiClient = CreateApiClient(new StubRemoteHttpTransport(uri =>
            uri.AbsolutePath.Contains("/api/launcher/game/config/json", StringComparison.Ordinal)
                ? """{"code":200,"data":{"url":"https://manifest.example.invalid/manifest.json"}}"""
                : "{\"source\":\"\",\"file\":[{\"path\":\"remote.bin\",\"size\":\"4\",\"hash\":\"0\"}]}"));
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            CreateRemoteLocalState(),
            LaunchCheckModes.RemoteManifest,
            PatchUrlGroups.Official);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_WhenRemoteManifestUrlIsEmpty_AllowsLaunch()
    {
        // Fail open like the official launcher: an unobtainable remote manifest must not
        // block launch, otherwise it deadlocks against a repair that targets the latest.
        var apiClient = CreateApiClient(new StubRemoteHttpTransport(uri =>
            uri.AbsolutePath.Contains("/api/launcher/game/config/json", StringComparison.Ordinal)
                ? """{"code":200,"data":{"url":""}}"""
                : "{}"));
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            CreateRemoteLocalState(),
            LaunchCheckModes.RemoteManifest,
            PatchUrlGroups.Official);

        Assert.True(result.Success);
        Assert.Equal(new LocalizationService().T(LocalizationKeys.LaunchCheckRemoteUnavailable), result.Message);
        Assert.False(result.HasDamagedFiles);
    }

    [Fact]
    public async Task ValidateAsync_WhenRemoteRequestFails_AllowsLaunch()
    {
        // Network failure (or a de-listed local-basis build) must fail open, not block launch.
        var apiClient = CreateApiClient(new StubRemoteHttpTransport(
            _ => new HttpRequestException("network failure")));
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            CreateRemoteLocalState(),
            LaunchCheckModes.RemoteManifest,
            PatchUrlGroups.Official);

        Assert.True(result.Success);
        Assert.Equal(new LocalizationService().T(LocalizationKeys.LaunchCheckRemoteUnavailable), result.Message);
    }

    private static LocalInstallationState CreateLocalState(params ManifestFile[] files) =>
        new()
        {
            Kind = LocalInstallationStateKind.Valid,
            Manifest = new LocalManifest { Files = files.ToList() }
        };

    private static LocalInstallationState CreateRemoteLocalState() =>
        new()
        {
            Kind = LocalInstallationStateKind.Valid,
            Manifest = new LocalManifest
            {
                Version = "1.0.0",
                Basis = "manifest.json"
            }
        };

    private static LauncherApiClient CreateApiClient(IRemoteHttpTransport transport) =>
        new(transport, new AuthorizationHeaderFactory(), new PatchUrlGroupService());

    private static ManifestValidationService CreateService(LauncherApiClient apiClient)
    {
        var localizer = new LocalizationService();
        return new ManifestValidationService(
            apiClient,
            new RemoteManifestService(apiClient),
            localizer);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
