using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Tests;

/// <summary>
/// <see cref="ManifestFileRemover"/> 的实测结果（2026-09-29 反馈轮）。此前的删除是「只做不报」，
/// 调用方于是只能拿清单条数当删除条数去汇报：卸载日志里的 <c>files: 157</c> 既可能是删了 157 个，
/// 也可能一个都没删。实测结果把「计划」与「实际」分开。
/// </summary>
public sealed class ManifestFileRemoverTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public void DeleteAll_WhenSomeEntriesAreAlreadyGone_CountsOnlyWhatWasRemoved()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(Path.Combine(gamePath, "data"));
        var firstPath = Path.Combine(gamePath, "data", "one.bin");
        var gonePath = Path.Combine(gamePath, "data", "two.bin");
        var thirdPath = Path.Combine(gamePath, "data", "three.bin");
        File.WriteAllText(firstPath, "11111");
        File.WriteAllText(thirdPath, "333");
        File.WriteAllText(gonePath, "22");
        // 清单是提交时的快照：提交之后再手工删掉一个条目，删除时就该按「本来就不在」记。
        File.Delete(gonePath);
        var expectedBytes = new FileInfo(firstPath).Length + new FileInfo(thirdPath).Length;
        ManifestFile[] files =
        [
            new() { Path = "data/one.bin" },
            new() { Path = "data/two.bin" },
            new() { Path = "data/three.bin" }
        ];

        var result = ManifestFileRemover.DeleteAll(gamePath, files);

        Assert.Equal(2, result.RemovedCount);
        Assert.Equal(1, result.MissingCount);
        Assert.Equal(expectedBytes, result.RemovedBytes);
        Assert.False(File.Exists(firstPath));
        Assert.False(File.Exists(thirdPath));
    }

    [Fact]
    public void DeleteAll_WhenAManifestEntryIsReadOnly_ClearsTheAttributeAndCountsIt()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        var readOnlyPath = Path.Combine(gamePath, "readonly.bin");
        File.WriteAllText(readOnlyPath, "content");
        var expectedBytes = new FileInfo(readOnlyPath).Length;
        File.SetAttributes(readOnlyPath, FileAttributes.ReadOnly);
        ManifestFile[] files = [new() { Path = "readonly.bin" }];

        var result = ManifestFileRemover.DeleteAll(gamePath, files);

        Assert.Equal(1, result.RemovedCount);
        Assert.Equal(0, result.MissingCount);
        Assert.Equal(expectedBytes, result.RemovedBytes);
        Assert.False(File.Exists(readOnlyPath));
    }

    [Fact]
    public void DeleteAll_WhenTheManifestIsEmpty_ReportsNothingRemoved()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);

        var result = ManifestFileRemover.DeleteAll(gamePath, []);

        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(0, result.MissingCount);
        Assert.Equal(0, result.RemovedBytes);
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
