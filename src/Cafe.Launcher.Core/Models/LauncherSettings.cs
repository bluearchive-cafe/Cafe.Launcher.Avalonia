using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core;

namespace Cafe.Launcher.Core.Models;

/// <summary>单一版本化设置根；全局偏好与按稳定游戏身份索引的独立设置共同落盘。</summary>
public sealed class LauncherSettings : SettingsModel
{
    private int formatVersion = SettingsFormatVersions.Current;
    private string lastViewedGameId = LauncherProfiles.BlueArchiveJapan.RuntimeId;
    private Dictionary<string, GameSettings> games = CreateDefaultGames();
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
    private bool allowBlockedNetworkAddresses;
    private bool enableStartupUpdateCheck = true;
    private bool showRemoteContentCard = true;
    private bool rememberWindowPositionAndSize;
    private int? windowPositionX;
    private int? windowPositionY;
    private double? windowWidth;
    private double? windowHeight;
    private string customBackgroundPath = "";
    private string backgroundSource = BackgroundSources.Bundled;
    private string backgroundFit = BackgroundFits.UniformToFill;
    private string backgroundFillColor = "#FF000000";
    private int backgroundVideoVolume;
    private bool backgroundVideoMuted;
    private string updateChannel = UpdateChannels.Stable;
    private string logLevel =
#if DEBUG
        LogLevels.Verbose
#else
        LogLevels.Information
#endif
    ;
    private string statusDetailMode = StatusDetailModes.Compact;
    private string acceptedAgreementVersion = "";

    /// <summary>持久化格式版本；未知版本由设置读取入口拒绝，不归一化为当前版本。</summary>
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get => formatVersion; set => SetProperty(ref formatVersion, value); }

    /// <summary>上次查看的稳定游戏身份；只影响显示，不决定任何游戏设置的写入目标。</summary>
    [JsonPropertyName("lastViewedGameId")]
    public string LastViewedGameId { get => lastViewedGameId; set => SetProperty(ref lastViewedGameId, value); }

    /// <summary>按稳定且区分大小写的游戏身份索引；保留空字典、空条目及缺项，交由格式读取入口判定。</summary>
    [JsonPropertyName("games")]
    public Dictionary<string, GameSettings> Games
    {
        get => games;
        set => SetProperty(ref games, value is null ? null! : new Dictionary<string, GameSettings>(value, StringComparer.Ordinal));
    }

    /// <summary>读取明确游戏的设置；未知身份或缺失条目直接报错，不回退当前条目或蔚蓝档案。</summary>
    public GameSettings GetGameSettings(string gameId)
    {
        if (!LauncherProfiles.TryGetSupportedGame(gameId, out _))
        {
            throw new ArgumentException("Unsupported game identity.", nameof(gameId));
        }

        if (Games is null || !Games.TryGetValue(gameId, out var settings) || settings is null)
        {
            throw new InvalidOperationException($"Missing settings for game: {gameId}");
        }

        return settings;
    }

    private GameSettings? FindBlueArchiveSettings() =>
        Games is not null && Games.TryGetValue(LauncherProfiles.BlueArchiveJapan.RuntimeId, out var settings)
            ? settings
            : null;

    private static Dictionary<string, GameSettings> CreateDefaultGames() => new(StringComparer.Ordinal)
    {
        [LauncherProfiles.BlueArchiveJapan.RuntimeId] = new(),
        [LauncherProfiles.StellaSoraChina.RuntimeId] = new()
    };

    /// <summary>固定投影蔚蓝档案设置的兼容入口；不跟随当前查看条目，也不单独写入 JSON。</summary>
    [JsonIgnore]
    public string GamePath
    {
        get => GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).GamePath;
        set
        {
            var game = GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId);
            if (EqualityComparer<string>.Default.Equals(game.GamePath, value))
            {
                return;
            }

            game.GamePath = value;
            OnPropertyChanged();
        }
    }

    /// <summary>固定投影蔚蓝档案设置的兼容入口；不跟随当前查看条目，也不单独写入 JSON。</summary>
    [JsonIgnore]
    public string LaunchCheckMode
    {
        get => GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).LaunchCheckMode;
        set
        {
            var game = GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId);
            if (EqualityComparer<string>.Default.Equals(game.LaunchCheckMode, value))
            {
                return;
            }

            game.LaunchCheckMode = value;
            OnPropertyChanged();
        }
    }

    [JsonPropertyName("proxyMode")]
    public string ProxyMode { get => proxyMode; set => SetProperty(ref proxyMode, value); }

    [JsonPropertyName("closeBehavior")]
    public string CloseBehavior { get => closeBehavior; set => SetProperty(ref closeBehavior, value); }

    /// <summary>固定投影蔚蓝档案设置的兼容入口；不跟随当前查看条目，也不单独写入 JSON。</summary>
    [JsonIgnore]
    public string AfterLaunchBehavior
    {
        get => GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).AfterLaunchBehavior;
        set
        {
            var game = GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId);
            if (EqualityComparer<string>.Default.Equals(game.AfterLaunchBehavior, value))
            {
                return;
            }

            game.AfterLaunchBehavior = value;
            OnPropertyChanged();
        }
    }

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

    /// <summary>
    /// 高级兼容开关：允许远程地址解析到本机或内网。hosts 型 GitHub 加速器（FastGithub、
    /// Watt Toolkit 等）把域名改写为回环地址并在本机提供服务，该解析结果正是其工作前提。
    /// 默认关闭；开启后只跳过「解析结果必须是公网」这一条，scheme、端口、userinfo、
    /// localhost 名字与字面地址的限制不变（ADR-060、AUD-SEC-008）。
    /// </summary>
    [JsonPropertyName("allowBlockedNetworkAddresses")]
    public bool AllowBlockedNetworkAddresses { get => allowBlockedNetworkAddresses; set => SetProperty(ref allowBlockedNetworkAddresses, value); }

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

    /// <summary>固定投影蔚蓝档案设置的兼容入口；不跟随当前查看条目，也不单独写入 JSON。</summary>
    [JsonIgnore]
    public string PatchUrlGroup
    {
        get => GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).PatchUrlGroup;
        set
        {
            var game = GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId);
            if (EqualityComparer<string>.Default.Equals(game.PatchUrlGroup, value))
            {
                return;
            }

            game.PatchUrlGroup = value;
            OnPropertyChanged();
        }
    }

    [JsonPropertyName("customBackgroundPath")]
    public string CustomBackgroundPath { get => customBackgroundPath; set => SetProperty(ref customBackgroundPath, value); }

    [JsonPropertyName("backgroundSource")]
    public string BackgroundSource { get => backgroundSource; set => SetProperty(ref backgroundSource, value); }

    [JsonPropertyName("backgroundFit")]
    public string BackgroundFit { get => backgroundFit; set => SetProperty(ref backgroundFit, value); }

    [JsonPropertyName("backgroundFillColor")]
    public string BackgroundFillColor { get => backgroundFillColor; set => SetProperty(ref backgroundFillColor, value); }

    [JsonPropertyName("backgroundVideoVolume")]
    public int BackgroundVideoVolume { get => backgroundVideoVolume; set => SetProperty(ref backgroundVideoVolume, value); }

    /// <summary>Silences video playback while retaining the configured volume.</summary>
    [JsonPropertyName("backgroundVideoMuted")]
    public bool BackgroundVideoMuted { get => backgroundVideoMuted; set => SetProperty(ref backgroundVideoMuted, value); }

    /// <summary>固定投影蔚蓝档案设置的兼容入口；不跟随当前查看条目，也不单独写入 JSON。</summary>
    [JsonIgnore]
    public string ResourcePanelUid
    {
        get => GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).ResourcePanelUid;
        set
        {
            var game = GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId);
            if (EqualityComparer<string>.Default.Equals(game.ResourcePanelUid, value))
            {
                return;
            }

            game.ResourcePanelUid = value;
            OnPropertyChanged();
        }
    }

    [JsonPropertyName("updateChannel")]
    public string UpdateChannel { get => updateChannel; set => SetProperty(ref updateChannel, value); }

    [JsonPropertyName("logLevel")]
    public string LogLevel { get => logLevel; set => SetProperty(ref logLevel, value); }

    /// <summary>固定投影蔚蓝档案设置的兼容入口；不跟随当前查看条目，也不单独写入 JSON。</summary>
    [JsonIgnore]
    public string ResourcePanelUidSource
    {
        get => GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).ResourcePanelUidSource;
        set
        {
            var game = GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId);
            if (EqualityComparer<string>.Default.Equals(game.ResourcePanelUidSource, value))
            {
                return;
            }

            game.ResourcePanelUidSource = value;
            OnPropertyChanged();
        }
    }

    /// <summary>固定投影蔚蓝档案设置的兼容入口；不跟随当前查看条目，也不单独写入 JSON。</summary>
    [JsonIgnore]
    public GameRuntimeSettings GameRuntime
    {
        get => GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).GameRuntime;
        set
        {
            var game = GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId);
            if (EqualityComparer<GameRuntimeSettings>.Default.Equals(game.GameRuntime, value))
            {
                return;
            }

            game.GameRuntime = value;
            OnPropertyChanged();
        }
    }

    [JsonPropertyName("statusDetailMode")]
    public string StatusDetailMode { get => statusDetailMode; set => SetProperty(ref statusDetailMode, value); }

    /// <summary>用户在设置向导中同意的协议版本；空值表示尚未同意。</summary>
    [JsonPropertyName("acceptedAgreementVersion")]
    public string AcceptedAgreementVersion
    {
        get => acceptedAgreementVersion;
        set => SetProperty(ref acceptedAgreementVersion, value);
    }

    /// <summary>
    /// Deep-clones this settings object, preserving missing or explicitly null game entries
    /// for the document reader to validate instead of silently creating default game settings.
    /// Shared by <c>LauncherSettingsService.NormalizeSettings</c> and <see cref="Services.SettingsEditor"/>.
    /// </summary>
    public LauncherSettings DeepClone()
    {
        return new LauncherSettings(this);
    }

    /// <summary>
    /// Copy constructor for deep cloning. Copies all settings properties,
    /// including detached game entries and a shallow copy of <see cref="ThemeColorPalette"/> (strings are immutable).
    /// ⚠️ When adding a new setting property to this class,
    /// you MUST add a corresponding line to this constructor.
    /// Failure to do so results in silent shallow copy of the new property.
    /// The same property must also be added to <see cref="ComparedProperties"/>, otherwise state
    /// identity ignores it and the settings page's save button stops tracking that field.
    /// LauncherSettingsTests guards both lists.
    /// </summary>
    public LauncherSettings(LauncherSettings other)
    {
        FormatVersion = other.FormatVersion;
        LastViewedGameId = other.LastViewedGameId;
        if (other.Games is null)
        {
            Games = null!;
        }
        else
        {
            var copiedGames = new Dictionary<string, GameSettings>(StringComparer.Ordinal);
            foreach (var (gameId, settings) in other.Games)
            {
                copiedGames.Add(gameId, settings?.DeepClone()!);
            }

            Games = copiedGames;
        }

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
        // A hand-edited or corrupted settings file can carry "themeColorPalette": null;
        // NormalizeSettings deep-clones before its own ?? [] guard runs, so the copy
        // constructor must tolerate null without crashing the load path.
        ThemeColorPalette = other.ThemeColorPalette is null ? [] : [.. other.ThemeColorPalette];
        SelectedThemeColorPaletteIndex = other.SelectedThemeColorPaletteIndex;
        DownloadSpeedLimit = other.DownloadSpeedLimit;
        EnableHttp2 = other.EnableHttp2;
        AllowBlockedNetworkAddresses = other.AllowBlockedNetworkAddresses;
        EnableStartupUpdateCheck = other.EnableStartupUpdateCheck;
        ShowRemoteContentCard = other.ShowRemoteContentCard;
        RememberWindowPositionAndSize = other.RememberWindowPositionAndSize;
        WindowPositionX = other.WindowPositionX;
        WindowPositionY = other.WindowPositionY;
        WindowWidth = other.WindowWidth;
        WindowHeight = other.WindowHeight;
        CustomBackgroundPath = other.CustomBackgroundPath;
        BackgroundSource = other.BackgroundSource;
        BackgroundFit = other.BackgroundFit;
        BackgroundFillColor = other.BackgroundFillColor;
        BackgroundVideoVolume = other.BackgroundVideoVolume;
        BackgroundVideoMuted = other.BackgroundVideoMuted;
        StatusDetailMode = other.StatusDetailMode;
        AcceptedAgreementVersion = other.AcceptedAgreementVersion;
        UpdateChannel = other.UpdateChannel;
        LogLevel = other.LogLevel;
    }

    /// <summary>
    /// Applies only edits made since a session baseline to the specified scope.
    /// A null game identity denotes launcher preferences; navigation and window geometry
    /// are owned by the shell and are never restored from an old settings draft.
    /// </summary>
    public void ApplyDraftChanges(LauncherSettings draft, LauncherSettings baseline, string? gameId)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(baseline);
        if (gameId is not null)
        {
            GetGameSettings(gameId).ApplyDraftChanges(
                draft.GetGameSettings(gameId),
                baseline.GetGameSettings(gameId));
            return;
        }

        if (!ValuesEqual(draft.ProxyMode, baseline.ProxyMode)) ProxyMode = draft.ProxyMode;
        if (!ValuesEqual(draft.CloseBehavior, baseline.CloseBehavior)) CloseBehavior = draft.CloseBehavior;
        if (!ValuesEqual(draft.Language, baseline.Language)) Language = draft.Language;
        if (!ValuesEqual(draft.ThemeMode, baseline.ThemeMode)) ThemeMode = draft.ThemeMode;
        if (!ValuesEqual(draft.MotionMode, baseline.MotionMode)) MotionMode = draft.MotionMode;
        if (!ValuesEqual(draft.ThemeColorMode, baseline.ThemeColorMode)) ThemeColorMode = draft.ThemeColorMode;
        if (!ValuesEqual(draft.ThemeColorExtractionAlgorithm, baseline.ThemeColorExtractionAlgorithm)) ThemeColorExtractionAlgorithm = draft.ThemeColorExtractionAlgorithm;
        if (!ValuesEqual(draft.ThemeColorVariant, baseline.ThemeColorVariant)) ThemeColorVariant = draft.ThemeColorVariant;
        if (!ValuesEqual(draft.NeutralColorStrategy, baseline.NeutralColorStrategy)) NeutralColorStrategy = draft.NeutralColorStrategy;
        if (!ValuesEqual(draft.CustomThemeColor, baseline.CustomThemeColor)) CustomThemeColor = draft.CustomThemeColor;
        if (!ValuesEqual(draft.SelectedThemeColorPaletteIndex, baseline.SelectedThemeColorPaletteIndex)) SelectedThemeColorPaletteIndex = draft.SelectedThemeColorPaletteIndex;
        if (!ValuesEqual(draft.DownloadSpeedLimit, baseline.DownloadSpeedLimit)) DownloadSpeedLimit = draft.DownloadSpeedLimit;
        if (!ValuesEqual(draft.EnableHttp2, baseline.EnableHttp2)) EnableHttp2 = draft.EnableHttp2;
        if (!ValuesEqual(draft.AllowBlockedNetworkAddresses, baseline.AllowBlockedNetworkAddresses)) AllowBlockedNetworkAddresses = draft.AllowBlockedNetworkAddresses;
        if (!ValuesEqual(draft.EnableStartupUpdateCheck, baseline.EnableStartupUpdateCheck)) EnableStartupUpdateCheck = draft.EnableStartupUpdateCheck;
        if (!ValuesEqual(draft.ShowRemoteContentCard, baseline.ShowRemoteContentCard)) ShowRemoteContentCard = draft.ShowRemoteContentCard;
        if (!ValuesEqual(draft.RememberWindowPositionAndSize, baseline.RememberWindowPositionAndSize)) RememberWindowPositionAndSize = draft.RememberWindowPositionAndSize;
        if (!ValuesEqual(draft.CustomBackgroundPath, baseline.CustomBackgroundPath)) CustomBackgroundPath = draft.CustomBackgroundPath;
        if (!ValuesEqual(draft.BackgroundSource, baseline.BackgroundSource)) BackgroundSource = draft.BackgroundSource;
        if (!ValuesEqual(draft.BackgroundFit, baseline.BackgroundFit)) BackgroundFit = draft.BackgroundFit;
        if (!ValuesEqual(draft.BackgroundFillColor, baseline.BackgroundFillColor)) BackgroundFillColor = draft.BackgroundFillColor;
        if (!ValuesEqual(draft.BackgroundVideoVolume, baseline.BackgroundVideoVolume)) BackgroundVideoVolume = draft.BackgroundVideoVolume;
        if (!ValuesEqual(draft.BackgroundVideoMuted, baseline.BackgroundVideoMuted)) BackgroundVideoMuted = draft.BackgroundVideoMuted;
        if (!ValuesEqual(draft.StatusDetailMode, baseline.StatusDetailMode)) StatusDetailMode = draft.StatusDetailMode;
        if (!ValuesEqual(draft.AcceptedAgreementVersion, baseline.AcceptedAgreementVersion)) AcceptedAgreementVersion = draft.AcceptedAgreementVersion;
        if (!ValuesEqual(draft.UpdateChannel, baseline.UpdateChannel)) UpdateChannel = draft.UpdateChannel;
        if (!ValuesEqual(draft.LogLevel, baseline.LogLevel)) LogLevel = draft.LogLevel;
        if (!ValuesEqual(draft.ThemeColorPalette, baseline.ThemeColorPalette))
        {
            ThemeColorPalette = draft.ThemeColorPalette is null ? null! : [.. draft.ThemeColorPalette];
        }
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
        (nameof(FormatVersion), settings => settings.FormatVersion),
        (nameof(LastViewedGameId), settings => settings.LastViewedGameId),
        (nameof(Games), settings => settings.Games),
        (nameof(GamePath), settings => settings.FindBlueArchiveSettings()?.GamePath),
        (nameof(LaunchCheckMode), settings => settings.FindBlueArchiveSettings()?.LaunchCheckMode),
        (nameof(ProxyMode), settings => settings.ProxyMode),
        (nameof(CloseBehavior), settings => settings.CloseBehavior),
        (nameof(AfterLaunchBehavior), settings => settings.FindBlueArchiveSettings()?.AfterLaunchBehavior),
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
        (nameof(AllowBlockedNetworkAddresses), settings => settings.AllowBlockedNetworkAddresses),
        (nameof(EnableStartupUpdateCheck), settings => settings.EnableStartupUpdateCheck),
        (nameof(ShowRemoteContentCard), settings => settings.ShowRemoteContentCard),
        (nameof(RememberWindowPositionAndSize), settings => settings.RememberWindowPositionAndSize),
        (nameof(WindowPositionX), settings => settings.WindowPositionX),
        (nameof(WindowPositionY), settings => settings.WindowPositionY),
        (nameof(WindowWidth), settings => settings.WindowWidth),
        (nameof(WindowHeight), settings => settings.WindowHeight),
        (nameof(PatchUrlGroup), settings => settings.FindBlueArchiveSettings()?.PatchUrlGroup),
        (nameof(CustomBackgroundPath), settings => settings.CustomBackgroundPath),
        (nameof(BackgroundSource), settings => settings.BackgroundSource),
        (nameof(BackgroundFit), settings => settings.BackgroundFit),
        (nameof(BackgroundFillColor), settings => settings.BackgroundFillColor),
        (nameof(BackgroundVideoVolume), settings => settings.BackgroundVideoVolume),
        (nameof(BackgroundVideoMuted), settings => settings.BackgroundVideoMuted),
        (nameof(ResourcePanelUid), settings => settings.FindBlueArchiveSettings()?.ResourcePanelUid),
        (nameof(ResourcePanelUidSource), settings => settings.FindBlueArchiveSettings()?.ResourcePanelUidSource),
        (nameof(StatusDetailMode), settings => settings.StatusDetailMode),
        (nameof(AcceptedAgreementVersion), settings => settings.AcceptedAgreementVersion),
        (nameof(UpdateChannel), settings => settings.UpdateChannel),
        (nameof(LogLevel), settings => settings.LogLevel),
        (nameof(GameRuntime), settings => settings.FindBlueArchiveSettings()?.GameRuntime)
    ];

    /// <summary>
    /// Reports whether <paramref name="other"/> holds the same settings state — the identity the
    /// settings page's dirty flag and save button are derived from, and the counterpart of
    /// <see cref="DeepClone"/>: clone, then compare, must answer "same".
    /// Every entry goes through <see cref="ValuesEqual"/>: palettes compare in order, while games
    /// compare by Ordinal key and each game/runtime recursively compares its own settings state.
    /// The fixed BA aliases remain declared here so property guards cover the compatibility API.
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
    /// leave the editor permanently dirty. That case is currently unreachable — the presentation
    /// layer's window geometry capture filters with <c>double.IsFinite</c> and
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

        if (left is GameSettings leftGame && right is GameSettings rightGame)
        {
            return leftGame.HasSameSettingsState(rightGame);
        }

        if (left is Dictionary<string, GameSettings> leftGames
            && right is Dictionary<string, GameSettings> rightGames)
        {
            if (leftGames.Count != rightGames.Count)
            {
                return false;
            }

            // Dictionary insertion order is not settings state; game identities use Ordinal keys.
            var ordinalRight = rightGames.Comparer.Equals(StringComparer.Ordinal)
                ? rightGames
                : new Dictionary<string, GameSettings>(rightGames, StringComparer.Ordinal);
            foreach (var (gameId, settings) in leftGames)
            {
                if (!ordinalRight.TryGetValue(gameId, out var otherSettings)
                    || !ValuesEqual(settings, otherSettings))
                {
                    return false;
                }
            }

            return true;
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
    /// <param name="buildIdentity">
    /// The host-supplied build identity. The pre-release channel default is decided by
    /// <see cref="LauncherBuildIdentity.IsPrerelease"/> on the value the composition root injected —
    /// never by reflecting over whatever assembly happens to be the entry point. Callers for which
    /// the channel is irrelevant (motion preferences) may omit it and get the stable channel.
    /// </param>
    public static LauncherSettings CreateDefaults(LauncherBuildIdentity? buildIdentity = null)
    {
        var settings = new LauncherSettings();

        if (buildIdentity?.IsPrerelease == true)
        {
            settings.UpdateChannel = UpdateChannels.Beta;
        }

        // Chinese users are the primary audience for Cafe-localised game resources;
        // default to Cafe source so they get Chinese text without manual setup.
        if (IsChineseUICulture())
        {
            settings.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).PatchUrlGroup =
                PatchUrlGroups.Cafe;
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
