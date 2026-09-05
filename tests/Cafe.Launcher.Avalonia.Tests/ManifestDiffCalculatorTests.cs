using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ManifestDiffCalculatorTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void GameManifestDiff_WhenFilesChangeAddAndRemove_ReturnsExpectedPlan()
    {
        var unchanged = CreateManifestFile("unchanged.bin", "1", 1);
        var changed = CreateManifestFile("changed.bin", "1", 1);
        var removed = CreateManifestFile("removed.bin", "1", 1);
        var added = CreateManifestFile("added.bin", "1", 1);

        var result = ManifestDiffCalculator.GameManifestDiff(
            [unchanged, changed, removed],
            [unchanged, CreateManifestFile("changed.bin", "2", 2), added]);

        Assert.Equal(["added.bin", "changed.bin"], result.NeedDownload.Select(file => file.Path).Order());
        Assert.Equal(["removed.bin"], result.NeedDelete.Select(file => file.Path));
    }

    [Fact]
    public void GameResultMerge_WhenPlansOverlap_PreservesDeleteAndDeduplicatesPath()
    {
        var shared = CreateManifestFile("shared.bin", "1", 1);
        var result = ManifestDiffCalculator.GameResultMerge(
            new DownloadPlan { NeedDelete = [shared] },
            new DownloadPlan { NeedDownload = [CreateManifestFile("shared.bin", "2", 2), CreateManifestFile("new.bin", "3", 3)] });

        Assert.Equal(["shared.bin"], result.NeedDelete.Select(file => file.Path));
        Assert.Equal(["new.bin"], result.NeedDownload.Select(file => file.Path));
    }

    [Fact]
    public async Task CheckStat_WhenFileIsMissingOrWrongSize_ReturnsBothAndReportsProgress()
    {
        Directory.CreateDirectory(tempDir);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(tempDir, "valid.bin"), [1]);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(tempDir, "wrong.bin"), [1]);
        var progress = new List<int>();

        var result = ManifestDiffCalculator.CheckStat(
            [CreateManifestFile("valid.bin", "1", 1), CreateManifestFile("wrong.bin", "2", 2), CreateManifestFile("missing.bin", "3", 3)],
            tempDir,
            progress.Add);

        Assert.Equal(["missing.bin", "wrong.bin"], result.Select(file => file.Path).Order());
        Assert.Equal([33, 67, 100], progress);
    }

    [Fact]
    public void GameManifestDiff_WhenOnlyHashChanges_SamePathAndSize_RequiresDownloadOnly()
    {
        // 同名同大小但哈希不同：内容已变化，必须重新下载，且不得进入删除集。
        var result = ManifestDiffCalculator.GameManifestDiff(
            [CreateManifestFile("data.bin", "1", 5)],
            [CreateManifestFile("data.bin", "2", 5)]);

        Assert.Equal(["data.bin"], result.NeedDownload.Select(file => file.Path));
        Assert.Empty(result.NeedDelete);
    }

    [Fact]
    public void GameManifestDiff_WhenPathDiffersOnlyByCase_TreatsAsDeleteAndDownload()
    {
        // 路径比较采用 StringComparer.Ordinal：仅大小写不同视为两个不同文件
        // （旧路径进删除集，新路径进下载集）。
        var result = ManifestDiffCalculator.GameManifestDiff(
            [CreateManifestFile("Data.BIN", "1", 1)],
            [CreateManifestFile("data.bin", "1", 1)]);

        Assert.Equal(["Data.BIN"], result.NeedDelete.Select(file => file.Path));
        Assert.Equal(["data.bin"], result.NeedDownload.Select(file => file.Path));
    }

    [Fact]
    public void GameManifestDiff_WhenFileIsRenamed_OldGoesToDeleteAndNewToDownload()
    {
        var result = ManifestDiffCalculator.GameManifestDiff(
            [CreateManifestFile("old/old.bin", "1", 3)],
            [CreateManifestFile("new/new.bin", "1", 3)]);

        Assert.Equal(["old/old.bin"], result.NeedDelete.Select(file => file.Path));
        Assert.Equal(["new/new.bin"], result.NeedDownload.Select(file => file.Path));
    }

    [Fact]
    public void GameManifestDiff_WhenManifestsAreIdentical_ReturnsEmptyPlan()
    {
        var files = new[] { CreateManifestFile("a.bin", "1", 1), CreateManifestFile("b.bin", "2", 2) };

        var result = ManifestDiffCalculator.GameManifestDiff(files, files);

        Assert.Empty(result.NeedDownload);
        Assert.Empty(result.NeedDelete);
    }

    [Fact]
    public void GameManifestDiff_WhenOldListIsEmpty_AllNewFilesAreDownloaded()
    {
        var result = ManifestDiffCalculator.GameManifestDiff(
            [],
            [CreateManifestFile("a.bin", "1", 1), CreateManifestFile("b.bin", "2", 2)]);

        Assert.Equal(["a.bin", "b.bin"], result.NeedDownload.Select(file => file.Path).Order());
        Assert.Empty(result.NeedDelete);
    }

    [Fact]
    public void GameManifestDiff_WhenNewListIsEmpty_AllOldFilesAreDeleted()
    {
        var result = ManifestDiffCalculator.GameManifestDiff(
            [CreateManifestFile("a.bin", "1", 1), CreateManifestFile("b.bin", "2", 2)],
            []);

        Assert.Empty(result.NeedDownload);
        Assert.Equal(["a.bin", "b.bin"], result.NeedDelete.Select(file => file.Path).Order());
    }

    [Fact]
    public void GameManifestDiff_WhenBothListsAreEmpty_ReturnsEmptyPlan()
    {
        var result = ManifestDiffCalculator.GameManifestDiff([], []);

        Assert.Empty(result.NeedDownload);
        Assert.Empty(result.NeedDelete);
    }

    [Fact]
    public void CheckStat_WhenManifestIsEmpty_ReturnsEmptyDiffWithoutProgress()
    {
        var progressCalls = 0;

        var result = ManifestDiffCalculator.CheckStat([], tempDir, _ => progressCalls++);

        // 空清单：不访问磁盘、不触发进度回调，直接返回空差异。
        Assert.Empty(result);
        Assert.Equal(0, progressCalls);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static ManifestFile CreateManifestFile(string path, string hash, long size) => new()
    {
        Path = path,
        Hash = hash,
        Size = size.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
}
