using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

/// <summary>
/// 「目录里还有东西吗」的判据（2026-09-29 反馈轮）。它决定全新安装开始前要不要提示「目录中已有
/// 内容」，因此「不存在」与「存在但为空」都必须答 false——对空目录说「已有内容」是假话，而提示
/// 一旦开始说假话，用户就会连同真的那句一起忽略。
/// </summary>
public sealed class InstallDirectoryContentTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public void HasEntries_WhenTheFolderDoesNotExist_IsFalse() =>
        // 全新安装的默认路径就是这样：没什么可说，也不该去枚举一个不存在的路径。
        Assert.False(InstallDirectoryContent.HasEntries(Path.Combine(tempDir, "missing")));

    [Fact]
    public void HasEntries_WhenTheFolderIsEmpty_IsFalse()
    {
        var path = Path.Combine(tempDir, "empty");
        Directory.CreateDirectory(path);

        Assert.False(InstallDirectoryContent.HasEntries(path));
    }

    [Fact]
    public void HasEntries_WhenTheFolderHoldsOnlyADirectory_IsTrue()
    {
        // 自制的空子目录也算「有东西」：判据只回答「空不空」，不替调用方解释条目是什么。
        var path = Path.Combine(tempDir, "with-folder");
        Directory.CreateDirectory(Path.Combine(path, "empty-subfolder"));

        Assert.True(InstallDirectoryContent.HasEntries(path));
    }

    [Fact]
    public void HasEntries_WhenTheFolderHoldsAFile_IsTrue()
    {
        var path = Path.Combine(tempDir, "with-file");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "leftover.bin"), "x");

        Assert.True(InstallDirectoryContent.HasEntries(path));
    }

    [Fact]
    public void HasEntries_WhenThePathIsBlank_IsFalse() =>
        Assert.False(InstallDirectoryContent.HasEntries("  "));

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
