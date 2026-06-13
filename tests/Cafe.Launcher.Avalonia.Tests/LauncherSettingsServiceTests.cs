using System.Text.Json;
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
        Assert.Equal(DownloadSpeedLimits.Unlimited, settings.DownloadSpeedLimit);
        Assert.True(settings.ToastNotificationsEnabled);
        Assert.True(settings.ShowRemoteContentCard);
        Assert.Equal(PatchUrlGroups.Official, settings.PatchUrlGroup);
        Assert.Equal("", settings.CustomBackgroundPath);
        Assert.Equal(BackgroundSources.Bundled, settings.BackgroundSource);
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
              "downloadSpeedLimit": "invalid",
              "toastNotificationsEnabled": false,
              "showRemoteContentCard": false,
              "patchUrlGroup": "invalid",
              "backgroundSource": "invalid"
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
        Assert.Equal(DownloadSpeedLimits.Unlimited, settings.DownloadSpeedLimit);
        Assert.False(settings.ToastNotificationsEnabled);
        Assert.False(settings.ShowRemoteContentCard);
        Assert.Equal(PatchUrlGroups.Official, settings.PatchUrlGroup);
        Assert.Equal(BackgroundSources.Bundled, settings.BackgroundSource);
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
            DownloadSpeedLimit = DownloadSpeedLimits._10MBs,
            ToastNotificationsEnabled = false,
            ShowRemoteContentCard = false,
            PatchUrlGroup = PatchUrlGroups.Cafe,
            BackgroundSource = BackgroundSources.Remote
        };

        await service.SaveAsync(settings);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("gamePath", out _));
        Assert.True(root.TryGetProperty("launchCheckMode", out _));
        Assert.True(root.TryGetProperty("proxyMode", out _));
        Assert.True(root.TryGetProperty("closeBehavior", out _));
        Assert.True(root.TryGetProperty("language", out _));
        Assert.True(root.TryGetProperty("themeMode", out _));
        Assert.True(root.TryGetProperty("downloadSpeedLimit", out _));
        Assert.True(root.TryGetProperty("toastNotificationsEnabled", out _));
        Assert.True(root.TryGetProperty("showRemoteContentCard", out _));
        Assert.True(root.TryGetProperty("patchUrlGroup", out _));
        Assert.True(root.TryGetProperty("backgroundSource", out _));
        Assert.False(File.Exists($"{settingsPath}.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
