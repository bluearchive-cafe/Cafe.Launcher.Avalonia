using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameInstallationPathTests
{
    private readonly GameInstallationPath installationPath = new();

    [Fact]
    public void GetDefaultGamePath_UsesLauncherParentDirectory_MatchingOfficialLauncher()
    {
        // Official launcher defaults to dirname(exe)/../YostarGames/BlueArchive_JP
        // (request-default-download-path, index.js:742-743). The rewrite must resolve
        // the same location so both launchers agree on the default install path.
        var parentOfLauncherDir = Path.GetDirectoryName(
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!;
        var expected = Path.GetFullPath(Path.Combine(
            parentOfLauncherDir,
            GamePaths.RootFolderName,
            GamePaths.GameFolderName));

        var result = installationPath.GetDefaultGamePath();

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetDefaultGamePath_WhenRunningAsAppImage_UsesWritableUserProfile()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var applicationBaseDirectory = Path.Combine(testRoot, ".mount_cafe", "usr", "bin");
        var userProfileDirectory = Path.Combine(testRoot, "home");
        var expected = Path.GetFullPath(Path.Combine(
            userProfileDirectory,
            GamePaths.RootFolderName,
            GamePaths.GameFolderName));

        var result = installationPath.GetDefaultGamePath(
            applicationBaseDirectory,
            Path.Combine(userProfileDirectory, "Cafe.Launcher.AppImage"),
            userProfileDirectory);

        Assert.Equal(expected, result);
        Assert.DoesNotContain(".mount_cafe", result, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeGamePath_WhenParentPathProvided_AppendsLauncherFolders()
    {
        var input = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = installationPath.NormalizeGamePath(input);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(input, GamePaths.RootFolderName, GamePaths.GameFolderName)),
            result);
    }

    [Fact]
    public void NormalizeGamePath_WhenRootFolderProvided_AppendsGameFolder()
    {
        var input = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            GamePaths.RootFolderName);

        var result = installationPath.NormalizeGamePath(input);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(input, GamePaths.GameFolderName)),
            result);
    }

    [Fact]
    public void NormalizeGamePath_WhenGameFolderProvided_ReturnsFullPath()
    {
        var input = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            GamePaths.RootFolderName,
            GamePaths.GameFolderName);

        var result = installationPath.NormalizeGamePath(input);

        Assert.Equal(Path.GetFullPath(input), result);
    }

    [Fact]
    public void NormalizeGamePath_WhenGameFolderHasTrailingSeparator_RemovesTrailingSeparator()
    {
        var input = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            GamePaths.RootFolderName,
            GamePaths.GameFolderName) + Path.DirectorySeparatorChar;

        var result = installationPath.NormalizeGamePath(input);

        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(input)), result);
    }
}
