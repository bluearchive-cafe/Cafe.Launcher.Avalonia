using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// <see cref="DirectorySizeProbe"/> 的口径（ADR-030）：只累加文件字节、跳过 reparse point、
/// 缺失与取不到的条目一律按 0 处理而不是抛——它是确认框上的展示数字，不该让对话框打不开。
/// </summary>
public sealed class DirectorySizeProbeTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Measure_WhenTreeHasNestedFiles_SumsEveryFileByte()
    {
        var root = Path.Combine(tempDir, "tree");
        Directory.CreateDirectory(Path.Combine(root, "data", "nested"));
        File.WriteAllText(Path.Combine(root, "a.txt"), "12345");
        File.WriteAllText(Path.Combine(root, "data", "b.txt"), "123");
        File.WriteAllText(Path.Combine(root, "data", "nested", "c.txt"), "1");

        Assert.Equal(9, DirectorySizeProbe.Measure(root));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Measure_WhenPathIsBlank_ReturnsZero(string? path) =>
        Assert.Equal(0, DirectorySizeProbe.Measure(path));

    [Fact]
    public void Measure_WhenPathDoesNotExist_ReturnsZero() =>
        Assert.Equal(0, DirectorySizeProbe.Measure(Path.Combine(tempDir, "missing")));

    [Fact]
    public void Measure_WhenTreeContainsReparsePoint_SkipsItAndKeepsCounting()
    {
        var root = Path.Combine(tempDir, "tree-with-link");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "counted.txt"), "1234");
        var outside = Path.Combine(tempDir, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "not-counted.txt"), "1234567890");
        TestSymlinks.CreateDirectorySymbolicLinkOrSkip(Path.Combine(root, "link"), outside);

        // 删除不会跟随 reparse point，测量必须与之一致，否则「显示多少」就不是「删多少」。
        Assert.Equal(4, DirectorySizeProbe.Measure(root));
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
