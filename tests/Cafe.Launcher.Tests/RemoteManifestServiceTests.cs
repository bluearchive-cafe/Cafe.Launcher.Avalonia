using System.Net;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.Auth;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

public sealed class RemoteManifestServiceTests
{
    [Fact]
    public async Task GetRequiredManifestAsync_WhenUrlAndManifestAreValid_ReturnsManifest()
    {
        var apiClient = CreateApiClient(new StubRemoteHttpTransport(uri =>
            uri.AbsolutePath.Contains("/api/launcher/game/config/json", StringComparison.Ordinal)
                ? """{"code":200,"data":{"url":"https://manifest.example.invalid/latest.json"}}"""
                : "{\"source\":\"packages\",\"file\":[]}"));
        var service = new RemoteManifestService(apiClient);

        var result = await service.GetRequiredManifestAsync(
            "1.0.0",
            "manifest.json",
            PatchUrlGroups.Official);

        Assert.Equal("packages", result.Source);
    }

    [Fact]
    public async Task GetRequiredManifestAsync_WhenUrlIsEmpty_Throws()
    {
        var apiClient = CreateApiClient(new StubRemoteHttpTransport(uri =>
            uri.AbsolutePath.Contains("/api/launcher/game/config/json", StringComparison.Ordinal)
                ? """{"code":200,"data":{"url":""}}"""
                : "{}"));
        var service = new RemoteManifestService(apiClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetRequiredManifestAsync(
                "1.0.0",
                "manifest.json",
                PatchUrlGroups.Official));
    }

    [Fact]
    public async Task GetRequiredManifestAsync_WhenCafeManifestIsNotFound_FallsBackToOfficialHost()
    {
        var transport = new StubRemoteHttpTransport(uri =>
        {
            if (uri.AbsolutePath.Contains("/api/launcher/game/config/json", StringComparison.Ordinal))
            {
                return """{"code":200,"data":{"url":"https://launcher-pkg-ba-jp.yo-star.com/zip_online_config_json/test.json"}}""";
            }

            return uri.Host == "launcher-pkg-ba-jp.bluearchive.cafe"
                ? new HttpRequestException("Not Found", null, HttpStatusCode.NotFound)
                : "{\"source\":\"official\",\"file\":[]}";
        });
        var service = new RemoteManifestService(CreateApiClient(transport));

        var result = await service.GetRequiredManifestAsync(
            "1.0.0",
            "manifest.json",
            PatchUrlGroups.Cafe);

        Assert.Equal("official", result.Source);
        Assert.Equal(
            [
                "api-launcher-jp.yo-star.com",
                "launcher-pkg-ba-jp.bluearchive.cafe",
                "launcher-pkg-ba-jp.yo-star.com"
            ],
            transport.RequestedUris.Select(requested => requested.Host));
    }

    [Fact]
    public async Task GetOptionalManifestAsync_WhenUrlIsEmpty_ReturnsNull()
    {
        var apiClient = CreateApiClient(new StubRemoteHttpTransport(uri =>
            uri.AbsolutePath.Contains("/api/launcher/game/config/json", StringComparison.Ordinal)
                ? """{"code":200,"data":{"url":""}}"""
                : "{}"));
        var service = new RemoteManifestService(apiClient);

        var result = await service.GetOptionalManifestAsync(
            "1.0.0",
            "manifest.json",
            PatchUrlGroups.Official);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOptionalManifestAsync_WhenRequestFails_ReturnsNull()
    {
        var apiClient = CreateApiClient(new StubRemoteHttpTransport(
            _ => new HttpRequestException("network failure")));
        var service = new RemoteManifestService(apiClient);

        var result = await service.GetOptionalManifestAsync(
            "1.0.0",
            "manifest.json",
            PatchUrlGroups.Official);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOptionalManifestAsync_WhenCanceled_PropagatesCancellation()
    {
        var apiClient = CreateApiClient(new StubRemoteHttpTransport(
            _ => new OperationCanceledException()));
        var service = new RemoteManifestService(apiClient);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetOptionalManifestAsync(
                "1.0.0",
                "manifest.json",
                PatchUrlGroups.Official,
                cts.Token));
    }

    private static LauncherApiClient CreateApiClient(IRemoteHttpTransport transport) =>
        new(LauncherProfiles.BlueArchiveJapan, transport, new AuthorizationHeaderFactory(LauncherProfiles.BlueArchiveJapan), new PatchUrlGroupService(LauncherProfiles.BlueArchiveJapan, LauncherProfiles.Cafe));
}
