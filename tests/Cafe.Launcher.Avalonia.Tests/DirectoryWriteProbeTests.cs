using Cafe.Launcher.Avalonia.Helpers;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DirectoryWriteProbeTests
{
    [Fact]
    public void CanWrite_ExistingDirectory_ReturnsTrue()
    {
        var directory = CreateTempDirectory();

        Assert.True(DirectoryWriteProbe.CanWrite(directory));
    }

    [Fact]
    public void CanWrite_MissingDirectory_ReturnsFalse()
    {
        var directory = Path.Combine(CreateTempDirectory(), "missing");

        Assert.False(DirectoryWriteProbe.CanWrite(directory));
    }

    [Fact]
    public void CanCreate_TargetDirectoryExists_ReturnsTrue()
    {
        var directory = CreateTempDirectory();

        Assert.True(DirectoryWriteProbe.CanCreate(directory));
    }

    [Fact]
    public void CanCreate_MissingChainUnderWritableAncestor_ReturnsTrue()
    {
        var ancestor = CreateTempDirectory();
        var target = Path.Combine(ancestor, "YostarGames", "BlueArchive_JP");

        Assert.True(DirectoryWriteProbe.CanCreate(target));
    }

    [Fact]
    public void CanCreate_AncestorChainBlockedByFile_ReturnsFalse()
    {
        var ancestor = CreateTempDirectory();
        var blocker = Path.Combine(ancestor, "blocker");
        File.WriteAllText(blocker, "not a directory");
        var target = Path.Combine(blocker, "YostarGames", "BlueArchive_JP");

        Assert.False(DirectoryWriteProbe.CanCreate(target));
    }

    [Fact]
    public void CanCreate_WhenProbeSucceeds_LeavesNoResidueFiles()
    {
        var ancestor = CreateTempDirectory();
        var target = Path.Combine(ancestor, "YostarGames", "BlueArchive_JP");

        var result = DirectoryWriteProbe.CanCreate(target);

        Assert.True(result);
        Assert.Empty(Directory.GetFiles(ancestor, "*.tmp"));
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
