using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GamePathValidatorTests
{
    [Theory]
    [InlineData("data/file.bin")]
    [InlineData("subdir/nested/file.bin")]
    [InlineData("./data/file.bin")]
    public void GetSafePath_WhenPathIsSafe_ReturnsNormalizedPath(string relativePath)
    {
        var gamePath = Path.Combine(Path.GetTempPath(), "GameDir");

        var result = GamePathValidator.GetSafePath(gamePath, relativePath);

        Assert.StartsWith(Path.GetFullPath(gamePath) + Path.DirectorySeparatorChar, result);
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("data/../../outside.bin")]
    [InlineData("..\\escape.bin")]
    public void GetSafePath_WhenPathEscapes_ThrowsInvalidOperation(string relativePath)
    {
        var gamePath = Path.Combine(Path.GetTempPath(), "GameDir");

        var ex = Assert.Throws<InvalidOperationException>(
            () => GamePathValidator.GetSafePath(gamePath, relativePath));
        Assert.Contains("escapes", ex.Message);
    }

    [Fact]
    public void GetSafePath_WhenPathIsEmpty_ReturnsGameRoot()
    {
        var gamePath = Path.Combine(Path.GetTempPath(), "GameDir");

        var result = GamePathValidator.GetSafePath(gamePath, "");

        Assert.Equal(Path.GetFullPath(gamePath), result);
    }

    [Fact]
    public void GetSafePath_WhenGameRootIsDriveRoot_DoesNotDuplicateSeparator()
    {
        var driveRoot = Path.GetPathRoot(Path.GetTempPath())!;

        // If the root is a drive root (e.g. "C:\"), a valid relative path should be safe.
        var result = GamePathValidator.GetSafePath(driveRoot, "test.bin");

        Assert.StartsWith(driveRoot.TrimEnd(Path.DirectorySeparatorChar), result);
    }
}
