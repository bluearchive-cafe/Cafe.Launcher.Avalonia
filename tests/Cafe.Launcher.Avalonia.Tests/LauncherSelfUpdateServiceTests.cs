using System.Security.Cryptography;
using System.Text;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.Update;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherSelfUpdateServiceTests
{
    private const string PackageUrl = "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.2.3/Cafe.Launcher.Avalonia_v1.2.3_win-x64.zip";
    private const string ManifestUrl = "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.2.3/SHA256SUMS";
    private const string PackageName = "Cafe.Launcher.Avalonia_v1.2.3_win-x64.zip";
    private const string Version = "1.2.3";

    private static readonly byte[] PackageBytes = Encoding.UTF8.GetBytes("launcher package payload");
    private static readonly string PackageSha = Convert.ToHexString(SHA256.HashData(PackageBytes)).ToLowerInvariant();

    [Fact]
    public async Task PrepareAsync_WhenHostIsNotWindows_ReturnsExternalDownload()
    {
        using var directory = TestDirectory.Create();
        var service = CreateService(
            new LauncherUpdateHostInfo(IsWindows: false, IsX64: true, IsInstallerInstall: false),
            _ => PackageBytes,
            directory.DataRoot);

        var preparation = await service.PrepareAsync(
            ReleaseFiles(), Version, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSelfUpdatePreparationStatus.ExternalDownload, preparation.Status);
    }

    [Fact]
    public async Task PrepareAsync_WhenChecksumManifestIsMissingFromRelease_ReturnsExternalDownload()
    {
        using var directory = TestDirectory.Create();
        var service = CreateService(WindowsPortableHost(), _ => PackageBytes, directory.DataRoot);
        var files = new List<ReleaseFile>
        {
            new() { Name = PackageName, Url = PackageUrl, Size = PackageBytes.Length }
        };

        var preparation = await service.PrepareAsync(
            files, Version, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSelfUpdatePreparationStatus.ExternalDownload, preparation.Status);
    }

    [Fact]
    public async Task PrepareAsync_WhenManifestIsUnparseable_ReturnsFailed()
    {
        using var directory = TestDirectory.Create();
        var service = CreateService(WindowsPortableHost(), Responder(manifestText: "not a manifest"), directory.DataRoot);

        var preparation = await service.PrepareAsync(
            ReleaseFiles(), Version, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSelfUpdatePreparationStatus.Failed, preparation.Status);
    }

    [Fact]
    public async Task PrepareAsync_WhenManifestOmitsThePackage_ReturnsFailed()
    {
        using var directory = TestDirectory.Create();
        var other = new string('a', 64);
        var service = CreateService(
            WindowsPortableHost(),
            Responder($"{other}  some-other-file.zip"),
            directory.DataRoot);

        var preparation = await service.PrepareAsync(
            ReleaseFiles(), Version, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSelfUpdatePreparationStatus.Failed, preparation.Status);
    }

    [Fact]
    public async Task PrepareAsync_WhenPackageVerifies_ReturnsReadyWithStagedPath()
    {
        using var directory = TestDirectory.Create();
        var service = CreateService(
            WindowsPortableHost(),
            Responder($"{PackageSha}  {PackageName}"),
            directory.DataRoot);

        var preparation = await service.PrepareAsync(
            ReleaseFiles(), Version, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSelfUpdatePreparationStatus.Ready, preparation.Status);
        Assert.Equal(LauncherUpdateTarget.WindowsPortable, preparation.Target);
        Assert.Equal(PackageSha, preparation.ExpectedSha256);
        Assert.NotNull(preparation.PackagePath);
        Assert.True(System.IO.File.Exists(preparation.PackagePath));
        Assert.StartsWith(directory.DataRoot.UpdateDirectory, preparation.PackagePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_WhenManifestDigestDoesNotMatchTheDownload_ReturnsFailed()
    {
        using var directory = TestDirectory.Create();
        var service = CreateService(
            WindowsPortableHost(),
            Responder($"{new string('0', 64)}  {PackageName}"),
            directory.DataRoot);

        var preparation = await service.PrepareAsync(
            ReleaseFiles(), Version, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSelfUpdatePreparationStatus.Failed, preparation.Status);
    }

    [Fact]
    public void ResolveInAppAvailability_WhenTheHostIsNotWindows_ReportsPlatformUnsupported()
    {
        using var directory = TestDirectory.Create();
        var service = CreateService(
            new LauncherUpdateHostInfo(IsWindows: false, IsX64: true, IsInstallerInstall: false),
            Responder($"{PackageSha}  {PackageName}"),
            directory.DataRoot);

        Assert.Equal(
            LauncherUpdateInAppAvailability.PlatformUnsupported,
            service.ResolveInAppAvailability(ReleaseFiles()));
    }

    [Fact]
    public void ResolveInAppAvailability_WhenTheReleaseHasNoChecksumManifest_ReportsPackageUnverifiable()
    {
        using var directory = TestDirectory.Create();
        var service = CreateService(
            WindowsPortableHost(),
            Responder($"{PackageSha}  {PackageName}"),
            directory.DataRoot);
        var files = new List<ReleaseFile>
        {
            new() { Name = PackageName, Url = PackageUrl, Size = PackageBytes.Length }
        };

        // 设备与安装都没问题，是这一版没有可校验的东西：归因必须落在发布侧。
        Assert.Equal(
            LauncherUpdateInAppAvailability.PackageUnverifiable,
            service.ResolveInAppAvailability(files));
    }

    [Fact]
    public void ResolveInAppAvailability_WhenTheHelperIsMissing_ReportsHelperMissing()
    {
        using var directory = TestDirectory.Create();
        var service = CreateService(
            WindowsPortableHost(),
            Responder($"{PackageSha}  {PackageName}"),
            directory.DataRoot,
            isHelperAvailable: false);

        Assert.Equal(
            LauncherUpdateInAppAvailability.HelperMissing,
            service.ResolveInAppAvailability(ReleaseFiles()));
    }

    [Fact]
    public void ResolveInAppAvailability_WhenHostReleaseAndHelperAgree_ReportsAvailable()
    {
        using var directory = TestDirectory.Create();
        var service = CreateService(
            WindowsPortableHost(),
            Responder($"{PackageSha}  {PackageName}"),
            directory.DataRoot);

        Assert.Equal(
            LauncherUpdateInAppAvailability.Available,
            service.ResolveInAppAvailability(ReleaseFiles()));
    }

    /// <summary>
    /// 形状约束：可用性判定只给原因，不给裸布尔（ADR-027 的同一条规矩）。调用方因此必须对
    /// 「不可用」表态——说明行按原因说话，而不是把三种否定压成一句笼统的「此设备无法…」。
    /// </summary>
    [Fact]
    public void ResolveInAppAvailability_IsTheOnlyPublicVerdict_NoBareBoolean()
    {
        var verdicts = typeof(LauncherSelfUpdateService)
            .GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(method => (method.Name, method.ReturnType))
            .ToArray();

        Assert.Contains(verdicts, verdict =>
            verdict.Name == "ResolveInAppAvailability"
            && verdict.ReturnType == typeof(LauncherUpdateInAppAvailability));
        Assert.DoesNotContain(verdicts, verdict => verdict.ReturnType == typeof(bool));
    }

    /// <summary>
    /// A host without the helper cannot apply anything, so the download must not start at all:
    /// fetching a package that is guaranteed to end in the release-page fallback would only
    /// cost the user the transfer.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_WhenTheHelperIsMissing_ReturnsExternalDownloadWithoutFetching()
    {
        using var directory = TestDirectory.Create();
        var transport = new StubRemoteHttpTransport(Responder($"{PackageSha}  {PackageName}"));
        var service = new LauncherSelfUpdateService(
            new LauncherUpdateDownloader(transport),
            new LauncherUpdateHostInfoProviderFake(WindowsPortableHost()),
            new StubWindowsLauncherUpdateApplier(isAvailable: false),
            directory.DataRoot,
            new LocalDiagnostics());

        var preparation = await service.PrepareAsync(
            ReleaseFiles(), Version, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSelfUpdatePreparationStatus.ExternalDownload, preparation.Status);
        Assert.Empty(transport.RequestedUris);
    }

    private static LauncherUpdateHostInfo WindowsPortableHost() =>
        new(IsWindows: true, IsX64: true, IsInstallerInstall: false);

    private static Func<Uri, object?> Responder(string manifestText) =>
        uri => uri.AbsolutePath.EndsWith("SHA256SUMS", StringComparison.Ordinal)
            ? manifestText
            : PackageBytes;

    private static LauncherSelfUpdateService CreateService(
        LauncherUpdateHostInfo host,
        Func<Uri, object?> responder,
        LauncherDataRoot dataRoot,
        bool isHelperAvailable = true) =>
        new(
            new LauncherUpdateDownloader(new StubRemoteHttpTransport(responder)),
            new LauncherUpdateHostInfoProviderFake(host),
            new StubWindowsLauncherUpdateApplier(isHelperAvailable),
            dataRoot,
            new LocalDiagnostics());

    private static List<ReleaseFile> ReleaseFiles() =>
    [
        new() { Name = PackageName, Url = PackageUrl, Size = PackageBytes.Length },
        new() { Name = "SHA256SUMS", Url = ManifestUrl, Size = 128 }
    ];

    private sealed class LauncherUpdateHostInfoProviderFake(LauncherUpdateHostInfo info)
        : ILauncherUpdateHostInfoProvider
    {
        public LauncherUpdateHostInfo GetHostInfo() => info;
    }
}
