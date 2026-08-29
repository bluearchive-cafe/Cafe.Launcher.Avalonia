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

    [Fact]
    public void GetSafePath_WhenExistingDirectoryIsSymbolicLink_ThrowsInvalidOperation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "GameDir");
        var outsidePath = Path.Combine(tempDir, "Outside");
        var linkPath = Path.Combine(gamePath, "linked");
        Directory.CreateDirectory(gamePath);
        Directory.CreateDirectory(outsidePath);

        try
        {
            CreateDirectorySymbolicLinkOrSkip(linkPath, outsidePath);

            var exception = Assert.Throws<InvalidOperationException>(
                () => GamePathValidator.GetSafePath(gamePath, "linked/file.bin"));

            Assert.Contains("reparse point", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }

            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetSafePath_WhenGameRootIsSymbolicLink_ThrowsInvalidOperation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var actualGamePath = Path.Combine(tempDir, "ActualGameDir");
        var linkedGamePath = Path.Combine(tempDir, "LinkedGameDir");
        Directory.CreateDirectory(actualGamePath);

        try
        {
            CreateDirectorySymbolicLinkOrSkip(linkedGamePath, actualGamePath);

            var exception = Assert.Throws<InvalidOperationException>(
                () => GamePathValidator.GetSafePath(linkedGamePath, "data/file.bin"));

            Assert.Contains("reparse point", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(linkedGamePath))
            {
                Directory.Delete(linkedGamePath);
            }

            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void CreateDirectorySymbolicLinkOrSkip(string path, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            Assert.SkipWhen(
                true,
                $"Directory symbolic links are unavailable: {exception.Message}");
        }
    }
}

public sealed class GamePathValidatorPlatformTests
{
    [Fact]
    public void PathComparison_IsCaseInsensitiveOnlyOnWindows()
    {
        var expected = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        Assert.Equal(expected, GamePathValidator.PathComparison);
    }
}
