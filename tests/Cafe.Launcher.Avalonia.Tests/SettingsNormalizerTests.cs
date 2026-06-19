using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class SettingsNormalizerTests
{
    [Fact]
    public void Normalize_WhenValuesInvalid_AppliesExactDefaults()
    {
        var normalizer = new SettingsNormalizer();
        var settings = new LauncherSettings
        {
            GamePath = null!,
            LaunchCheckMode = "invalid",
            ProxyMode = "invalid",
            CloseBehavior = "invalid",
            Language = "invalid",
            ThemeMode = "invalid",
            ThemeColorMode = "invalid",
            CustomThemeColor = "invalid",
            ThemeColorPalette = ["#ff112233", "invalid", "#445566", "#FF112233"],
            SelectedThemeColorPaletteIndex = 99,
            DownloadSpeedLimit = "invalid",
            PatchUrlGroup = "invalid",
            BackgroundSource = "invalid",
            BackgroundFit = "invalid",
            BackgroundFillColor = "#112233",
            UpdateChannel = "invalid",
            ResourcePanelUid = "  UID123  "
        };

        var result = normalizer.Normalize(settings);

        Assert.NotSame(settings, result);
        Assert.Equal("invalid", settings.ProxyMode);
        Assert.Equal("  UID123  ", settings.ResourcePanelUid);
        Assert.Equal("", result.GamePath);
        Assert.Equal(LaunchCheckModes.LocalManifest, result.LaunchCheckMode);
        Assert.Equal(ProxyModes.Direct, result.ProxyMode);
        Assert.Equal(CloseBehaviors.Minimize, result.CloseBehavior);
        Assert.Equal(LauncherLanguages.Auto, result.Language);
        Assert.Equal(ThemeModes.System, result.ThemeMode);
        Assert.Equal(ThemeColorModes.Default, result.ThemeColorMode);
        Assert.Equal(LauncherConstants.DefaultThemeColor, result.CustomThemeColor);
        Assert.Equal(["#FF112233", "#FF445566"], result.ThemeColorPalette);
        Assert.Equal(0, result.SelectedThemeColorPaletteIndex);
        Assert.Equal(DownloadSpeedLimits.Unlimited, result.DownloadSpeedLimit);
        Assert.Equal(PatchUrlGroups.Official, result.PatchUrlGroup);
        Assert.Equal(BackgroundSources.Bundled, result.BackgroundSource);
        Assert.Equal(BackgroundFits.UniformToFill, result.BackgroundFit);
        Assert.Equal("#FF112233", result.BackgroundFillColor);
        Assert.Equal(UpdateChannels.Stable, result.UpdateChannel);
        Assert.Equal("UID123", result.ResourcePanelUid);
    }

    [Theory]
    [InlineData("LocalManifest", LaunchCheckModes.LocalManifest)]
    [InlineData("RemoteManifest", LaunchCheckModes.RemoteManifest)]
    [InlineData("None", LaunchCheckModes.None)]
    public void Normalize_WhenLegacyLaunchCheckMode_MapsExactValue(
        string value,
        string expected)
    {
        var normalizer = new SettingsNormalizer();

        var result = normalizer.Normalize(new LauncherSettings
        {
            LaunchCheckMode = value
        });

        Assert.Equal(expected, result.LaunchCheckMode);
    }
}
