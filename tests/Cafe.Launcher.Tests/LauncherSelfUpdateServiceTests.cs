using System.Security.Cryptography;
using System.Text;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Tests;

public sealed class LauncherSelfUpdateServiceTests
{
    private const string PackageUrl = "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.2.3/Cafe.Launcher_v1.2.3_win-x64.zip";
    private const string ManifestUrl = "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.2.3/SHA256SUMS";
    private const string PackageName = "Cafe.Launcher_v1.2.3_win-x64.zip";
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
    public async Task PrepareAsync_WhenTheManifestTargetResolvesToALocalAddress_ReportsUrlRejectedAndLogsIt()
    {
        // #33：hosts/加速器把 GitHub 解析到 127.0.0.1 时，清单抓取在旧代码里被吞成
        // null，用户只看到笼统的网络失败、日志里什么都没有。现在必须分类并留痕。
        using var directory = TestDirectory.Create();
        var diagnostics = new RecordingDiagnostics();
        var service = CreateService(
            WindowsPortableHost(),
            uri => uri.AbsolutePath.EndsWith("SHA256SUMS", StringComparison.Ordinal)
                ? BlockedAddressRejection()
                : PackageBytes,
            directory.DataRoot,
            diagnostics: diagnostics);

        var preparation = await service.PrepareAsync(
            ReleaseFiles(), Version, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSelfUpdatePreparationStatus.Failed, preparation.Status);
        Assert.Equal(LauncherSelfUpdateFailureKind.UrlRejected, preparation.FailureKind);
        var log = Assert.Single(diagnostics.Messages);
        Assert.Contains("could not be fetched", log, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_WhenTheManifestFetchFailsOnTheNetwork_ReportsNetworkFailureAndLogsIt()
    {
        using var directory = TestDirectory.Create();
        var diagnostics = new RecordingDiagnostics();
        var service = CreateService(
            WindowsPortableHost(),
            uri => uri.AbsolutePath.EndsWith("SHA256SUMS", StringComparison.Ordinal)
                ? new HttpRequestException("connection refused")
                : PackageBytes,
            directory.DataRoot,
            diagnostics: diagnostics);

        var preparation = await service.PrepareAsync(
            ReleaseFiles(), Version, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSelfUpdatePreparationStatus.Failed, preparation.Status);
        Assert.Equal(LauncherSelfUpdateFailureKind.Network, preparation.FailureKind);
        Assert.Contains("connection refused", Assert.Single(diagnostics.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_WhenThePackageTargetResolvesToALocalAddress_ReportsUrlRejected()
    {
        using var directory = TestDirectory.Create();
        var service = CreateService(
            WindowsPortableHost(),
            uri => uri.AbsolutePath.EndsWith("SHA256SUMS", StringComparison.Ordinal)
                ? $"{PackageSha}  {PackageName}"
                : BlockedAddressRejection(),
            directory.DataRoot);

        var preparation = await service.PrepareAsync(
            ReleaseFiles(), Version, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSelfUpdatePreparationStatus.Failed, preparation.Status);
        Assert.Equal(LauncherSelfUpdateFailureKind.UrlRejected, preparation.FailureKind);
    }

    [Fact]
    public async Task PrepareAsync_WhenTheManifestDigestDoesNotMatchTheDownload_ReportsVerificationFailure()
    {
        using var directory = TestDirectory.Create();
        var service = CreateService(
            WindowsPortableHost(),
            Responder($"{new string('0', 64)}  {PackageName}"),
            directory.DataRoot);

        var preparation = await service.PrepareAsync(
            ReleaseFiles(), Version, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSelfUpdatePreparationStatus.Failed, preparation.Status);
        Assert.Equal(LauncherSelfUpdateFailureKind.Verification, preparation.FailureKind);
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

    [Theory]
    // 反斜杠上跳（Windows 分隔符）。
    [InlineData("..\\..\\x_win-x64.zip")]
    [InlineData("..\\x_win-x64.zip")]
    // 正斜杠上跳（两个平台的分隔符）。
    [InlineData("../x_win-x64.zip")]
    // 两种分隔符同现。
    [InlineData("a\\b/c_win-x64.zip")]
    // 绝对路径：Path.Combine 会整段替换基路径。
    [InlineData("C:\\temp\\x_win-x64.zip")]
    [InlineData("/tmp/x_win-x64.zip")]
    public async Task PrepareAsync_WhenThePackageNameIsNotASingleFileName_FailsBeforeDownloading(
        string packageName)
    {
        // AUD-UPD-001：包名会成为数据根下的落点文件名，越界形态必须在拼路径之前被拒绝，
        // 且不得先花掉一次网络请求。
        using var directory = TestDirectory.Create();
        var transport = new StubRemoteHttpTransport(
            _ => throw new InvalidOperationException("No request may be made for an unsafe package name."));
        var service = new LauncherSelfUpdateService(
            new LauncherUpdateDownloader(transport),
            new LauncherUpdateHostInfoProviderFake(WindowsPortableHost()),
            new StubWindowsLauncherUpdateApplier(isAvailable: true),
            directory.DataRoot,
            new LocalDiagnostics());
        var files = new List<ReleaseFile>
        {
            new() { Name = packageName, Url = PackageUrl, Size = PackageBytes.Length },
            new() { Name = "SHA256SUMS", Url = ManifestUrl, Size = 128 }
        };

        var preparation = await service.PrepareAsync(
            files, Version, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSelfUpdatePreparationStatus.Failed, preparation.Status);
        Assert.Equal(LauncherSelfUpdateFailureKind.Verification, preparation.FailureKind);
        Assert.Empty(transport.RequestedUris);
        Assert.False(System.IO.Directory.Exists(directory.DataRoot.UpdateDirectory));
    }

    [Theory]
    // "." / ".." 能通过逐字符过滤，却指向目录本身或父目录。
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("../..")]
    [InlineData("..\\..")]
    [InlineData("1.2/../..")]
    public async Task PrepareAsync_WhenTheVersionSegmentWouldEscape_StagesInsideTheUpdateDirectory(
        string version)
    {
        // 版本段来自发布元数据；落点必须始终在 UpdateDirectory 之内（带分隔符的前缀比较）。
        using var directory = TestDirectory.Create();
        var service = CreateService(
            WindowsPortableHost(),
            Responder($"{PackageSha}  {PackageName}"),
            directory.DataRoot);

        var preparation = await service.PrepareAsync(
            ReleaseFiles(), version, progress: null, TestContext.Current.CancellationToken);

        Assert.True(
            preparation.Status == LauncherSelfUpdatePreparationStatus.Ready,
            preparation.FailureMessage);
        Assert.NotNull(preparation.PackagePath);
        Assert.True(System.IO.File.Exists(preparation.PackagePath));
        Assert.StartsWith(
            System.IO.Path.TrimEndingDirectorySeparator(directory.DataRoot.UpdateDirectory)
                + System.IO.Path.DirectorySeparatorChar,
            preparation.PackagePath,
            StringComparison.Ordinal);
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
        bool isHelperAvailable = true,
        ILauncherDiagnostics? diagnostics = null) =>
        new(
            new LauncherUpdateDownloader(new StubRemoteHttpTransport(responder)),
            new LauncherUpdateHostInfoProviderFake(host),
            new StubWindowsLauncherUpdateApplier(isHelperAvailable),
            dataRoot,
            diagnostics ?? new LocalDiagnostics());

    /// <summary>
    /// 构造与 <see cref="RemoteHttpUrlValidator"/> 私网拒绝完全同形的异常：类型不变，
    /// 只多带标记键，自更新服务据此把「本机/内网地址」与其他传输失败分开。
    /// </summary>
    private static InvalidOperationException BlockedAddressRejection()
    {
        var exception = new InvalidOperationException(
            "Remote URL resolves to a blocked network address. Blocked: 127.0.0.1");
        exception.Data[RemoteHttpUrlValidator.BlockedAddressDataKey] = true;
        return exception;
    }

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
