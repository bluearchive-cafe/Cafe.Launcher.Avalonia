using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ResourcePanelUidServiceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ResourcePanelUidServiceTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public async Task ResolveUidAsync_WhenCookieContainsUid_ReturnsCookieUid()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await WriteCookieLibraryAsync(cookiePath, "COOKIE_UID");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ResourcePanelUid = "SETTINGS_UID" });
        var service = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);

        var uid = await service.ResolveUidAsync();

        Assert.Equal("COOKIE_UID", uid);
    }

    [Fact]
    public async Task ResolveUidAsync_WhenCookieMissing_ReturnsSettingsUid()
    {
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ResourcePanelUid = "SETTINGS_UID" });
        var service = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));

        var uid = await service.ResolveUidAsync();

        Assert.Equal("SETTINGS_UID", uid);
    }

    [Fact]
    public async Task ResolveUidAsync_WhenNoUidExists_ReturnsEmptyString()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await WriteCookieLibraryAsync(cookiePath, "");
        var service = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            new LauncherSettingsService(Path.Combine(tempDir, "settings.json")),
            cookiePath);

        var uid = await service.ResolveUidAsync();

        Assert.Equal("", uid);
    }

    private static async Task WriteCookieLibraryAsync(string path, string uid)
    {
        await using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(1);
        writer.Write(string.IsNullOrEmpty(uid) ? 0 : 1);
        if (string.IsNullOrEmpty(uid))
        {
            await stream.FlushAsync();
            return;
        }

        writer.Write(1);
        writer.Write("uid");
        writer.Write(uid);
        writer.Write(DateTime.UtcNow.ToBinary());
        writer.Write(DateTime.UtcNow.ToBinary());
        writer.Write(DateTime.FromBinary(0).ToBinary());
        writer.Write(2147483647L);
        writer.Write(false);
        writer.Write("bluearchive.cafe");
        writer.Write("/");
        writer.Write(false);
        writer.Write(false);
        writer.Flush();
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
