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
        await WriteCookieLibraryAsync(cookiePath, "COOKIEAA");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ResourcePanelUid = "SETTINGA" });
        var service = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);

        var uid = await service.ResolveUidAsync();

        Assert.Equal("COOKIEAA", uid);
    }

    [Fact]
    public async Task ResolveUidAsync_WhenCookieMissing_ReturnsSettingsUid()
    {
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ResourcePanelUid = "SETTINGA" });
        var service = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));

        var uid = await service.ResolveUidAsync();

        Assert.Equal("SETTINGA", uid);
    }

    [Fact]
    public async Task ResolveUidAsync_WhenUidCookieDomainDoesNotMatch_ReturnsSettingsUid()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await WriteCookieLibraryAsync(cookiePath, "COOKIEAA", "example.com", "/");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ResourcePanelUid = "SETTINGA" });
        var service = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);

        var uid = await service.ResolveUidAsync();

        Assert.Equal("SETTINGA", uid);
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

    [Theory]
    [InlineData("ABCDEFGH", true)]
    [InlineData("ZXYWVUTS", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("ABCDEFG", false)]
    [InlineData("ABCDEFGHI", false)]
    [InlineData("abcdefgh", false)]
    [InlineData("ABC12345", false)]
    [InlineData("ABCD-EFG", false)]
    [InlineData(" ABCDEFGH ", false)]
    public void IsValidUid_ValidatesEightUppercaseLetters(string? uid, bool expected)
    {
        Assert.Equal(expected, ResourcePanelUidService.IsValidUid(uid));
    }

    [Fact]
    public async Task ResolveUidAsync_WhenCookieUidHasInvalidFormat_FallsBackToSettings()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await WriteCookieLibraryAsync(cookiePath, "invalid");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ResourcePanelUid = "SETTINGA" });
        var service = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);

        var uid = await service.ResolveUidAsync();

        Assert.Equal("SETTINGA", uid);
    }

    [Fact]
    public async Task ResolveUidAsync_WhenBothCookieAndSettingsAreInvalid_ReturnsEmpty()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await WriteCookieLibraryAsync(cookiePath, "bad");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ResourcePanelUid = "also-bad" });
        var service = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);

        var uid = await service.ResolveUidAsync();

        Assert.Equal("", uid);
    }

    [Fact]
    public async Task SaveManualUidAsync_WhenUidHasInvalidFormat_Throws()
    {
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        var service = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveManualUidAsync("bad-uid"));
    }

    private static async Task WriteCookieLibraryAsync(
        string path,
        string uid,
        string domain = "bluearchive.cafe",
        string cookiePath = "/")
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
        writer.Write(domain);
        writer.Write(cookiePath);
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
