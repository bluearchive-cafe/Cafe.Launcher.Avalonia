using System.Text;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LevelDbReaderTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public LevelDbReaderTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public void TryReadValues_EmptyDirectory_ReturnsEmptyDict()
    {
        var results = LevelDbReader.TryReadValues(tempDir);

        Assert.Empty(results);
    }

    [Fact]
    public void TryReadValues_NonExistentDirectory_ReturnsEmptyDict()
    {
        var results = LevelDbReader.TryReadValues(Path.Combine(tempDir, "nonexistent"));

        Assert.Empty(results);
    }

    [Fact]
    public void TryReadValues_LdbFileWithKnownKey_ExtractsValue()
    {
        var downloadPathBytes = Encoding.UTF8.GetBytes("_downloadPath\x01");
        var valueBytes = Encoding.UTF8.GetBytes(@"C:\YostarGames\BlueArchive_JP");
        var ldbContent = new byte[downloadPathBytes.Length + valueBytes.Length + 1];
        Array.Copy(downloadPathBytes, 0, ldbContent, 0, downloadPathBytes.Length);
        Array.Copy(valueBytes, 0, ldbContent, downloadPathBytes.Length, valueBytes.Length);
        // Null terminator at the end
        ldbContent[^1] = 0x00;

        File.WriteAllBytes(Path.Combine(tempDir, "000001.ldb"), ldbContent);

        var results = LevelDbReader.TryReadValues(tempDir);

        Assert.NotEmpty(results);
        Assert.True(results.ContainsKey("downloadPath"));
        Assert.Contains(@"YostarGames\BlueArchive_JP", results["downloadPath"]);
    }

    [Fact]
    public void TryReadValues_LogFileWithKnownKey_ExtractsValue()
    {
        var proxyKeyBytes = Encoding.UTF8.GetBytes("_proxy-config\x01");
        var valueBytes = Encoding.UTF8.GetBytes("system");
        var logContent = new byte[proxyKeyBytes.Length + valueBytes.Length];
        Array.Copy(proxyKeyBytes, 0, logContent, 0, proxyKeyBytes.Length);
        Array.Copy(valueBytes, 0, logContent, proxyKeyBytes.Length, valueBytes.Length);

        File.WriteAllBytes(Path.Combine(tempDir, "000001.log"), logContent);

        var results = LevelDbReader.TryReadValues(tempDir);

        Assert.NotEmpty(results);
        Assert.True(results.ContainsKey("proxy-config"));
        Assert.Equal("system", results["proxy-config"]);
    }

    [Fact]
    public void TryReadValues_MultipleKeys_ExtractsAll()
    {
        var proxyKeyBytes = Encoding.UTF8.GetBytes("_proxy-config\x01");
        var proxyValueBytes = Encoding.UTF8.GetBytes("direct\x00");
        var proxyEntry = new byte[proxyKeyBytes.Length + proxyValueBytes.Length];
        Array.Copy(proxyKeyBytes, 0, proxyEntry, 0, proxyKeyBytes.Length);
        Array.Copy(proxyValueBytes, 0, proxyEntry, proxyKeyBytes.Length, proxyValueBytes.Length);

        var closeKeyBytes = Encoding.UTF8.GetBytes("_close-choice\x01");
        var closeValueBytes = Encoding.UTF8.GetBytes("minimize\x00");
        var closeEntry = new byte[closeKeyBytes.Length + closeValueBytes.Length];
        Array.Copy(closeKeyBytes, 0, closeEntry, 0, closeKeyBytes.Length);
        Array.Copy(closeValueBytes, 0, closeEntry, closeKeyBytes.Length, closeValueBytes.Length);

        var combined = new byte[proxyEntry.Length + closeEntry.Length];
        Array.Copy(proxyEntry, 0, combined, 0, proxyEntry.Length);
        Array.Copy(closeEntry, 0, combined, proxyEntry.Length, closeEntry.Length);

        File.WriteAllBytes(Path.Combine(tempDir, "000001.ldb"), combined);

        var results = LevelDbReader.TryReadValues(tempDir);

        Assert.Equal(2, results.Count);
        Assert.Equal("direct", results["proxy-config"]);
        Assert.Equal("minimize", results["close-choice"]);
    }

    [Fact]
    public void TryReadValues_InvalidValue_NotReturned()
    {
        var proxyKeyBytes = Encoding.UTF8.GetBytes("_proxy-config\x01");
        var valueBytes = Encoding.UTF8.GetBytes("invalid_value");  // not "direct" or "system"
        var ldbContent = new byte[proxyKeyBytes.Length + valueBytes.Length + 1];
        Array.Copy(proxyKeyBytes, 0, ldbContent, 0, proxyKeyBytes.Length);
        Array.Copy(valueBytes, 0, ldbContent, proxyKeyBytes.Length, valueBytes.Length);
        ldbContent[^1] = 0x00;

        File.WriteAllBytes(Path.Combine(tempDir, "000001.ldb"), ldbContent);

        var results = LevelDbReader.TryReadValues(tempDir);

        // Invalid proxy value should not be returned
        Assert.False(results.ContainsKey("proxy-config"));
    }

    [Fact]
    public void TryReadValues_EmptyFile_ReturnsEmptyDict()
    {
        File.WriteAllBytes(Path.Combine(tempDir, "000001.ldb"), []);

        var results = LevelDbReader.TryReadValues(tempDir);

        Assert.Empty(results);
    }

    [Fact]
    public void TryReadValues_NoMatchingKeys_ReturnsEmptyDict()
    {
        var unrelatedBytes = Encoding.UTF8.GetBytes("some_other_data_not_matching_anything");
        File.WriteAllBytes(Path.Combine(tempDir, "000001.ldb"), unrelatedBytes);

        var results = LevelDbReader.TryReadValues(tempDir);

        Assert.Empty(results);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
