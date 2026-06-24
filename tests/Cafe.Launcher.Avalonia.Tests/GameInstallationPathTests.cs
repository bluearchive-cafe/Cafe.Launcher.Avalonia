using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameInstallationPathTests
{
    private readonly GameInstallationPath installationPath = new();

    [Fact]
    public void GetDefaultGamePath_UsesLauncherDirectory_MatchingOfficialLauncher()
    {
        // Official launcher defaults to dirname(exe)\YostarGames\BlueArchive_JP. The rewrite
        // must resolve the same location so both launchers don't install the game twice.
        var expected = Path.GetFullPath(Path.Combine(
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
            GamePaths.RootFolderName,
            GamePaths.GameFolderName));

        var result = installationPath.GetDefaultGamePath();

        Assert.Equal(expected, result);
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
