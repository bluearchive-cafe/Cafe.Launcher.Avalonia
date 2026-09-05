using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class Crc64ServiceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public Crc64ServiceTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public async Task ComputeFileAsync_WhenCanonicalCrc64XzVector_MatchesSpecCheckValue()
    {
        // CRC-64/XZ 规范校验值：check("123456789") = 0x995DC9BBDF1939FA。
        var filePath = Path.Combine(tempDir, "canonical.txt");
        await File.WriteAllBytesAsync(filePath, "123456789"u8.ToArray());
        var service = new Crc64Service();

        var result = await service.ComputeFileAsync(filePath);

        Assert.Equal("11051210869376104954", result);
    }

    [Theory]
    [InlineData(1, 2582559429254684254UL)]
    [InlineData(7, 5556657517272146431UL)]
    [InlineData(8, 12222807979173201616UL)]
    [InlineData(9, 3452146847344984940UL)]
    [InlineData(16, 16365817571135009565UL)]
    [InlineData(63, 11021987937096340637UL)]
    [InlineData(65, 16847853975378864881UL)]
    [InlineData(1048581, 3926898253094299527UL)]
    public async Task ComputeFileAsync_WhenLengthCrossesSlicingBlocks_MatchesIndependentReference(
        int length,
        ulong expected)
    {
        // 向量由独立参考实现（逐字节 CRC-64/XZ）生成，覆盖 8 字节分块的全部
        // 边界——slicing-by-8 必须与逐位算法一致；1048581 跨越 1MB 读缓冲。
        var filePath = Path.Combine(tempDir, $"len-{length}.bin");
        await File.WriteAllBytesAsync(
            filePath,
            Enumerable.Range(0, length).Select(i => (byte)((i * 31 + 7) & 0xFF)).ToArray());
        var service = new Crc64Service();

        var result = await service.ComputeFileAsync(filePath);

        Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), result);
    }

    [Fact]
    public async Task ComputeFileAsync_WhenFileIsEmpty_ReturnsZero()
    {
        var filePath = Path.Combine(tempDir, "empty.bin");
        await File.WriteAllBytesAsync(filePath, []);
        var service = new Crc64Service();

        var result = await service.ComputeFileAsync(filePath);

        Assert.Equal("0", result);
    }

    [Fact]
    public async Task ComputeFileAsync_WhenComputedTwice_ReturnsSameHash()
    {
        var filePath = Path.Combine(tempDir, "data.bin");
        await File.WriteAllTextAsync(filePath, "Hello, World!");
        var service = new Crc64Service();

        var first = await service.ComputeFileAsync(filePath);
        var second = await service.ComputeFileAsync(filePath);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ComputeFileAsync_WhenFileHasContent_ReturnsNonZeroString()
    {
        var filePath = Path.Combine(tempDir, "data.bin");
        await File.WriteAllTextAsync(filePath, "Test content for CRC64 verification.");
        var service = new Crc64Service();

        var result = await service.ComputeFileAsync(filePath);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.NotEqual("0", result);
    }

    [Fact]
    public async Task ComputeFileAsync_WhenSameContentDifferentFiles_ReturnsSameHash()
    {
        var fileA = Path.Combine(tempDir, "a.bin");
        var fileB = Path.Combine(tempDir, "b.bin");
        var content = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(fileA, content);
        await File.WriteAllBytesAsync(fileB, content);
        var service = new Crc64Service();

        var hashA = await service.ComputeFileAsync(fileA);
        var hashB = await service.ComputeFileAsync(fileB);

        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public async Task ComputeFileAsync_WhenContentDiffers_ReturnsDifferentHash()
    {
        var fileA = Path.Combine(tempDir, "a.bin");
        var fileB = Path.Combine(tempDir, "b.bin");
        await File.WriteAllBytesAsync(fileA, [1, 2, 3]);
        await File.WriteAllBytesAsync(fileB, [4, 5, 6]);
        var service = new Crc64Service();

        var hashA = await service.ComputeFileAsync(fileA);
        var hashB = await service.ComputeFileAsync(fileB);

        Assert.NotEqual(hashA, hashB);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }
}
