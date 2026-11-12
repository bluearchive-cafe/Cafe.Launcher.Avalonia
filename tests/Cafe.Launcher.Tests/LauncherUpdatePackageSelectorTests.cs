using Cafe.Launcher.UI.Models;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Tests;

public sealed class LauncherUpdatePackageSelectorTests
{
    private static readonly LauncherUpdateHostInfo WindowsPortableHost = new(
        IsWindows: true,
        IsX64: true,
        IsInstallerInstall: false);

    private static readonly LauncherUpdateHostInfo WindowsInstallerHost = new(
        IsWindows: true,
        IsX64: true,
        IsInstallerInstall: true);

    [Fact]
    public void Select_NonWindowsHost_FallsBackToExternalDownload()
    {
        var selection = LauncherUpdatePackageSelector.Select(
            new LauncherUpdateHostInfo(IsWindows: false, IsX64: true, IsInstallerInstall: false),
            ReleaseFiles());

        Assert.Equal(LauncherUpdateTarget.ExternalDownload, selection.Target);
        Assert.Null(selection.Package);
        Assert.Null(selection.ChecksumManifest);
        Assert.Equal(LauncherUpdateInAppAvailability.PlatformUnsupported, selection.Availability);
    }

    [Fact]
    public void Select_NonX64WindowsHost_FallsBackToExternalDownload()
    {
        var selection = LauncherUpdatePackageSelector.Select(
            new LauncherUpdateHostInfo(IsWindows: true, IsX64: false, IsInstallerInstall: false),
            ReleaseFiles());

        Assert.Equal(LauncherUpdateTarget.ExternalDownload, selection.Target);
        Assert.Equal(LauncherUpdateInAppAvailability.PlatformUnsupported, selection.Availability);
    }

    [Fact]
    public void Select_WindowsX64Portable_SelectsPortableZipAndChecksumManifest()
    {
        var selection = LauncherUpdatePackageSelector.Select(WindowsPortableHost, ReleaseFiles());

        Assert.Equal(LauncherUpdateTarget.WindowsPortable, selection.Target);
        Assert.Equal("Cafe.Launcher_v1.2.3_win-x64.zip", selection.Package?.Name);
        Assert.Equal("SHA256SUMS", selection.ChecksumManifest?.Name);
        Assert.Equal(LauncherUpdateInAppAvailability.Available, selection.Availability);
    }

    [Fact]
    public void Select_WindowsX64Installer_SelectsSetupExecutable()
    {
        var selection = LauncherUpdatePackageSelector.Select(WindowsInstallerHost, ReleaseFiles());

        Assert.Equal(LauncherUpdateTarget.WindowsInstaller, selection.Target);
        Assert.Equal("Cafe.Launcher_v1.2.3_setup.exe", selection.Package?.Name);
        Assert.Equal(LauncherUpdateInAppAvailability.Available, selection.Availability);
    }

    [Fact]
    public void Select_WhenPortableZipMissing_FallsBackToExternalDownload()
    {
        var files = ReleaseFiles().Where(file => !file.Name.EndsWith("_win-x64.zip", StringComparison.Ordinal)).ToList();

        var selection = LauncherUpdatePackageSelector.Select(WindowsPortableHost, files);

        Assert.Equal(LauncherUpdateTarget.ExternalDownload, selection.Target);
        Assert.Equal(LauncherUpdateInAppAvailability.PackageUnverifiable, selection.Availability);
    }

    [Fact]
    public void Select_WhenChecksumManifestMissing_FallsBackToExternalDownload()
    {
        var files = ReleaseFiles().Where(file => file.Name != "SHA256SUMS").ToList();

        var selection = LauncherUpdatePackageSelector.Select(WindowsPortableHost, files);

        Assert.Equal(LauncherUpdateTarget.ExternalDownload, selection.Target);
        Assert.Equal(LauncherUpdateInAppAvailability.PackageUnverifiable, selection.Availability);
    }

    [Fact]
    public void Select_IgnoresOtherPlatformAssets()
    {
        var selection = LauncherUpdatePackageSelector.Select(WindowsPortableHost, ReleaseFiles());

        Assert.DoesNotContain("osx-arm64", selection.Package!.Name, StringComparison.Ordinal);
        Assert.DoesNotContain("linux-x64", selection.Package!.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Select_SuffixMatch_IsCaseInsensitive()
    {
        var files = new List<ReleaseFile>
        {
            new() { Name = "Cafe.Launcher_V1.2.3_WIN-X64.ZIP", Url = Url, Size = 1 },
            new() { Name = "sha256sums", Url = Url, Size = 1 }
        };

        var selection = LauncherUpdatePackageSelector.Select(WindowsPortableHost, files);

        Assert.Equal(LauncherUpdateTarget.WindowsPortable, selection.Target);
        Assert.Equal(LauncherUpdateInAppAvailability.Available, selection.Availability);
    }

    [Fact]
    public void Select_WhenFilesAreEmpty_FallsBackToExternalDownload()
    {
        var selection = LauncherUpdatePackageSelector.Select(WindowsPortableHost, []);

        Assert.Equal(LauncherUpdateTarget.ExternalDownload, selection.Target);
        Assert.Equal(LauncherUpdateInAppAvailability.PackageUnverifiable, selection.Availability);
    }

    [Theory]
    // 空与目录别名。
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    // 反斜杠只对 Windows 是分隔符；单靠 Path.GetFileName 在 Unix 上会放过它。
    [InlineData("..\\..\\x_setup.exe")]
    [InlineData("..\\x_win-x64.zip")]
    [InlineData("../x_win-x64.zip")]
    [InlineData("a/b\\c_setup.exe")]
    // 绝对路径。
    [InlineData("C:\\temp\\Cafe.Launcher_v1.2.3_setup.exe")]
    [InlineData("/tmp/Cafe.Launcher_v1.2.3_setup.exe")]
    // 尾随分隔符仍是分隔符。
    [InlineData("Cafe.Launcher_v1.2.3_setup.exe/")]
    public void IsSingleFileNameSegment_ForPathLikeOrReservedNames_ReturnsFalse(string name)
    {
        Assert.False(LauncherUpdatePackageSelector.IsSingleFileNameSegment(name));
    }

    [Theory]
    [InlineData("Cafe.Launcher_v1.2.3_win-x64.zip")]
    [InlineData("Cafe.Launcher_v1.2.3_setup.exe")]
    [InlineData("Cafe.Launcher_v1.2.3-beta.1_win-x64.zip")]
    [InlineData("SHA256SUMS")]
    public void IsSingleFileNameSegment_ForReleaseAssetNames_ReturnsTrue(string name)
    {
        Assert.True(LauncherUpdatePackageSelector.IsSingleFileNameSegment(name));
    }

    [Fact]
    public void IsSingleFileNameSegment_WhenNameCarriesAnInvalidFileNameCharacter_ReturnsFalse()
    {
        // 非法字符集随平台不同（Windows 含 '"'、'|' 等，Unix 只有 '\0' 与 '/'），
        // 样本因此从 Path.GetInvalidFileNameChars() 取而不写死。
        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            Assert.False(LauncherUpdatePackageSelector.IsSingleFileNameSegment(
                $"Cafe.Launcher_v1.2.3_a{invalid}b_win-x64.zip"));
        }
    }

    private const string Url = "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v1.2.3/Cafe.Launcher_v1.2.3_win-x64.zip";

    private static List<ReleaseFile> ReleaseFiles() =>
    [
        new() { Name = "Cafe.Launcher_v1.2.3_win-x64.zip", Url = Url, Size = 1 },
        new() { Name = "Cafe.Launcher_v1.2.3_setup.exe", Url = Url, Size = 1 },
        new() { Name = "Cafe.Launcher_v1.2.3_osx-arm64.zip", Url = Url, Size = 1 },
        new() { Name = "Cafe.Launcher_v1.2.3_linux-x64.tar.gz", Url = Url, Size = 1 },
        new() { Name = "Cafe.Launcher_v1.2.3_linux-x64.AppImage", Url = Url, Size = 1 },
        new() { Name = "Cafe.Launcher_v1.2.3_linux-x64.deb", Url = Url, Size = 1 },
        new() { Name = "Cafe.Launcher_v1.2.3_linux-x64.rpm", Url = Url, Size = 1 },
        new() { Name = "SHA256SUMS", Url = Url, Size = 1 }
    ];
}
