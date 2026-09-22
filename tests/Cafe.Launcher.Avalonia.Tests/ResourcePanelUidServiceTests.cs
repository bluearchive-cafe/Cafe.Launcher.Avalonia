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

    [Theory]
    [InlineData(GameRuntimeRunners.Auto, GameRuntimeRunners.Umu, "gytxtx")]
    [InlineData(GameRuntimeRunners.Auto, GameRuntimeRunners.Wine, "steamuser")]
    [InlineData(GameRuntimeRunners.Wine, GameRuntimeRunners.Wine, "wineuser")]
    public async Task ResolveLinuxCookieLibraryPath_ManagedPrefix_FindsExistingLibrary(
        string selectedRunner, string installedRunner, string profile)
    {
        var expected = CreateCookiePath(Path.Combine(tempDir, installedRunner, "prefix"), profile);
        await BestHttpCookieLibraryFixture.WriteUidAsync(expected, "COOKIEAA");

        var actual = ResourcePanelUidService.ResolveLinuxCookieLibraryPath(
            new GameRuntimeSettings { Runner = selectedRunner }, "gytxtx",
            runner => Path.Combine(tempDir, runner, "prefix"));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveLinuxCookieLibraryPath_MissingCustomPrefix_DoesNotUseManagedPrefix()
    {
        var prefix = Path.Combine(tempDir, "custom");
        var actual = ResourcePanelUidService.ResolveLinuxCookieLibraryPath(
            new GameRuntimeSettings { PrefixPath = prefix }, "gytxtx",
            _ => throw new InvalidOperationException("Custom prefix must take precedence."));

        Assert.Equal(CreateCookiePath(prefix, "gytxtx"), actual);
    }

    [Fact]
    public async Task ResolveLinuxCookieLibraryPath_ExplicitRunner_DoesNotReadOtherRunner()
    {
        var umuPrefix = Path.Combine(tempDir, GameRuntimeRunners.Umu);
        await BestHttpCookieLibraryFixture.WriteUidAsync(CreateCookiePath(umuPrefix, "gytxtx"), "COOKIEAA");

        var actual = ResourcePanelUidService.ResolveLinuxCookieLibraryPath(
            new GameRuntimeSettings { Runner = GameRuntimeRunners.Wine }, "gytxtx",
            runner => Path.Combine(tempDir, runner));

        Assert.Equal(CreateCookiePath(Path.Combine(tempDir, GameRuntimeRunners.Wine), "gytxtx"), actual);
    }

    [Fact]
    public async Task ResolveUidAsync_LinuxPrefixChanges_ReadsNewLibraryAndUpdatesDisplayedPath()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux compatibility prefix resolution.");
        var savedSettings = new SavedSettingsTestRig(Path.Combine(tempDir, "settings.json"));
        var service = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(), savedSettings.SettingsService, savedSettings.Writer);
        foreach (var uid in new[] { "COOKIEAA", "COOKIEBB" })
        {
            var prefix = Path.Combine(tempDir, uid);
            var cookiePath = CreateCookiePath(prefix, "steamuser");
            await BestHttpCookieLibraryFixture.WriteUidAsync(cookiePath, uid);
            await savedSettings.SeedAsync(new LauncherSettings
            {
                GameRuntime = new GameRuntimeSettings { PrefixPath = prefix }
            });

            Assert.Equal(uid, await service.ResolveUidAsync());
            Assert.Equal(cookiePath, service.CookieLibraryPath);
        }
    }

    private static string CreateCookiePath(string prefix, string profile)
    {
        var directory = Path.Combine(prefix, "drive_c", "users", profile,
            "AppData", "LocalLow", "YostarJP", "BlueArchive", "Cookies");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "Library");
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
