using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.ViewModels;

public sealed class SettingsOptionsViewModel
{
    private readonly LocalizationService localizer;
    private readonly DiskSpaceService diskSpaceService;

    public SettingsOptionsViewModel(
        LocalizationService localizer,
        DiskSpaceService diskSpaceService)
    {
        this.localizer = localizer;
        this.diskSpaceService = diskSpaceService;
    }

    public ObservableCollection<SettingOption> BackgroundSource { get; } =
    [
        new() { Code = BackgroundSources.Bundled },
        new() { Code = BackgroundSources.Remote },
        new() { Code = BackgroundSources.Custom },
        new() { Code = BackgroundSources.Video }
    ];

    public ObservableCollection<SettingOption> BackgroundFit { get; } =
    [
        new() { Code = BackgroundFits.Fill },
        new() { Code = BackgroundFits.Uniform },
        new() { Code = BackgroundFits.UniformToFill }
    ];

    public ObservableCollection<SettingOption> ThemeColor { get; } =
    [
        new() { Code = ThemeColorModes.Default },
        new() { Code = ThemeColorModes.System },
        new() { Code = ThemeColorModes.Wallpaper },
        new() { Code = ThemeColorModes.Custom }
    ];

    public ObservableCollection<SettingOption> LaunchCheckMode { get; } =
    [
        new() { Code = LaunchCheckModes.LocalManifest },
        new() { Code = LaunchCheckModes.RemoteManifest },
        new() { Code = LaunchCheckModes.None }
    ];

    public ObservableCollection<SettingOption> ProxyMode { get; } =
    [
        new() { Code = ProxyModes.Direct },
        new() { Code = ProxyModes.System }
    ];

    public ObservableCollection<SettingOption> PatchUrlGroup { get; } =
    [
        new() { Code = PatchUrlGroups.Official },
        new() { Code = PatchUrlGroups.Cafe }
    ];

    public ObservableCollection<SettingOption> DownloadSpeedLimit { get; } =
    [
        new() { Code = DownloadSpeedLimits.Unlimited },
        new() { Code = DownloadSpeedLimits.Speed1MBs },
        new() { Code = DownloadSpeedLimits.Speed5MBs },
        new() { Code = DownloadSpeedLimits.Speed10MBs },
        new() { Code = DownloadSpeedLimits.Speed25MBs },
        new() { Code = DownloadSpeedLimits.Speed50MBs }
    ];

    public ObservableCollection<SettingOption> CloseBehavior { get; } =
    [
        new() { Code = CloseBehaviors.Minimize },
        new() { Code = CloseBehaviors.Exit }
    ];

    public IReadOnlyList<LanguageOption> Language { get; } = LocalizationService.GetLanguageOptions();

    public ObservableCollection<SettingOption> UpdateChannel { get; } =
    [
        new() { Code = UpdateChannels.Stable },
        new() { Code = UpdateChannels.Beta }
    ];

    public ObservableCollection<SettingOption> LogLevel { get; } =
    [
        new() { Code = LogLevels.Verbose },
        new() { Code = LogLevels.Debug },
        new() { Code = LogLevels.Information },
        new() { Code = LogLevels.Warning },
        new() { Code = LogLevels.Error },
        new() { Code = LogLevels.Fatal }
    ];

    public ObservableCollection<ThemeOption> Theme { get; } =
    [
        new() { Code = ThemeModes.System },
        new() { Code = ThemeModes.Light },
        new() { Code = ThemeModes.Dark }
    ];

    public void RefreshDisplayNames()
    {
        foreach (var option in Theme)
        {
            option.DisplayName = option.Code switch
            {
                ThemeModes.Light => localizer.T("themeLight"),
                ThemeModes.Dark => localizer.T("themeDark"),
                _ => localizer.T("themeSystem")
            };
        }

        foreach (var option in ThemeColor)
        {
            option.DisplayName = option.Code switch
            {
                ThemeColorModes.System => localizer.T("themeColorSystem"),
                ThemeColorModes.Wallpaper => localizer.T("themeColorWallpaper"),
                ThemeColorModes.Custom => localizer.T("themeColorCustom"),
                _ => localizer.T("themeColorDefault")
            };
        }

        foreach (var option in LaunchCheckMode)
        {
            option.DisplayName = option.Code switch
            {
                LaunchCheckModes.RemoteManifest => localizer.T("launchCheckRemoteManifest"),
                LaunchCheckModes.None => localizer.T("launchCheckNone"),
                _ => localizer.T("launchCheckLocalManifest")
            };
        }

        foreach (var option in ProxyMode)
        {
            option.DisplayName = option.Code switch
            {
                ProxyModes.System => localizer.T("proxySystem"),
                _ => localizer.T("proxyDirect")
            };
        }

        foreach (var option in PatchUrlGroup)
        {
            option.DisplayName = option.Code switch
            {
                PatchUrlGroups.Cafe => localizer.T("downloadSourceCafe"),
                _ => localizer.T("downloadSourceOfficial")
            };
        }

        foreach (var option in CloseBehavior)
        {
            option.DisplayName = option.Code switch
            {
                CloseBehaviors.Exit => localizer.T("closeBehaviorExit"),
                _ => localizer.T("closeBehaviorMinimize")
            };
        }

        foreach (var option in DownloadSpeedLimit)
        {
            option.DisplayName = option.Code switch
            {
                DownloadSpeedLimits.Speed1MBs => localizer.T("speed1MBs"),
                DownloadSpeedLimits.Speed5MBs => localizer.T("speed5MBs"),
                DownloadSpeedLimits.Speed10MBs => localizer.T("speed10MBs"),
                DownloadSpeedLimits.Speed25MBs => localizer.T("speed25MBs"),
                DownloadSpeedLimits.Speed50MBs => localizer.T("speed50MBs"),
                _ => localizer.T("speedUnlimited")
            };
        }

        foreach (var option in BackgroundSource)
        {
            option.DisplayName = option.Code switch
            {
                BackgroundSources.Remote => localizer.T("backgroundSourceRemote"),
                BackgroundSources.Custom => localizer.T("backgroundSourceCustom"),
                BackgroundSources.Video => localizer.T("backgroundSourceVideo"),
                _ => localizer.T("backgroundSourceBundled")
            };
        }

        foreach (var option in BackgroundFit)
        {
            option.DisplayName = option.Code switch
            {
                BackgroundFits.Fill => localizer.T("backgroundFitFill"),
                BackgroundFits.Uniform => localizer.T("backgroundFitUniform"),
                _ => localizer.T("backgroundFitUniformToFill")
            };
        }

        foreach (var option in UpdateChannel)
        {
            option.DisplayName = option.Code switch
            {
                UpdateChannels.Beta => localizer.T("updateChannelBeta"),
                _ => localizer.T("updateChannelStable")
            };
        }

        foreach (var option in LogLevel)
        {
            option.DisplayName = option.Code switch
            {
                LogLevels.Verbose => localizer.T("logLevelVerbose"),
                LogLevels.Debug => localizer.T("logLevelDebug"),
                LogLevels.Warning => localizer.T("logLevelWarning"),
                LogLevels.Error => localizer.T("logLevelError"),
                LogLevels.Fatal => localizer.T("logLevelFatal"),
                _ => localizer.T("logLevelInformation")
            };
        }
    }

    public string ResolveLanguageDisplayName(string language) =>
        Language.FirstOrDefault(option => option.Code == language)?.DisplayName
        ?? Language.First(option => option.Code == LauncherLanguages.Auto).DisplayName;

    public string ResolveThemeDisplayName(string themeMode) =>
        Theme.FirstOrDefault(option => option.Code == themeMode)?.DisplayName
        ?? localizer.T("themeSystem");

    public string ResolveLaunchCheckDisplayName(string launchCheckMode) =>
        launchCheckMode switch
        {
            LaunchCheckModes.RemoteManifest => localizer.T("statusLaunchCheckRemote"),
            LaunchCheckModes.None => localizer.T("statusLaunchCheckNone"),
            _ => localizer.T("statusLaunchCheckLocal")
        };

    public string ResolveDiskSpaceText(string gamePath, string? requiredSize)
    {
        var required = string.IsNullOrWhiteSpace(requiredSize)
            ? "--"
            : requiredSize.Replace(" ", "", System.StringComparison.Ordinal);
        var availableBytes = diskSpaceService.GetAvailableBytes(gamePath);
        var available = availableBytes.HasValue
            ? FileSizeFormatter.Format(availableBytes.Value)
            : "--";
        return localizer.F("diskSpace", required, available);
    }
}
