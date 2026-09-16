using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Testing;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DirectoryWriteProbeTests
{
    [Fact]
    public void CanWrite_ExistingDirectory_ReturnsTrue()
    {
        using var directory = TestDirectory.Create();

        Assert.True(DirectoryWriteProbe.CanWrite(directory));
    }

    [Fact]
    public void CanWrite_MissingDirectory_ReturnsFalse()
    {
        using var directory = TestDirectory.Create();
        var missing = Path.Combine(directory, "missing");

        Assert.False(DirectoryWriteProbe.CanWrite(missing));
    }

    [Fact]
    public void CanCreate_TargetDirectoryExists_ReturnsTrue()
    {
        using var directory = TestDirectory.Create();

        Assert.True(DirectoryWriteProbe.CanCreate(directory));
    }

    [Fact]
    public void CanCreate_MissingChainUnderWritableAncestor_ReturnsTrue()
    {
        using var ancestor = TestDirectory.Create();
        var target = Path.Combine(ancestor, "YostarGames", "BlueArchive_JP");

        Assert.True(DirectoryWriteProbe.CanCreate(target));
    }

    [Fact]
    public void CanCreate_AncestorChainBlockedByFile_ReturnsFalse()
    {
        using var ancestor = TestDirectory.Create();
        var blocker = Path.Combine(ancestor, "blocker");
        File.WriteAllText(blocker, "not a directory");
        var target = Path.Combine(blocker, "YostarGames", "BlueArchive_JP");

        Assert.False(DirectoryWriteProbe.CanCreate(target));
    }

    [Fact]
    public void CanCreate_WhenProbeSucceeds_LeavesNoResidueFiles()
    {
        using var ancestor = TestDirectory.Create();
        var target = Path.Combine(ancestor, "YostarGames", "BlueArchive_JP");

        var result = DirectoryWriteProbe.CanCreate(target);

        Assert.True(result);
        Assert.Empty(Directory.GetFiles(ancestor, "*.tmp"));
    }
}
