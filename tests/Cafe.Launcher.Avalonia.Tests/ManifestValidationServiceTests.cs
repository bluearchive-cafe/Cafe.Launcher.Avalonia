using System.Net;
using System.Net.Http;
using System.Text;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;

namespace Cafe.Launcher.Avalonia.Tests;

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
        using var apiClient = CreateApiClient(new HttpClientHandler());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            new LocalInstallationState(),
            LaunchCheckModes.None,
            PatchUrlGroups.Official,
            ProxyModes.Direct);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_WhenLocalFilesMatch_Succeeds()
    {
        var filePath = Path.Combine(tempDir, "data.bin");
        await File.WriteAllTextAsync(filePath, "1234");
        using var apiClient = CreateApiClient(new HttpClientHandler());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            CreateLocalState(new ManifestFile { Path = "data.bin", Size = "4" }),
            LaunchCheckModes.LocalManifest,
            PatchUrlGroups.Official,
            ProxyModes.Direct);

        Assert.True(result.Success);
        Assert.Equal(0, result.DamagedFileCount);
    }

    [Fact]
    public async Task ValidateAsync_WhenLocalFilesAreMissingOrWrongSize_ReturnsExactCounts()
    {
        await File.WriteAllTextAsync(Path.Combine(tempDir, "wrong.bin"), "1");
        using var apiClient = CreateApiClient(new HttpClientHandler());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            CreateLocalState(
                new ManifestFile { Path = "missing.bin", Size = "4" },
                new ManifestFile { Path = "wrong.bin", Size = "4" }),
            LaunchCheckModes.LocalManifest,
            PatchUrlGroups.Official,
            ProxyModes.Direct);

        Assert.False(result.Success);
        Assert.Equal(2, result.DamagedFileCount);
        Assert.Equal(1, result.MissingFileCount);
        Assert.Equal(1, result.SizeMismatchFileCount);
    }

    [Fact]
    public async Task ValidateAsync_WhenLocalInstallationIsNotInstalled_ReturnsFailure()
    {
        using var apiClient = CreateApiClient(new HttpClientHandler());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            new LocalInstallationState
            {
                Kind = LocalInstallationStateKind.NotInstalled,
                ManifestPath = Path.Combine(tempDir, "manifest.json")
            },
            LaunchCheckModes.LocalManifest,
            PatchUrlGroups.Official,
            ProxyModes.Direct);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_WhenLocalManifestIsUnreadable_ReturnsFailure()
    {
        using var apiClient = CreateApiClient(new HttpClientHandler());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            new LocalInstallationState
            {
                Kind = LocalInstallationStateKind.Corrupted,
                ManifestPath = Path.Combine(tempDir, "manifest.json")
            },
            LaunchCheckModes.LocalManifest,
            PatchUrlGroups.Official,
            ProxyModes.Direct);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_WhenRemoteMetadataIsMissing_ReturnsFailure()
    {
        using var apiClient = CreateApiClient(new HttpClientHandler());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            CreateLocalState(),
            LaunchCheckModes.RemoteManifest,
            PatchUrlGroups.Official,
            ProxyModes.Direct);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_WhenRemoteManifestMatches_Succeeds()
    {
        await File.WriteAllTextAsync(Path.Combine(tempDir, "remote.bin"), "1234");
        using var apiClient = CreateApiClient(new RemoteManifestHandler(
            "https://manifest.example.invalid/manifest.json",
            "{\"source\":\"\",\"file\":[{\"path\":\"remote.bin\",\"size\":\"4\",\"hash\":\"0\"}]}"));
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            CreateRemoteLocalState(),
            LaunchCheckModes.RemoteManifest,
            PatchUrlGroups.Official,
            ProxyModes.Direct);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_WhenRemoteManifestUrlIsEmpty_ReturnsFailure()
    {
        using var apiClient = CreateApiClient(new RemoteManifestHandler("", "{}"));
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            CreateRemoteLocalState(),
            LaunchCheckModes.RemoteManifest,
            PatchUrlGroups.Official,
            ProxyModes.Direct);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_WhenRemoteRequestFails_ReturnsFailure()
    {
        using var apiClient = CreateApiClient(new ThrowingHandler());
        var service = CreateService(apiClient);

        var result = await service.ValidateAsync(
            tempDir,
            CreateRemoteLocalState(),
            LaunchCheckModes.RemoteManifest,
            PatchUrlGroups.Official,
            ProxyModes.Direct);

        Assert.False(result.Success);
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

    private static LauncherApiClient CreateApiClient(HttpMessageHandler handler) =>
        new(handler, new AuthorizationHeaderFactory(), new PatchUrlGroupService());

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

    private sealed class RemoteManifestHandler(
        string manifestUrl,
        string manifestJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var isUrlRequest = request.RequestUri?.AbsolutePath.Contains(
                "/api/launcher/game/config/json",
                StringComparison.Ordinal) == true;
            var json = isUrlRequest
                ? $"{{\"code\":200,\"data\":{{\"url\":\"{manifestUrl}\"}}}}"
                : manifestJson;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("network failure");
    }
}
