using System.Collections.Generic;
using System.Text.Json.Serialization;
using Cafe.Launcher.Avalonia.Constants;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Models;

public sealed class LauncherSettings : ObservableObject
{
    private string gamePath = "";
    private string launchCheckMode = LaunchCheckModes.LocalManifest;
    private string proxyMode = ProxyModes.Auto;
    private string closeBehavior = CloseBehaviors.Minimize;
    private string language = LauncherLanguages.Auto;
    private string themeMode = ThemeModes.System;
    private string motionMode = MotionModes.System;
    private string themeColorMode = ThemeColorModes.Default;
    private string themeColorExtractionAlgorithm = ThemeColorExtractionAlgorithms.CelebiScore;
    private string themeColorVariant = ThemeColorVariants.TonalSpot;
    private string neutralColorStrategy = NeutralColorStrategies.BrandBlue;
    private string customThemeColor = LauncherConstants.DefaultThemeColor;
    private List<string> themeColorPalette = [];
    private int selectedThemeColorPaletteIndex;
    private string downloadSpeedLimit = DownloadSpeedLimits.Unlimited;
    private bool enableStartupUpdateCheck = true;
    private bool showRemoteContentCard = true;
    private bool rememberWindowPositionAndSize;
    private int? windowPositionX;
    private int? windowPositionY;
    private double? windowWidth;
    private double? windowHeight;
    private string patchUrlGroup = PatchUrlGroups.Official;
    private string customBackgroundPath = "";
    private string backgroundSource = BackgroundSources.Bundled;
    private string backgroundFit = BackgroundFits.UniformToFill;
    private string backgroundFillColor = "#FF000000";
    private string resourcePanelUid = "";
    private string updateChannel = UpdateChannels.Stable;
    private string logLevel =
#if DEBUG
        LogLevels.Verbose
#else
        LogLevels.Information
#endif
    ;
    private string resourcePanelUidSource = ResourcePanelUidSources.Auto;
    private GameRuntimeSettings gameRuntime = new();
    private string statusDetailMode = StatusDetailModes.Compact;

    [JsonPropertyName("gamePath")]
    public string GamePath { get => gamePath; set => SetProperty(ref gamePath, value); }

    [JsonPropertyName("launchCheckMode")]
    public string LaunchCheckMode { get => launchCheckMode; set => SetProperty(ref launchCheckMode, value); }

    [JsonPropertyName("proxyMode")]
    public string ProxyMode { get => proxyMode; set => SetProperty(ref proxyMode, value); }

    [JsonPropertyName("closeBehavior")]
    public string CloseBehavior { get => closeBehavior; set => SetProperty(ref closeBehavior, value); }

    [JsonPropertyName("language")]
    public string Language { get => language; set => SetProperty(ref language, value); }

    [JsonPropertyName("themeMode")]
    public string ThemeMode { get => themeMode; set => SetProperty(ref themeMode, value); }

    [JsonPropertyName("motionMode")]
    public string MotionMode { get => motionMode; set => SetProperty(ref motionMode, value); }

    [JsonPropertyName("themeColorMode")]
    public string ThemeColorMode { get => themeColorMode; set => SetProperty(ref themeColorMode, value); }

    [JsonPropertyName("themeColorExtractionAlgorithm")]
    public string ThemeColorExtractionAlgorithm { get => themeColorExtractionAlgorithm; set => SetProperty(ref themeColorExtractionAlgorithm, value); }

    [JsonPropertyName("themeColorVariant")]
    public string ThemeColorVariant { get => themeColorVariant; set => SetProperty(ref themeColorVariant, value); }

    [JsonPropertyName("neutralColorStrategy")]
    public string NeutralColorStrategy { get => neutralColorStrategy; set => SetProperty(ref neutralColorStrategy, value); }

    [JsonPropertyName("customThemeColor")]
    public string CustomThemeColor { get => customThemeColor; set => SetProperty(ref customThemeColor, value); }

    [JsonPropertyName("themeColorPalette")]
    public List<string> ThemeColorPalette { get => themeColorPalette; set => SetProperty(ref themeColorPalette, value); }

    [JsonPropertyName("selectedThemeColorPaletteIndex")]
    public int SelectedThemeColorPaletteIndex { get => selectedThemeColorPaletteIndex; set => SetProperty(ref selectedThemeColorPaletteIndex, value); }

    [JsonPropertyName("downloadSpeedLimit")]
    public string DownloadSpeedLimit { get => downloadSpeedLimit; set => SetProperty(ref downloadSpeedLimit, value); }

    [JsonPropertyName("enableStartupUpdateCheck")]
    public bool EnableStartupUpdateCheck { get => enableStartupUpdateCheck; set => SetProperty(ref enableStartupUpdateCheck, value); }

    [JsonPropertyName("showRemoteContentCard")]
    public bool ShowRemoteContentCard { get => showRemoteContentCard; set => SetProperty(ref showRemoteContentCard, value); }

    [JsonPropertyName("rememberWindowPositionAndSize")]
    public bool RememberWindowPositionAndSize { get => rememberWindowPositionAndSize; set => SetProperty(ref rememberWindowPositionAndSize, value); }

    [JsonPropertyName("windowPositionX")]
    public int? WindowPositionX { get => windowPositionX; set => SetProperty(ref windowPositionX, value); }

    [JsonPropertyName("windowPositionY")]
    public int? WindowPositionY { get => windowPositionY; set => SetProperty(ref windowPositionY, value); }

    [JsonPropertyName("windowWidth")]
    public double? WindowWidth { get => windowWidth; set => SetProperty(ref windowWidth, value); }

    [JsonPropertyName("windowHeight")]
    public double? WindowHeight { get => windowHeight; set => SetProperty(ref windowHeight, value); }

    [JsonPropertyName("patchUrlGroup")]
    public string PatchUrlGroup { get => patchUrlGroup; set => SetProperty(ref patchUrlGroup, value); }

    [JsonPropertyName("customBackgroundPath")]
    public string CustomBackgroundPath { get => customBackgroundPath; set => SetProperty(ref customBackgroundPath, value); }

    [JsonPropertyName("backgroundSource")]
    public string BackgroundSource { get => backgroundSource; set => SetProperty(ref backgroundSource, value); }

    [JsonPropertyName("backgroundFit")]
    public string BackgroundFit { get => backgroundFit; set => SetProperty(ref backgroundFit, value); }

    [JsonPropertyName("backgroundFillColor")]
    public string BackgroundFillColor { get => backgroundFillColor; set => SetProperty(ref backgroundFillColor, value); }

    [JsonPropertyName("resourcePanelUid")]
    public string ResourcePanelUid { get => resourcePanelUid; set => SetProperty(ref resourcePanelUid, value); }

    [JsonPropertyName("updateChannel")]
    public string UpdateChannel { get => updateChannel; set => SetProperty(ref updateChannel, value); }

    [JsonPropertyName("logLevel")]
    public string LogLevel { get => logLevel; set => SetProperty(ref logLevel, value); }

    [JsonPropertyName("resourcePanelUidSource")]
    public string ResourcePanelUidSource { get => resourcePanelUidSource; set => SetProperty(ref resourcePanelUidSource, value); }

    [JsonPropertyName("gameRuntime")]
    public GameRuntimeSettings GameRuntime { get => gameRuntime; set => SetProperty(ref gameRuntime, value); }

    [JsonPropertyName("statusDetailMode")]
    public string StatusDetailMode { get => statusDetailMode; set => SetProperty(ref statusDetailMode, value); }

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
        ThemeColorExtractionAlgorithm = other.ThemeColorExtractionAlgorithm;
        ThemeColorVariant = other.ThemeColorVariant;
        NeutralColorStrategy = other.NeutralColorStrategy;
        CustomThemeColor = other.CustomThemeColor;
        ThemeColorPalette = [.. other.ThemeColorPalette];
        SelectedThemeColorPaletteIndex = other.SelectedThemeColorPaletteIndex;
        DownloadSpeedLimit = other.DownloadSpeedLimit;
        EnableStartupUpdateCheck = other.EnableStartupUpdateCheck;
        ShowRemoteContentCard = other.ShowRemoteContentCard;
        RememberWindowPositionAndSize = other.RememberWindowPositionAndSize;
        WindowPositionX = other.WindowPositionX;
        WindowPositionY = other.WindowPositionY;
        WindowWidth = other.WindowWidth;
        WindowHeight = other.WindowHeight;
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
        GameRuntime = other.GameRuntime.DeepClone();
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
