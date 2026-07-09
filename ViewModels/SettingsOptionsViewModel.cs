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
        new() { Code = BackgroundSources.Custom }
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
        new() { Code = ProxyModes.Auto },
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

    public ObservableCollection<SettingOption> MotionMode { get; } =
    [
        new() { Code = MotionModes.System },
        new() { Code = MotionModes.Full },
        new() { Code = MotionModes.Reduced }
    ];

    public ObservableCollection<SettingOption> SettingsCategories { get; } = [];

    public void RefreshDisplayNames()
    {
        EnsureSettingCategories();
        UpdateSettingCategory(SettingsCategoryCodes.General, localizer.T("settingsCategoryGeneral"), localizer.T("settingsCategoryGeneralDescription"));
        UpdateSettingCategory(SettingsCategoryCodes.Game, localizer.T("settingsCategoryGame"), localizer.T("settingsCategoryGameDescription"));
        UpdateSettingCategory(SettingsCategoryCodes.DownloadNetwork, localizer.T("settingsCategoryDownloadNetwork"), localizer.T("settingsCategoryDownloadNetworkDescription"));
        UpdateSettingCategory(SettingsCategoryCodes.Appearance, localizer.T("settingsCategoryAppearance"), localizer.T("settingsCategoryAppearanceDescription"));
        UpdateSettingCategory(SettingsCategoryCodes.About, localizer.T("settingsCategoryAbout"), localizer.T("settingsCategoryAboutDescription"));

        RefreshOptions(Theme, code => code switch
        {
            ThemeModes.Light => localizer.T("themeLight"),
            ThemeModes.Dark => localizer.T("themeDark"),
            _ => localizer.T("themeSystem")
        });

        RefreshOptions(MotionMode, code => code switch
        {
            MotionModes.Full => localizer.T("motionModeFull"),
            MotionModes.Reduced => localizer.T("motionModeReduced"),
            _ => localizer.T("motionModeSystem")
        });

        RefreshOptions(ThemeColor, code => code switch
        {
            ThemeColorModes.System => localizer.T("themeColorSystem"),
            ThemeColorModes.Wallpaper => localizer.T("themeColorWallpaper"),
            ThemeColorModes.Custom => localizer.T("themeColorCustom"),
            _ => localizer.T("themeColorDefault")
        });

        RefreshOptions(LaunchCheckMode, code => code switch
        {
            LaunchCheckModes.RemoteManifest => localizer.T("launchCheckRemoteManifest"),
            LaunchCheckModes.None => localizer.T("launchCheckNone"),
            _ => localizer.T("launchCheckLocalManifest")
        });

        RefreshOptions(ProxyMode, code => code switch
        {
            ProxyModes.Auto => localizer.T("proxyAuto"),
            ProxyModes.System => localizer.T("proxySystem"),
            _ => localizer.T("proxyDirect")
        });

        RefreshOptions(PatchUrlGroup, code => code switch
        {
            PatchUrlGroups.Cafe => localizer.T("downloadSourceCafe"),
            _ => localizer.T("downloadSourceOfficial")
        });

        RefreshOptions(CloseBehavior, code => code switch
        {
            CloseBehaviors.Exit => localizer.T("closeBehaviorExit"),
            _ => localizer.T("closeBehaviorMinimize")
        });

        RefreshOptions(DownloadSpeedLimit, code => code switch
        {
            DownloadSpeedLimits.Speed1MBs => localizer.T("speed1MBs"),
            DownloadSpeedLimits.Speed5MBs => localizer.T("speed5MBs"),
            DownloadSpeedLimits.Speed10MBs => localizer.T("speed10MBs"),
            DownloadSpeedLimits.Speed25MBs => localizer.T("speed25MBs"),
            DownloadSpeedLimits.Speed50MBs => localizer.T("speed50MBs"),
            _ => localizer.T("speedUnlimited")
        });

        RefreshOptions(BackgroundSource, code => code switch
        {
            BackgroundSources.Remote => localizer.T("backgroundSourceRemote"),
            BackgroundSources.Custom => localizer.T("backgroundSourceCustom"),
            _ => localizer.T("backgroundSourceBundled")
        });

        RefreshOptions(BackgroundFit, code => code switch
        {
            BackgroundFits.Fill => localizer.T("backgroundFitFill"),
            BackgroundFits.Uniform => localizer.T("backgroundFitUniform"),
            _ => localizer.T("backgroundFitUniformToFill")
        });

        RefreshOptions(UpdateChannel, code => code switch
        {
            UpdateChannels.Beta => localizer.T("launcherUpdateChannelBeta"),
            _ => localizer.T("launcherUpdateChannelStable")
        });

        RefreshOptions(LogLevel, code => code switch
        {
            LogLevels.Verbose => localizer.T("logLevelVerbose"),
            LogLevels.Debug => localizer.T("logLevelDebug"),
            LogLevels.Warning => localizer.T("logLevelWarning"),
            LogLevels.Error => localizer.T("logLevelError"),
            LogLevels.Fatal => localizer.T("logLevelFatal"),
            _ => localizer.T("logLevelInformation")
        });
    }

    private void RefreshOptions(ObservableCollection<SettingOption> options, System.Func<string, string> resolveDisplayName)
    {
        foreach (var option in options)
        {
            option.DisplayName = resolveDisplayName(option.Code);
        }
    }

    private void RefreshOptions(ObservableCollection<ThemeOption> options, System.Func<string, string> resolveDisplayName)
    {
        foreach (var option in options)
        {
            option.DisplayName = resolveDisplayName(option.Code);
        }
    }

    private void EnsureSettingCategories()
    {
        if (SettingsCategories.Count > 0)
        {
            return;
        }

        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.General });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.Game });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.DownloadNetwork });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.Appearance });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.About });
    }

    private void UpdateSettingCategory(string code, string displayName, string description)
    {
        var option = SettingsCategories.First(option => option.Code == code);
        option.DisplayName = displayName;
        option.Description = description;
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
