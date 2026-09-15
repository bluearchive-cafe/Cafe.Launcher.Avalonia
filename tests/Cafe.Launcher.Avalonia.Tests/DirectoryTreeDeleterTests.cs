using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// <see cref="DirectoryTreeDeleter"/> 的守卫与语义（ADR-030）：目标必须落在调用方声明的根内，
/// 且不得是盘根或 reparse point；递归删除本身不跟随链接。
/// </summary>
public sealed class DirectoryTreeDeleterTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Delete_WhenTargetIsInsideAllowedRoot_RemovesTheWholeTree()
    {
        var root = Path.Combine(tempDir, "root");
        var target = Path.Combine(root, "nested", "tree");
        Directory.CreateDirectory(Path.Combine(target, "deep"));
        File.WriteAllText(Path.Combine(target, "deep", "file.txt"), "x");

        DirectoryTreeDeleter.Delete(target, root);

        Assert.False(Directory.Exists(target));
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void Delete_WhenTargetEqualsAllowedRoot_RemovesIt()
    {
        var root = Path.Combine(tempDir, "self");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "file.txt"), "x");

        DirectoryTreeDeleter.Delete(root, root);

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Delete_WhenTargetIsMissing_DoesNothing()
    {
        var root = Path.Combine(tempDir, "root");
        Directory.CreateDirectory(root);

        DirectoryTreeDeleter.Delete(Path.Combine(root, "missing"), root);

        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void Delete_WhenTargetEscapesAllowedRoot_ThrowsAndKeepsTheTarget()
    {
        var root = Path.Combine(tempDir, "root");
        var outside = Path.Combine(tempDir, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        var exception = Assert.Throws<InvalidOperationException>(
            () => DirectoryTreeDeleter.Delete(outside, root));

        Assert.Contains("outside", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(outside));
    }

    [Fact]
    public void Delete_WhenTargetIsDriveRoot_Throws()
    {
        var volumeRoot = Path.GetPathRoot(Path.GetFullPath(tempDir))!;

        Assert.Throws<InvalidOperationException>(() => DirectoryTreeDeleter.Delete(volumeRoot, volumeRoot));
    }

    [Fact]
    public void Delete_WhenTargetIsReparsePoint_ThrowsAndKeepsTheLinkTarget()
    {
        var root = Path.Combine(tempDir, "root");
        var outside = Path.Combine(tempDir, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        var link = Path.Combine(root, "link");
        TestSymlinks.CreateDirectorySymbolicLinkOrSkip(link, outside);

        Assert.Throws<InvalidOperationException>(() => DirectoryTreeDeleter.Delete(link, root));

        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
    }

    [Fact]
    public void Delete_WhenTreeContainsAReparsePoint_RemovesTheLinkAndKeepsItsTarget()
    {
        // 钉住 .NET 递归删除的语义：树内的链接按链接本身删掉，不跟随进目标目录。
        // 若哪天这个前提不成立，本用例会红，守卫再补。
        var root = Path.Combine(tempDir, "root-with-link");
        var outside = Path.Combine(tempDir, "outside-keep");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var keep = Path.Combine(outside, "keep.txt");
        File.WriteAllText(keep, "keep");
        File.WriteAllText(Path.Combine(root, "inside.txt"), "x");
        TestSymlinks.CreateDirectorySymbolicLinkOrSkip(Path.Combine(root, "link"), outside);

        DirectoryTreeDeleter.Delete(root, root);

        Assert.False(Directory.Exists(root));
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public void Delete_WhenTreeHasAReadOnlyFile_RemovesItToo()
    {
        // .NET 的递归删除会先摘掉只读属性；显式遍历必须做同样的事，否则卸载会卡在一个
        // 只读文件上（游戏目录里确实会出现）。
        var root = Path.Combine(tempDir, "root-readonly");
        Directory.CreateDirectory(root);
        var readOnlyPath = Path.Combine(root, "readonly.bin");
        File.WriteAllText(readOnlyPath, "x");
        File.SetAttributes(readOnlyPath, FileAttributes.ReadOnly);

        DirectoryTreeDeleter.Delete(root, root);

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Delete_WhenAnEntryCannotBeDeleted_RemovesTheRestAndReportsTheBlocker()
    {
        // 实测（2026-09-15 实机回归）：Blue Archive 的反作弊在 StreamingAssets 下留下
        // "Xigncode:{GUID}" 目录项——列得出来、打不开、无 8.3 短名，用户态没有任何 API 删得掉。
        // 从前一处卡住就中断整棵树的删除，报出来的还只是父目录那句无信息量的「目录不是空的」，
        // 安装目录整条留在盘上。现在要删完能删的，并把真正卡住的路径交回调用方如实上报。
        var root = Path.Combine(tempDir, "root-blocked");
        var keepDir = Path.Combine(root, "keep");
        var blockedDir = Path.Combine(root, "blocked");
        Directory.CreateDirectory(keepDir);
        Directory.CreateDirectory(blockedDir);
        File.WriteAllText(Path.Combine(keepDir, "gone.txt"), "x");
        var lockedPath = Path.Combine(blockedDir, "locked.bin");
        File.WriteAllText(lockedPath, "x");

        using var handle = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var blocked = DirectoryTreeDeleter.Delete(root, root);

        Assert.Contains(lockedPath, blocked);
        Assert.False(Directory.Exists(keepDir));
        Assert.True(File.Exists(lockedPath));
    }

    [Fact]
    public void Delete_WhenEverythingCanBeDeleted_ReportsNothingBlocked()
    {
        var root = Path.Combine(tempDir, "root-clean");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        File.WriteAllText(Path.Combine(root, "nested", "file.txt"), "x");

        var blocked = DirectoryTreeDeleter.Delete(root, root);

        Assert.Empty(blocked);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("child")]
    [InlineData("child/grandchild")]
    public void IsUnder_WhenPathIsInsideRoot_ReturnsTrue(string relative)
    {
        var root = Path.Combine(tempDir, "root");

        Assert.True(DirectoryTreeDeleter.IsUnder(Path.Combine(root, relative), root));
    }

    [Fact]
    public void IsUnder_WhenPathIsTheRootItself_ReturnsTrue()
    {
        var root = Path.Combine(tempDir, "root");

        Assert.True(DirectoryTreeDeleter.IsUnder(root, root));
    }

    [Fact]
    public void IsUnder_WhenPathIsASibling_ReturnsFalse()
    {
        var root = Path.Combine(tempDir, "root");

        Assert.False(DirectoryTreeDeleter.IsUnder(Path.Combine(tempDir, "root-sibling"), root));
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 2)
                {
                    throw;
                }

                Thread.Sleep(TimeSpan.FromMilliseconds(100 * (attempt + 1)));
            }
        }
    }
}
