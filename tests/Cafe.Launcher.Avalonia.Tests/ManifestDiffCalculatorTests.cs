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
