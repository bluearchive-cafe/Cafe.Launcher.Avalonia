using System.Text.Json;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherSettingsServiceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string settingsPath;

    public LauncherSettingsServiceTests()
    {
        settingsPath = Path.Combine(tempDir, "settings.json");
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public async Task ReadAsync_WhenFileMissing_ReturnsDefaults()
    {
        var service = new LauncherSettingsService(settingsPath);

        var settings = await service.ReadAsync();

        Assert.Equal("", settings.GamePath);
        Assert.Equal(LaunchCheckModes.LocalManifest, settings.LaunchCheckMode);
        Assert.Equal(ProxyModes.Direct, settings.ProxyMode);
        Assert.Equal(CloseBehaviors.Minimize, settings.CloseBehavior);
        Assert.Equal(LauncherLanguages.Auto, settings.Language);
        Assert.Equal(ThemeModes.System, settings.ThemeMode);
        Assert.Equal(ThemeColorModes.Default, settings.ThemeColorMode);
        Assert.Equal(LauncherConstants.DefaultThemeColor, settings.CustomThemeColor);
        Assert.Empty(settings.ThemeColorPalette);
        Assert.Equal(0, settings.SelectedThemeColorPaletteIndex);
        Assert.Equal(DownloadSpeedLimits.Unlimited, settings.DownloadSpeedLimit);
        Assert.True(settings.ToastNotificationsEnabled);
        Assert.True(settings.ShowRemoteContentCard);
        Assert.Equal(PatchUrlGroups.Official, settings.PatchUrlGroup);
        Assert.Equal("", settings.CustomBackgroundPath);
        Assert.Equal(BackgroundSources.Bundled, settings.BackgroundSource);
        Assert.Equal("", settings.ResourcePanelUid);
    }

    [Fact]
    public async Task ReadAsync_WhenLegacyFieldsExist_UsesExactLegacyNames()
    {
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "GamePath": "D:\\Games",
              "LaunchCheckMode": "RemoteManifest"
            }
            """);
        var service = new LauncherSettingsService(settingsPath);

        var settings = await service.ReadAsync();

        Assert.Equal(@"D:\Games", settings.GamePath);
        Assert.Equal(LaunchCheckModes.RemoteManifest, settings.LaunchCheckMode);
    }

    [Fact]
    public async Task ReadAsync_WhenValuesInvalid_NormalizesToDefaults()
    {
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "gamePath": null,
              "launchCheckMode": "invalid",
              "proxyMode": "invalid",
              "closeBehavior": "invalid",
              "language": "invalid",
              "themeMode": "invalid",
              "themeColorMode": "invalid",
              "customThemeColor": "invalid",
              "themeColorPalette": ["#ff112233", "invalid", "#445566"],
              "selectedThemeColorPaletteIndex": 99,
              "downloadSpeedLimit": "invalid",
              "toastNotificationsEnabled": false,
              "showRemoteContentCard": false,
              "patchUrlGroup": "invalid",
              "backgroundSource": "invalid",
              "resourcePanelUid": "  UID123  "
            }
            """);
        var service = new LauncherSettingsService(settingsPath);

        var settings = await service.ReadAsync();

        Assert.Equal("", settings.GamePath);
        Assert.Equal(LaunchCheckModes.LocalManifest, settings.LaunchCheckMode);
        Assert.Equal(ProxyModes.Direct, settings.ProxyMode);
        Assert.Equal(CloseBehaviors.Minimize, settings.CloseBehavior);
        Assert.Equal(LauncherLanguages.Auto, settings.Language);
        Assert.Equal(ThemeModes.System, settings.ThemeMode);
        Assert.Equal(ThemeColorModes.Default, settings.ThemeColorMode);
        Assert.Equal(LauncherConstants.DefaultThemeColor, settings.CustomThemeColor);
        Assert.Equal(["#FF112233", "#FF445566"], settings.ThemeColorPalette);
        Assert.Equal(0, settings.SelectedThemeColorPaletteIndex);
        Assert.Equal(DownloadSpeedLimits.Unlimited, settings.DownloadSpeedLimit);
        Assert.False(settings.ToastNotificationsEnabled);
        Assert.False(settings.ShowRemoteContentCard);
        Assert.Equal(PatchUrlGroups.Official, settings.PatchUrlGroup);
        Assert.Equal(BackgroundSources.Bundled, settings.BackgroundSource);
        Assert.Equal("UID123", settings.ResourcePanelUid);
    }

    [Fact]
    public async Task SaveAsync_WritesExactCurrentJsonFieldNames()
    {
        var service = new LauncherSettingsService(settingsPath);
        var settings = new LauncherSettings
        {
            GamePath = @"D:\YostarGames\BlueArchive_JP",
            LaunchCheckMode = LaunchCheckModes.RemoteManifest,
            ProxyMode = ProxyModes.System,
            CloseBehavior = CloseBehaviors.Exit,
            Language = LauncherLanguages.Japanese,
            ThemeMode = ThemeModes.Dark,
            ThemeColorMode = ThemeColorModes.Custom,
            CustomThemeColor = "#FF336699",
            ThemeColorPalette = ["#FF112233", "#FF445566"],
            SelectedThemeColorPaletteIndex = 1,
            DownloadSpeedLimit = DownloadSpeedLimits.Speed10MBs,
            ToastNotificationsEnabled = false,
            ShowRemoteContentCard = false,
            PatchUrlGroup = PatchUrlGroups.Cafe,
            CustomBackgroundPath = tempDir,
            BackgroundSource = BackgroundSources.Remote,
            BackgroundFit = BackgroundFits.Fill,
            BackgroundFillColor = "#FF112233",
            ResourcePanelUid = "UID123"
        };

        await service.SaveAsync(settings);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var root = document.RootElement;
        var propertyNames = root.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(root.TryGetProperty("gamePath", out _));
        Assert.True(root.TryGetProperty("launchCheckMode", out _));
        Assert.True(root.TryGetProperty("proxyMode", out _));
        Assert.True(root.TryGetProperty("closeBehavior", out _));
        Assert.True(root.TryGetProperty("language", out _));
        Assert.True(root.TryGetProperty("themeMode", out _));
        Assert.True(root.TryGetProperty("themeColorMode", out var themeColorMode));
        Assert.Equal(ThemeColorModes.Custom, themeColorMode.GetString());
        Assert.True(root.TryGetProperty("customThemeColor", out var customThemeColor));
        Assert.Equal("#FF336699", customThemeColor.GetString());
        Assert.True(root.TryGetProperty("themeColorPalette", out var themeColorPalette));
        Assert.Equal(["#FF112233", "#FF445566"], themeColorPalette.EnumerateArray().Select(item => item.GetString()));
        Assert.True(root.TryGetProperty("selectedThemeColorPaletteIndex", out var selectedThemeColorPaletteIndex));
        Assert.Equal(1, selectedThemeColorPaletteIndex.GetInt32());
        Assert.True(root.TryGetProperty("downloadSpeedLimit", out _));
        Assert.True(root.TryGetProperty("toastNotificationsEnabled", out _));
        Assert.True(root.TryGetProperty("showRemoteContentCard", out _));
        Assert.True(root.TryGetProperty("patchUrlGroup", out _));
        Assert.True(root.TryGetProperty("customBackgroundPath", out var customBackgroundPath));
        Assert.Equal(tempDir, customBackgroundPath.GetString());
        Assert.True(root.TryGetProperty("backgroundSource", out _));
        Assert.True(root.TryGetProperty("backgroundFit", out var backgroundFit));
        Assert.Equal(BackgroundFits.Fill, backgroundFit.GetString());
        Assert.True(root.TryGetProperty("backgroundFillColor", out var backgroundFillColor));
        Assert.Equal("#FF112233", backgroundFillColor.GetString());
        Assert.True(root.TryGetProperty("resourcePanelUid", out var resourcePanelUid));
        Assert.Equal("UID123", resourcePanelUid.GetString());
        var expectedPropertyNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "gamePath",
            "launchCheckMode",
            "proxyMode",
            "closeBehavior",
            "language",
            "themeMode",
            "themeColorMode",
            "customThemeColor",
            "themeColorPalette",
            "selectedThemeColorPaletteIndex",
            "downloadSpeedLimit",
            "toastNotificationsEnabled",
            "showRemoteContentCard",
            "patchUrlGroup",
            "customBackgroundPath",
            "backgroundSource",
            "backgroundFit",
            "backgroundFillColor",
            "videoBackgroundPath",
            "videoBackgroundMuted",
            "videoBackgroundVolume",
            "resourcePanelUid",
            "hasCompletedFirstLaunchWizard",
            "updateChannel",
            "logLevel"
        };
        Assert.True(expectedPropertyNames.SetEquals(propertyNames));
        Assert.False(File.Exists($"{settingsPath}.tmp"));
    }

    [Fact]
    public async Task SaveAsync_WhenCalledConcurrently_LeavesOneCompleteSettingsDocument()
    {
        var service = new LauncherSettingsService(settingsPath);
        var writes = Enumerable.Range(0, 32)
            .Select(index => service.SaveAsync(new LauncherSettings
            {
                GamePath = $@"D:\Games\{index}",
                ResourcePanelUid = $"UID{index}"
            }));

        await Task.WhenAll(writes);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var root = document.RootElement;
        var gamePath = root.GetProperty("gamePath").GetString();
        var resourcePanelUid = root.GetProperty("resourcePanelUid").GetString();
        Assert.NotNull(gamePath);
        Assert.NotNull(resourcePanelUid);
        Assert.Equal(gamePath!["D:\\Games\\".Length..], resourcePanelUid!["UID".Length..]);
        Assert.Empty(Directory.EnumerateFiles(tempDir, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
