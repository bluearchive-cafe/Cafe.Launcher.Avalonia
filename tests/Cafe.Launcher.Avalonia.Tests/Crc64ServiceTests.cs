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
