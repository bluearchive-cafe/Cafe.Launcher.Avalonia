using System;
using System.Collections;
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
    private bool enableHttp2 = true;
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

    [JsonPropertyName("enableHttp2")]
    public bool EnableHttp2 { get => enableHttp2; set => SetProperty(ref enableHttp2, value); }

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
    /// The same property must also be added to <see cref="ComparedProperties"/>, otherwise state
    /// identity ignores it and the settings page's save button stops tracking that field.
    /// LauncherSettingsTests guards both lists.
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
        EnableHttp2 = other.EnableHttp2;
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
        // A hand-edited or corrupted settings file can carry "gameRuntime": null;
        // NormalizeSettings deep-clones before its own ??= guard runs, so the copy
        // constructor must tolerate null without crashing the load path.
        GameRuntime = other.GameRuntime?.DeepClone() ?? new GameRuntimeSettings();
    }

    /// <summary>
    /// The properties that participate in <see cref="HasSameSettingsState"/>, as (name, reader)
    /// pairs. Declared here rather than enumerated by reflection so the compared set stays greppable
    /// and no production code depends on runtime reflection; LauncherSettingsTests reads the names
    /// and drives the readers, so a settable property missing from this table fails the tests instead
    /// of silently dropping out of state identity.
    /// </summary>
    private static readonly (string Name, Func<LauncherSettings, object?> Read)[] ComparedProperties =
    [
        (nameof(GamePath), settings => settings.GamePath),
        (nameof(LaunchCheckMode), settings => settings.LaunchCheckMode),
        (nameof(ProxyMode), settings => settings.ProxyMode),
        (nameof(CloseBehavior), settings => settings.CloseBehavior),
        (nameof(Language), settings => settings.Language),
        (nameof(ThemeMode), settings => settings.ThemeMode),
        (nameof(MotionMode), settings => settings.MotionMode),
        (nameof(ThemeColorMode), settings => settings.ThemeColorMode),
        (nameof(ThemeColorExtractionAlgorithm), settings => settings.ThemeColorExtractionAlgorithm),
        (nameof(ThemeColorVariant), settings => settings.ThemeColorVariant),
        (nameof(NeutralColorStrategy), settings => settings.NeutralColorStrategy),
        (nameof(CustomThemeColor), settings => settings.CustomThemeColor),
        (nameof(ThemeColorPalette), settings => settings.ThemeColorPalette),
        (nameof(SelectedThemeColorPaletteIndex), settings => settings.SelectedThemeColorPaletteIndex),
        (nameof(DownloadSpeedLimit), settings => settings.DownloadSpeedLimit),
        (nameof(EnableHttp2), settings => settings.EnableHttp2),
        (nameof(EnableStartupUpdateCheck), settings => settings.EnableStartupUpdateCheck),
        (nameof(ShowRemoteContentCard), settings => settings.ShowRemoteContentCard),
        (nameof(RememberWindowPositionAndSize), settings => settings.RememberWindowPositionAndSize),
        (nameof(WindowPositionX), settings => settings.WindowPositionX),
        (nameof(WindowPositionY), settings => settings.WindowPositionY),
        (nameof(WindowWidth), settings => settings.WindowWidth),
        (nameof(WindowHeight), settings => settings.WindowHeight),
        (nameof(PatchUrlGroup), settings => settings.PatchUrlGroup),
        (nameof(CustomBackgroundPath), settings => settings.CustomBackgroundPath),
        (nameof(BackgroundSource), settings => settings.BackgroundSource),
        (nameof(BackgroundFit), settings => settings.BackgroundFit),
        (nameof(BackgroundFillColor), settings => settings.BackgroundFillColor),
        (nameof(ResourcePanelUid), settings => settings.ResourcePanelUid),
        (nameof(ResourcePanelUidSource), settings => settings.ResourcePanelUidSource),
        (nameof(StatusDetailMode), settings => settings.StatusDetailMode),
        (nameof(UpdateChannel), settings => settings.UpdateChannel),
        (nameof(LogLevel), settings => settings.LogLevel),
        (nameof(GameRuntime), settings => settings.GameRuntime)
    ];

    /// <summary>
    /// Reports whether <paramref name="other"/> holds the same settings state — the identity the
    /// settings page's dirty flag and save button are derived from, and the counterpart of
    /// <see cref="DeepClone"/>: clone, then compare, must answer "same".
    /// Every entry goes through <see cref="ValuesEqual"/>, which handles the two shapes that carry
    /// no value equality of their own: <see cref="ThemeColorPalette"/> element by element (order is
    /// significant — the extractor decides the canonical order, so any dedup or normalization
    /// shifts indices) and <see cref="GameRuntime"/> by recursing into its own state identity.
    /// </summary>
    public bool HasSameSettingsState(LauncherSettings? other)
    {
        if (other is null)
        {
            return false;
        }

        foreach (var (_, read) in ComparedProperties)
        {
            if (!ValuesEqual(read(this), read(other)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Compares two settings values: the single per-value definition shared by
    /// <see cref="HasSameSettingsState"/> and the LauncherSettingsTests guards.
    /// Strings compare with <see cref="StringComparison.Ordinal"/> — deliberately not culture-aware,
    /// because these are stored codes rather than display text. Boxed values go through <c>Equals</c>
    /// because that is the comparison the boxed shape offers, and because it is reflexive where
    /// <c>==</c> is not: with <c>==</c>, two <c>NaN</c> window dimensions would compare different and
    /// leave the editor permanently dirty. That case is currently unreachable —
    /// <c>MainWindow.CaptureWindowState</c> filters with <c>double.IsFinite</c> and
    /// <c>LauncherSettingsService.NormalizeSettings</c> nulls non-finite dimensions on load — so
    /// identity does not depend on that filtering.
    /// Unrecognised types fall back to <see cref="object.Equals(object?)"/> and never throw: a wrong
    /// "different" answer only leaves the save button enabled, and the guards are what keep a wrong
    /// answer from shipping.
    /// </summary>
    internal static bool ValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is GameRuntimeSettings leftRuntime && right is GameRuntimeSettings rightRuntime)
        {
            return leftRuntime.HasSameSettingsState(rightRuntime);
        }

        // Strings are IEnumerable<char>, so they must be handled before the sequence branch.
        if (left is string leftText && right is string rightText)
        {
            return string.Equals(leftText, rightText, StringComparison.Ordinal);
        }

        if (left is IEnumerable leftItems && right is IEnumerable rightItems)
        {
            return SequencesEqual(leftItems, rightItems);
        }

        return left.Equals(right);
    }

    private static bool SequencesEqual(IEnumerable left, IEnumerable right)
    {
        var leftItems = left.GetEnumerator();
        var rightItems = right.GetEnumerator();

        try
        {
            while (true)
            {
                var hasLeft = leftItems.MoveNext();
                var hasRight = rightItems.MoveNext();

                if (hasLeft != hasRight)
                {
                    return false;
                }

                if (!hasLeft)
                {
                    return true;
                }

                if (!ValuesEqual(leftItems.Current, rightItems.Current))
                {
                    return false;
                }
            }
        }
        finally
        {
            (leftItems as IDisposable)?.Dispose();
            (rightItems as IDisposable)?.Dispose();
        }
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
