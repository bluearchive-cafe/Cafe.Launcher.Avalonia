using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class NoticeStateServiceTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();
    private readonly string statePath;

    public NoticeStateServiceTests()
    {
        statePath = Path.Combine(tempDir, "notices.json");
    }

    [Fact]
    public async Task ReadShownNoticesAsync_WhenFileMissing_ReturnsEmpty()
    {
        var service = new NoticeStateService( TestDataRoot.ForFile(statePath) );

        var result = await service.ReadShownNoticesAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveAndRead_RoundTripsCorrectly()
    {
        var service = new NoticeStateService( TestDataRoot.ForFile(statePath) );

        await service.SaveShownNoticeAsync("hash1");
        await service.SaveShownNoticeAsync("hash2");
        await service.SaveShownNoticeAsync("hash1"); // duplicate

        var result = await service.ReadShownNoticesAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains("hash1", result);
        Assert.Contains("hash2", result);
    }

    [Fact]
    public async Task SaveShownNoticeAsync_WhenFileCorrupt_DoesNotThrow()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        await File.WriteAllTextAsync(statePath, "not valid json");
        var service = new NoticeStateService( TestDataRoot.ForFile(statePath) );

        // Should not throw — the read path catches JsonException
        var exception = await Record.ExceptionAsync(
            () => service.SaveShownNoticeAsync("hash1"));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ReadShownNoticesAsync_WhenFileCorrupt_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        await File.WriteAllTextAsync(statePath, "{{{broken json");
        var service = new NoticeStateService( TestDataRoot.ForFile(statePath) );

        var result = await service.ReadShownNoticesAsync();

        Assert.Empty(result);
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
