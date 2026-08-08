using System.Collections.Generic;
using System.Text.Json.Serialization;
using Cafe.Launcher.Avalonia.Constants;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Models;

public sealed partial class LauncherSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("gamePath")]
    private string gamePath = "";

    [ObservableProperty]
    [property: JsonPropertyName("launchCheckMode")]
    private string launchCheckMode = LaunchCheckModes.LocalManifest;

    [ObservableProperty]
    [property: JsonPropertyName("proxyMode")]
    private string proxyMode = ProxyModes.Auto;

    [ObservableProperty]
    [property: JsonPropertyName("closeBehavior")]
    private string closeBehavior = CloseBehaviors.Minimize;

    [ObservableProperty]
    [property: JsonPropertyName("language")]
    private string language = LauncherLanguages.Auto;

    [ObservableProperty]
    [property: JsonPropertyName("themeMode")]
    private string themeMode = ThemeModes.System;

    [ObservableProperty]
    [property: JsonPropertyName("motionMode")]
    private string motionMode = MotionModes.System;

    [ObservableProperty]
    [property: JsonPropertyName("themeColorMode")]
    private string themeColorMode = ThemeColorModes.Default;

    [ObservableProperty]
    [property: JsonPropertyName("customThemeColor")]
    private string customThemeColor = LauncherConstants.DefaultThemeColor;

    [ObservableProperty]
    [property: JsonPropertyName("themeColorPalette")]
    private List<string> themeColorPalette = [];

    [ObservableProperty]
    [property: JsonPropertyName("selectedThemeColorPaletteIndex")]
    private int selectedThemeColorPaletteIndex;

    [ObservableProperty]
    [property: JsonPropertyName("downloadSpeedLimit")]
    private string downloadSpeedLimit = DownloadSpeedLimits.Unlimited;

    [ObservableProperty]
    [property: JsonPropertyName("toastNotificationsEnabled")]
    private bool toastNotificationsEnabled = true;

    [ObservableProperty]
    [property: JsonPropertyName("enableStartupUpdateCheck")]
    private bool enableStartupUpdateCheck = true;

    [ObservableProperty]
    [property: JsonPropertyName("showRemoteContentCard")]
    private bool showRemoteContentCard = true;

    [ObservableProperty]
    [property: JsonPropertyName("patchUrlGroup")]
    private string patchUrlGroup = PatchUrlGroups.Official;

    [ObservableProperty]
    [property: JsonPropertyName("customBackgroundPath")]
    private string customBackgroundPath = "";

    [ObservableProperty]
    [property: JsonPropertyName("backgroundSource")]
    private string backgroundSource = BackgroundSources.Bundled;

    [ObservableProperty]
    [property: JsonPropertyName("backgroundFit")]
    private string backgroundFit = BackgroundFits.UniformToFill;

    [ObservableProperty]
    [property: JsonPropertyName("backgroundFillColor")]
    private string backgroundFillColor = "#FF000000";

    [ObservableProperty]
    [property: JsonPropertyName("resourcePanelUid")]
    private string resourcePanelUid = "";

    [ObservableProperty]
    [property: JsonPropertyName("updateChannel")]
    private string updateChannel = UpdateChannels.Stable;

    [ObservableProperty]
    [property: JsonPropertyName("logLevel")]
    private string logLevel =
#if DEBUG
        LogLevels.Verbose
#else
        LogLevels.Information
#endif
    ;

    [ObservableProperty]
    [property: JsonPropertyName("resourcePanelUidSource")]
    private string resourcePanelUidSource = ResourcePanelUidSources.Auto;

    [ObservableProperty]
    [property: JsonPropertyName("statusDetailMode")]
    private string statusDetailMode = StatusDetailModes.Detailed;

    /// <summary>
    /// Deep-clones this settings object.
    /// Shared by <c>LauncherSettingsService.NormalizeSettings</c> and <see cref="Services.SettingsEditor"/>.
    /// </summary>
    public LauncherSettings DeepClone()
    {
        return new LauncherSettings(this);
    }

    /// <summary>
    /// Copy constructor for deep cloning. Copies all settings properties,
    /// including a shallow copy of <see cref="ThemeColorPalette"/> (strings are immutable).
    /// ⚠️ When adding a new setting property to this class,
    /// you MUST add a corresponding line to this constructor.
    /// Failure to do so results in silent shallow copy of the new property.
    /// </summary>
    public LauncherSettings(LauncherSettings other)
    {
        GamePath = other.GamePath;
        LaunchCheckMode = other.LaunchCheckMode;
        ProxyMode = other.ProxyMode;
        CloseBehavior = other.CloseBehavior;
        Language = other.Language;
        ThemeMode = other.ThemeMode;
        MotionMode = other.MotionMode;
        ThemeColorMode = other.ThemeColorMode;
        CustomThemeColor = other.CustomThemeColor;
        ThemeColorPalette = [.. other.ThemeColorPalette];
        SelectedThemeColorPaletteIndex = other.SelectedThemeColorPaletteIndex;
        DownloadSpeedLimit = other.DownloadSpeedLimit;
        ToastNotificationsEnabled = other.ToastNotificationsEnabled;
        EnableStartupUpdateCheck = other.EnableStartupUpdateCheck;
        ShowRemoteContentCard = other.ShowRemoteContentCard;
        PatchUrlGroup = other.PatchUrlGroup;
        CustomBackgroundPath = other.CustomBackgroundPath;
        BackgroundSource = other.BackgroundSource;
        BackgroundFit = other.BackgroundFit;
        BackgroundFillColor = other.BackgroundFillColor;
        ResourcePanelUid = other.ResourcePanelUid;
        ResourcePanelUidSource = other.ResourcePanelUidSource;
        StatusDetailMode = other.StatusDetailMode;
        UpdateChannel = other.UpdateChannel;
        LogLevel = other.LogLevel;
    }

    /// <summary>
    /// Default constructor. Creates settings with defaults.
    /// </summary>
    public LauncherSettings() { }

    /// <summary>
    /// Creates default settings with pre-release builds defaulting to the beta update channel.
    /// Shared by <see cref="Services.LauncherSettingsService"/> and <see cref="Services.SettingsEditor"/>.
    /// When the system UI language is Chinese, defaults to the Cafe patch URL group so Chinese
    /// players get the Cafe-localised version without manually changing the download source.
    /// </summary>
    public static LauncherSettings CreateDefaults()
    {
        var settings = new LauncherSettings();

        if (Constants.BuildInfo.LauncherVersion.Contains('-'))
        {
            settings.UpdateChannel = UpdateChannels.Beta;
        }

        // Chinese users are the primary audience for Cafe-localised game resources;
        // default to Cafe source so they get Chinese text without manual setup.
        if (IsChineseUICulture())
        {
            settings.PatchUrlGroup = PatchUrlGroups.Cafe;
        }

        return settings;
    }

    private static bool IsChineseUICulture()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        return culture is "zh-CN" or "zh-TW" or "zh-HK" or "zh-MO" or "zh-SG"
            or "zh-Hans" or "zh-Hant";
    }
}
