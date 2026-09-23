using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Update;

namespace Cafe.Launcher.Avalonia.Tests;

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
        Assert.False(selection.CanApplyInApp);
    }

    [Fact]
    public void Select_NonX64WindowsHost_FallsBackToExternalDownload()
    {
        var selection = LauncherUpdatePackageSelector.Select(
            new LauncherUpdateHostInfo(IsWindows: true, IsX64: false, IsInstallerInstall: false),
            ReleaseFiles());

        Assert.Equal(LauncherUpdateTarget.ExternalDownload, selection.Target);
        Assert.False(selection.CanApplyInApp);
    }

    [Fact]
    public void Select_WindowsX64Portable_SelectsPortableZipAndChecksumManifest()
    {
        var selection = LauncherUpdatePackageSelector.Select(WindowsPortableHost, ReleaseFiles());

        Assert.Equal(LauncherUpdateTarget.WindowsPortable, selection.Target);
        Assert.Equal("Cafe.Launcher.Avalonia_v1.2.3_win-x64.zip", selection.Package?.Name);
        Assert.Equal("SHA256SUMS", selection.ChecksumManifest?.Name);
        Assert.True(selection.CanApplyInApp);
    }

    [Fact]
    public void Select_WindowsX64Installer_SelectsSetupExecutable()
    {
        var selection = LauncherUpdatePackageSelector.Select(WindowsInstallerHost, ReleaseFiles());

        Assert.Equal(LauncherUpdateTarget.WindowsInstaller, selection.Target);
        Assert.Equal("Cafe.Launcher.Avalonia_v1.2.3_setup.exe", selection.Package?.Name);
        Assert.True(selection.CanApplyInApp);
    }

    [Fact]
    public void Select_WhenPortableZipMissing_FallsBackToExternalDownload()
    {
        var files = ReleaseFiles().Where(file => !file.Name.EndsWith("_win-x64.zip", StringComparison.Ordinal)).ToList();

        var selection = LauncherUpdatePackageSelector.Select(WindowsPortableHost, files);

        Assert.Equal(LauncherUpdateTarget.ExternalDownload, selection.Target);
        Assert.False(selection.CanApplyInApp);
    }

    [Fact]
    public void Select_WhenChecksumManifestMissing_FallsBackToExternalDownload()
    {
        var files = ReleaseFiles().Where(file => file.Name != "SHA256SUMS").ToList();

        var selection = LauncherUpdatePackageSelector.Select(WindowsPortableHost, files);

        Assert.Equal(LauncherUpdateTarget.ExternalDownload, selection.Target);
        Assert.False(selection.CanApplyInApp);
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
            new() { Name = "Cafe.Launcher.Avalonia_V1.2.3_WIN-X64.ZIP", Url = Url, Size = 1 },
            new() { Name = "sha256sums", Url = Url, Size = 1 }
        };

        var selection = LauncherUpdatePackageSelector.Select(WindowsPortableHost, files);

        Assert.Equal(LauncherUpdateTarget.WindowsPortable, selection.Target);
        Assert.True(selection.CanApplyInApp);
    }

    [Fact]
    public void Select_WhenFilesAreEmpty_FallsBackToExternalDownload()
    {
        var selection = LauncherUpdatePackageSelector.Select(WindowsPortableHost, []);

        Assert.Equal(LauncherUpdateTarget.ExternalDownload, selection.Target);
    }

    private const string Url = "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v1.2.3/Cafe.Launcher.Avalonia_v1.2.3_win-x64.zip";

    private static List<ReleaseFile> ReleaseFiles() =>
    [
        new() { Name = "Cafe.Launcher.Avalonia_v1.2.3_win-x64.zip", Url = Url, Size = 1 },
        new() { Name = "Cafe.Launcher.Avalonia_v1.2.3_setup.exe", Url = Url, Size = 1 },
        new() { Name = "Cafe.Launcher.Avalonia_v1.2.3_osx-arm64.zip", Url = Url, Size = 1 },
        new() { Name = "Cafe.Launcher.Avalonia_v1.2.3_linux-x64.tar.gz", Url = Url, Size = 1 },
        new() { Name = "Cafe.Launcher.Avalonia_v1.2.3_linux-x64.AppImage", Url = Url, Size = 1 },
        new() { Name = "Cafe.Launcher.Avalonia_v1.2.3_linux-x64.deb", Url = Url, Size = 1 },
        new() { Name = "SHA256SUMS", Url = Url, Size = 1 }
    ];
}
