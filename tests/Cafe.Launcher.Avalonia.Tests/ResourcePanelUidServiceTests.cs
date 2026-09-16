using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ResourcePanelUidServiceTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public async Task ResolveUidAsync_WhenCookieContainsUid_ReturnsCookieUid()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await BestHttpCookieLibraryFixture.WriteUidAsync(cookiePath, "COOKIEAA");
        var savedSettings = new SavedSettingsTestRig(Path.Combine(tempDir, "settings.json"));
        await savedSettings.SeedAsync(new LauncherSettings { ResourcePanelUid = "SETTINGA" });
        var service = new ResourcePanelUidService(new BestHttpCookieLibraryService(), savedSettings.SettingsService, savedSettings.Writer, cookiePath);

        var uid = await service.ResolveUidAsync();

        Assert.Equal("COOKIEAA", uid);
    }

    [Fact]
    public async Task ResolveUidAsync_WhenCookieMissing_ReturnsSettingsUid()
    {
        var savedSettings = new SavedSettingsTestRig(Path.Combine(tempDir, "settings.json"));
        await savedSettings.SeedAsync(new LauncherSettings { ResourcePanelUid = "SETTINGA" });
        var service = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            savedSettings.SettingsService,
            savedSettings.Writer,
            Path.Combine(tempDir, "missing"));

        var uid = await service.ResolveUidAsync();

        Assert.Equal("SETTINGA", uid);
    }

    [Fact]
    public async Task ResolveUidAsync_WhenUidCookieDomainDoesNotMatch_ReturnsSettingsUid()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await BestHttpCookieLibraryFixture.WriteUidAsync(cookiePath, "COOKIEAA", "example.com", "/");
        var savedSettings = new SavedSettingsTestRig(Path.Combine(tempDir, "settings.json"));
        await savedSettings.SeedAsync(new LauncherSettings { ResourcePanelUid = "SETTINGA" });
        var service = new ResourcePanelUidService(new BestHttpCookieLibraryService(), savedSettings.SettingsService, savedSettings.Writer, cookiePath);

        var uid = await service.ResolveUidAsync();

        Assert.Equal("SETTINGA", uid);
    }

    [Fact]
    public async Task ResolveUidAsync_WhenNoUidExists_ReturnsEmptyString()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await BestHttpCookieLibraryFixture.WriteUidAsync(cookiePath, "");
        var savedSettings = new SavedSettingsTestRig(Path.Combine(tempDir, "settings.json"));
        var service = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            savedSettings.SettingsService,
            savedSettings.Writer,
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
        await BestHttpCookieLibraryFixture.WriteUidAsync(cookiePath, "invalid");
        var savedSettings = new SavedSettingsTestRig(Path.Combine(tempDir, "settings.json"));
        await savedSettings.SeedAsync(new LauncherSettings { ResourcePanelUid = "SETTINGA" });
        var service = new ResourcePanelUidService(new BestHttpCookieLibraryService(), savedSettings.SettingsService, savedSettings.Writer, cookiePath);

        var uid = await service.ResolveUidAsync();

        Assert.Equal("SETTINGA", uid);
    }

    [Fact]
    public async Task ResolveUidAsync_WhenBothCookieAndSettingsAreInvalid_ReturnsEmpty()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await BestHttpCookieLibraryFixture.WriteUidAsync(cookiePath, "bad");
        var savedSettings = new SavedSettingsTestRig(Path.Combine(tempDir, "settings.json"));
        await savedSettings.SeedAsync(new LauncherSettings { ResourcePanelUid = "also-bad" });
        var service = new ResourcePanelUidService(new BestHttpCookieLibraryService(), savedSettings.SettingsService, savedSettings.Writer, cookiePath);

        var uid = await service.ResolveUidAsync();

        Assert.Equal("", uid);
    }

    [Fact]
    public async Task SaveManualUidAsync_WhenUidHasInvalidFormat_Throws()
    {
        var savedSettings = new SavedSettingsTestRig(Path.Combine(tempDir, "settings.json"));
        var service = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            savedSettings.SettingsService,
            savedSettings.Writer,
            Path.Combine(tempDir, "missing"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveManualUidAsync("bad-uid"));
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
