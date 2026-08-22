using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class SettingsOptionDescriptorTests : LocalizationTestBase
{
    static SettingsOptionDescriptorTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Theory]
    [MemberData(nameof(ExpectedSettingsOptions))]
    public void SettingsOptionsViewModel_EnumeratedOptions_KeepCurrentOrderAndLocalizedNames(
        string propertyName,
        string[] expectedCodes)
    {
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.English);
        var options = new SettingsOptionsViewModel(localizer, new DiskSpaceService());

        options.RefreshDisplayNames();

        var collection = Assert.IsAssignableFrom<IEnumerable<SelectableOption>>(
            typeof(SettingsOptionsViewModel).GetProperty(propertyName)!.GetValue(options));
        var actual = collection.ToList();
        Assert.Equal(expectedCodes, actual.Select(option => option.Code));
        Assert.All(actual, option => Assert.False(string.IsNullOrWhiteSpace(option.DisplayName)));
    }

    [Fact]
    public void RefreshDisplayNames_PreservesOptionCollectionInstances()
    {
        var options = new SettingsOptionsViewModel(new LocalizationService(), new DiskSpaceService());
        var theme = options.Theme;
        var downloadSpeedLimit = options.DownloadSpeedLimit;

        options.RefreshDisplayNames();
        options.RefreshDisplayNames();

        Assert.Same(theme, options.Theme);
        Assert.Same(downloadSpeedLimit, options.DownloadSpeedLimit);
    }

    public static TheoryData<string, string[]> ExpectedSettingsOptions()
    {
        return new TheoryData<string, string[]>
        {
            { nameof(SettingsOptionsViewModel.BackgroundSource), [BackgroundSources.Bundled, BackgroundSources.Remote, BackgroundSources.Custom] },
            { nameof(SettingsOptionsViewModel.BackgroundFit), [BackgroundFits.Fill, BackgroundFits.Uniform, BackgroundFits.UniformToFill] },
            { nameof(SettingsOptionsViewModel.ThemeColor), [ThemeColorModes.Default, ThemeColorModes.System, ThemeColorModes.Wallpaper, ThemeColorModes.Custom] },
            { nameof(SettingsOptionsViewModel.LaunchCheckMode), [LaunchCheckModes.LocalManifest, LaunchCheckModes.RemoteManifest, LaunchCheckModes.None] },
            { nameof(SettingsOptionsViewModel.ProxyMode), [ProxyModes.Auto, ProxyModes.Direct, ProxyModes.System] },
            { nameof(SettingsOptionsViewModel.PatchUrlGroup), [PatchUrlGroups.Official, PatchUrlGroups.Cafe] },
            { nameof(SettingsOptionsViewModel.DownloadSpeedLimit), [DownloadSpeedLimits.Unlimited, DownloadSpeedLimits.Speed1MBs, DownloadSpeedLimits.Speed5MBs, DownloadSpeedLimits.Speed10MBs, DownloadSpeedLimits.Speed25MBs, DownloadSpeedLimits.Speed50MBs] },
            { nameof(SettingsOptionsViewModel.CloseBehavior), [CloseBehaviors.Minimize, CloseBehaviors.Exit] },
            { nameof(SettingsOptionsViewModel.UpdateChannel), [UpdateChannels.Stable, UpdateChannels.Beta] },
            { nameof(SettingsOptionsViewModel.LogLevel), [LogLevels.Verbose, LogLevels.Debug, LogLevels.Information, LogLevels.Warning, LogLevels.Error, LogLevels.Fatal] },
            { nameof(SettingsOptionsViewModel.Theme), [ThemeModes.System, ThemeModes.Light, ThemeModes.Dark] },
            { nameof(SettingsOptionsViewModel.MotionMode), [MotionModes.System, MotionModes.Full, MotionModes.Reduced] },
            { nameof(SettingsOptionsViewModel.StatusDetailMode), [StatusDetailModes.Hidden, StatusDetailModes.Compact] }
        };
    }
}
