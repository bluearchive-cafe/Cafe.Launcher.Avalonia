using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Services;

public sealed partial class LocalizedStrings : ObservableObject
{
    [ObservableProperty] private string settings = "";
    [ObservableProperty] private string settingsStatus = "";
    [ObservableProperty] private string settingsGameFiles = "";
    [ObservableProperty] private string settingsDownloadNetwork = "";
    [ObservableProperty] private string settingsAppPreferences = "";
    [ObservableProperty] private string minimize = "";
    [ObservableProperty] private string close = "";
    [ObservableProperty] private string version = "";
    [ObservableProperty] private string executable = "";
    [ObservableProperty] private string path = "";
    [ObservableProperty] private string changePath = "";
    [ObservableProperty] private string refresh = "";
    [ObservableProperty] private string refreshTooltip = "";
    [ObservableProperty] private string stop = "";
    [ObservableProperty] private string officialSite = "";
    [ObservableProperty] private string startGame = "";
    [ObservableProperty] private string resourcePanel = "";
    [ObservableProperty] private string resourcePanelDescription = "";
    [ObservableProperty] private string resourcePanelGameText = "";
    [ObservableProperty] private string resourcePanelMainVoice = "";
    [ObservableProperty] private string resourcePanelMedia = "";
    [ObservableProperty] private string resourcePanelOfficialVersion = "";
    [ObservableProperty] private string resourcePanelLocalizedVersion = "";
    [ObservableProperty] private string resourcePanelLoading = "";
    [ObservableProperty] private string resourcePanelReady = "";
    [ObservableProperty] private string resourcePanelWaiting = "";
    [ObservableProperty] private string resourcePanelFailed = "";
    [ObservableProperty] private string resourcePanelRefresh = "";
    [ObservableProperty] private string resourcePanelSave = "";
    [ObservableProperty] private string resourcePanelUid = "";
    [ObservableProperty] private string resourcePanelManualUid = "";
    [ObservableProperty] private string resourcePanelSaveUid = "";
    [ObservableProperty] private string resourcePanelUidMissing = "";
    [ObservableProperty] private string resourcePanelLoadFailed = "";
    [ObservableProperty] private string resourcePanelSaved = "";
    [ObservableProperty] private string resourcePanelUidSaved = "";
    [ObservableProperty] private string resourcePanelUidEmpty = "";
    [ObservableProperty] private string resourcePanelCurrentUid = "";
    [ObservableProperty] private string notice = "";
    [ObservableProperty] private string banners = "";
    [ObservableProperty] private string news = "";
    [ObservableProperty] private string socialMedia = "";
    [ObservableProperty] private string gamePath = "";
    [ObservableProperty] private string choose = "";
    [ObservableProperty] private string launchCheck = "";
    [ObservableProperty] private string proxy = "";
    [ObservableProperty] private string closeBehavior = "";
    [ObservableProperty] private string language = "";
    [ObservableProperty] private string theme = "";
    [ObservableProperty] private string themeDescription = "";
    [ObservableProperty] private string themeSystem = "";
    [ObservableProperty] private string themeLight = "";
    [ObservableProperty] private string themeDark = "";
    [ObservableProperty] private string themeColor = "";
    [ObservableProperty] private string themeColorDescription = "";
    [ObservableProperty] private string themeColorDefault = "";
    [ObservableProperty] private string themeColorSystem = "";
    [ObservableProperty] private string themeColorWallpaper = "";
    [ObservableProperty] private string themeColorCustom = "";
    [ObservableProperty] private string themeColorPalette = "";
    [ObservableProperty] private string themeColorPaletteDescription = "";
    [ObservableProperty] private string refreshThemeColorPalette = "";
    [ObservableProperty] private string openColorPalette = "";
    [ObservableProperty] private string save = "";
    [ObservableProperty] private string gameManagement = "";
    [ObservableProperty] private string gameManagementDescription = "";
    [ObservableProperty] private string repair = "";
    [ObservableProperty] private string uninstall = "";
    [ObservableProperty] private string about = "";
    [ObservableProperty] private string aboutDescription = "";
    [ObservableProperty] private string agreement = "";
    [ObservableProperty] private string privacy = "";
    [ObservableProperty] private string updateEndpoint = "";
    [ObservableProperty] private string confirmUninstall = "";
    [ObservableProperty] private string uninstallConfirmDescription = "";
    [ObservableProperty] private string cancel = "";
    [ObservableProperty] private string repairWarning = "";
    [ObservableProperty] private string copyright = "";
    [ObservableProperty] private string repairConfirm = "";
    [ObservableProperty] private string repairConfirmDescription = "";
    [ObservableProperty] private string noticeConfirm = "";
    [ObservableProperty] private string noticeExit = "";
    [ObservableProperty] private string showLauncher = "";
    [ObservableProperty] private string exitLauncher = "";
    [ObservableProperty] private string githubRepository = "";
    [ObservableProperty] private string checkUpdates = "";
    [ObservableProperty] private string checkUpdatesUnavailable = "";
    [ObservableProperty] private string trayOpenLauncher = "";
    [ObservableProperty] private string trayExitLauncher = "";
    [ObservableProperty] private string aboutLinks = "";
    [ObservableProperty] private string aboutCopyrightText = "";
    [ObservableProperty] private string aboutDisclaimerText = "";
    [ObservableProperty] private string customBackground = "";
    [ObservableProperty] private string customBackgroundDescription = "";
    [ObservableProperty] private string chooseImage = "";
    [ObservableProperty] private string chooseFolder = "";
    [ObservableProperty] private string clearBackground = "";
    [ObservableProperty] private string backgroundSet = "";
    [ObservableProperty] private string backgroundCleared = "";
    [ObservableProperty] private string backgroundSource = "";
    [ObservableProperty] private string backgroundSourceDescription = "";
    [ObservableProperty] private string backgroundSourceBundled = "";
    [ObservableProperty] private string backgroundSourceRemote = "";
    [ObservableProperty] private string backgroundSourceCustom = "";
    [ObservableProperty] private string versionInfo = "";
    [ObservableProperty] private string runtimeInfo = "";
    [ObservableProperty] private string buildInfo = "";
    [ObservableProperty] private string downloadSpeedLimit = "";
    [ObservableProperty] private string downloadSpeedLimitDescription = "";
    [ObservableProperty] private string notificationSettings = "";
    [ObservableProperty] private string toastNotifications = "";
    [ObservableProperty] private string remoteContentCard = "";
    [ObservableProperty] private string showRemoteContentCard = "";
    [ObservableProperty] private string toggleOn = "";
    [ObservableProperty] private string toggleOff = "";
    [ObservableProperty] private string launchCheckDescription = "";
    [ObservableProperty] private string proxyDescription = "";
    [ObservableProperty] private string downloadSource = "";
    [ObservableProperty] private string downloadSourceDescription = "";
    [ObservableProperty] private string downloadSourceOfficial = "";
    [ObservableProperty] private string downloadSourceCafe = "";
    [ObservableProperty] private string downloadSourceChangedRepairPrompt = "";
    [ObservableProperty] private string pause = "";
    [ObservableProperty] private string resume = "";
    [ObservableProperty] private string paused = "";
    [ObservableProperty] private string confirmStop = "";
    [ObservableProperty] private string stopDownloadConfirm = "";
    [ObservableProperty] private string unsavedChanges = "";
    [ObservableProperty] private string unsavedChangesMessage = "";
    [ObservableProperty] private string discardChanges = "";
    [ObservableProperty] private string keepEditing = "";
    [ObservableProperty] private string chooseInstallFolder = "";
    [ObservableProperty] private string chooseBackgroundImageTitle = "";
    [ObservableProperty] private string chooseBackgroundFolderTitle = "";
    [ObservableProperty] private string carouselPage = "";
    [ObservableProperty] private string pauseCarousel = "";
    [ObservableProperty] private string resumeCarousel = "";
    [ObservableProperty] private string bannerLoadingFailed = "";
    [ObservableProperty] private string statusNetworkLoaded = "";
    [ObservableProperty] private string statusLaunchCheckLocal = "";
    [ObservableProperty] private string statusLaunchCheckRemote = "";
    [ObservableProperty] private string statusLaunchCheckNone = "";
    [ObservableProperty] private string gameLaunchedMinimized = "";
    [ObservableProperty] private string migrationWizardTitle = "";
    [ObservableProperty] private string migrationWizardDescription = "";
    [ObservableProperty] private string migrationGamePathLabel = "";
    [ObservableProperty] private string migrationGamePathBrowse = "";
    [ObservableProperty] private string migrationProxyLabel = "";
    [ObservableProperty] private string migrationCloseBehaviorLabel = "";
    [ObservableProperty] private string migrationClickCodeFound = "";
    [ObservableProperty] private string migrationLevelDbFailed = "";
    [ObservableProperty] private string migrationGamePathNotFound = "";
    [ObservableProperty] private string migrationSkip = "";
    [ObservableProperty] private string migrationApply = "";
    [ObservableProperty] private string migrationApplied = "";
    [ObservableProperty] private string migrationNoOldLauncher = "";

    public void Apply(LocalizationService localizer)
    {
        Settings = localizer.T("settings");
        SettingsStatus = localizer.T("settingsStatus");
        SettingsGameFiles = localizer.T("settingsGameFiles");
        SettingsDownloadNetwork = localizer.T("settingsDownloadNetwork");
        SettingsAppPreferences = localizer.T("settingsAppPreferences");
        Minimize = localizer.T("minimize");
        Close = localizer.T("close");
        Version = localizer.T("version");
        Executable = localizer.T("executable");
        Path = localizer.T("path");
        ChangePath = localizer.T("changePath");
        Refresh = localizer.T("refresh");
        RefreshTooltip = localizer.T("refreshTooltip");
        Stop = localizer.T("stop");
        OfficialSite = localizer.T("officialSite");
        StartGame = localizer.T("startGame");
        ResourcePanel = localizer.T("resourcePanel");
        ResourcePanelDescription = localizer.T("resourcePanelDescription");
        ResourcePanelGameText = localizer.T("resourcePanelGameText");
        ResourcePanelMainVoice = localizer.T("resourcePanelMainVoice");
        ResourcePanelMedia = localizer.T("resourcePanelMedia");
        ResourcePanelOfficialVersion = localizer.T("resourcePanelOfficialVersion");
        ResourcePanelLocalizedVersion = localizer.T("resourcePanelLocalizedVersion");
        ResourcePanelLoading = localizer.T("resourcePanelLoading");
        ResourcePanelReady = localizer.T("resourcePanelReady");
        ResourcePanelWaiting = localizer.T("resourcePanelWaiting");
        ResourcePanelFailed = localizer.T("resourcePanelFailed");
        ResourcePanelRefresh = localizer.T("resourcePanelRefresh");
        ResourcePanelSave = localizer.T("resourcePanelSave");
        ResourcePanelUid = localizer.T("resourcePanelUid");
        ResourcePanelManualUid = localizer.T("resourcePanelManualUid");
        ResourcePanelSaveUid = localizer.T("resourcePanelSaveUid");
        ResourcePanelUidMissing = localizer.T("resourcePanelUidMissing");
        ResourcePanelLoadFailed = localizer.T("resourcePanelLoadFailed");
        ResourcePanelSaved = localizer.T("resourcePanelSaved");
        ResourcePanelUidSaved = localizer.T("resourcePanelUidSaved");
        ResourcePanelUidEmpty = localizer.T("resourcePanelUidEmpty");
        ResourcePanelCurrentUid = localizer.T("resourcePanelCurrentUid");
        Notice = localizer.T("notice");
        Banners = localizer.T("banners");
        News = localizer.T("news");
        SocialMedia = localizer.T("socialMedia");
        GamePath = localizer.T("gamePath");
        Choose = localizer.T("choose");
        LaunchCheck = localizer.T("launchCheck");
        Proxy = localizer.T("proxy");
        CloseBehavior = localizer.T("closeBehavior");
        Language = localizer.T("language");
        Theme = localizer.T("theme");
        ThemeDescription = localizer.T("themeDescription");
        ThemeSystem = localizer.T("themeSystem");
        ThemeLight = localizer.T("themeLight");
        ThemeDark = localizer.T("themeDark");
        ThemeColor = localizer.T("themeColor");
        ThemeColorDescription = localizer.T("themeColorDescription");
        ThemeColorDefault = localizer.T("themeColorDefault");
        ThemeColorSystem = localizer.T("themeColorSystem");
        ThemeColorWallpaper = localizer.T("themeColorWallpaper");
        ThemeColorCustom = localizer.T("themeColorCustom");
        ThemeColorPalette = localizer.T("themeColorPalette");
        ThemeColorPaletteDescription = localizer.T("themeColorPaletteDescription");
        RefreshThemeColorPalette = localizer.T("refreshThemeColorPalette");
        OpenColorPalette = localizer.T("openColorPalette");
        Save = localizer.T("save");
        GameManagement = localizer.T("gameManagement");
        GameManagementDescription = localizer.T("gameManagementDescription");
        Repair = localizer.T("repair");
        Uninstall = localizer.T("uninstall");
        About = localizer.T("about");
        AboutDescription = localizer.T("aboutDescription");
        Agreement = localizer.T("agreement");
        Privacy = localizer.T("privacy");
        UpdateEndpoint = localizer.T("updateEndpoint");
        ConfirmUninstall = localizer.T("confirmUninstall");
        UninstallConfirmDescription = localizer.T("uninstallConfirmDescription");
        Cancel = localizer.T("cancel");
        RepairWarning = localizer.T("repairWarning");
        Copyright = localizer.T("copyright");
        RepairConfirm = localizer.T("repairConfirm");
        RepairConfirmDescription = localizer.T("repairConfirmDescription");
        NoticeConfirm = localizer.T("noticeConfirm");
        NoticeExit = localizer.T("noticeExit");
        ShowLauncher = localizer.T("showLauncher");
        ExitLauncher = localizer.T("exitLauncher");
        GithubRepository = localizer.T("githubRepository");
        CheckUpdates = localizer.T("checkUpdates");
        CheckUpdatesUnavailable = localizer.T("checkUpdatesUnavailable");
        TrayOpenLauncher = localizer.T("trayOpenLauncher");
        TrayExitLauncher = localizer.T("trayExitLauncher");
        AboutLinks = localizer.T("aboutLinks");
        AboutCopyrightText = localizer.T("aboutCopyrightText");
        AboutDisclaimerText = localizer.T("aboutDisclaimerText");
        CustomBackground = localizer.T("customBackground");
        CustomBackgroundDescription = localizer.T("customBackgroundDescription");
        ChooseImage = localizer.T("chooseImage");
        ChooseFolder = localizer.T("chooseFolder");
        ClearBackground = localizer.T("clearBackground");
        BackgroundSet = localizer.T("backgroundSet");
        BackgroundCleared = localizer.T("backgroundCleared");
        BackgroundSource = localizer.T("backgroundSource");
        BackgroundSourceDescription = localizer.T("backgroundSourceDescription");
        BackgroundSourceBundled = localizer.T("backgroundSourceBundled");
        BackgroundSourceRemote = localizer.T("backgroundSourceRemote");
        BackgroundSourceCustom = localizer.T("backgroundSourceCustom");
        VersionInfo = localizer.T("versionInfo");
        RuntimeInfo = localizer.T("runtimeInfo");
        BuildInfo = localizer.T("buildInfo");
        DownloadSpeedLimit = localizer.T("downloadSpeedLimit");
        DownloadSpeedLimitDescription = localizer.T("downloadSpeedLimitDescription");
        NotificationSettings = localizer.T("notificationSettings");
        ToastNotifications = localizer.T("toastNotifications");
        RemoteContentCard = localizer.T("remoteContentCard");
        ShowRemoteContentCard = localizer.T("showRemoteContentCard");
        ToggleOn = localizer.T("toggleOn");
        ToggleOff = localizer.T("toggleOff");
        LaunchCheckDescription = localizer.T("launchCheckDescription");
        ProxyDescription = localizer.T("proxyDescription");
        DownloadSource = localizer.T("downloadSource");
        DownloadSourceDescription = localizer.T("downloadSourceDescription");
        DownloadSourceOfficial = localizer.T("downloadSourceOfficial");
        DownloadSourceCafe = localizer.T("downloadSourceCafe");
        DownloadSourceChangedRepairPrompt = localizer.T("downloadSourceChangedRepairPrompt");
        Pause = localizer.T("pause");
        Resume = localizer.T("resume");
        Paused = localizer.T("paused");
        ConfirmStop = localizer.T("confirmStop");
        StopDownloadConfirm = localizer.T("stopDownloadConfirm");
        UnsavedChanges = localizer.T("unsavedChanges");
        UnsavedChangesMessage = localizer.T("unsavedChangesMessage");
        DiscardChanges = localizer.T("discardChanges");
        KeepEditing = localizer.T("keepEditing");
        ChooseInstallFolder = localizer.T("chooseInstallFolder");
        ChooseBackgroundImageTitle = localizer.T("chooseBackgroundImageTitle");
        ChooseBackgroundFolderTitle = localizer.T("chooseBackgroundFolderTitle");
        CarouselPage = localizer.T("carouselPage");
        PauseCarousel = localizer.T("pauseCarousel");
        ResumeCarousel = localizer.T("resumeCarousel");
        BannerLoadingFailed = localizer.T("bannerLoadingFailed");
        StatusNetworkLoaded = localizer.T("statusNetworkLoaded");
        StatusLaunchCheckLocal = localizer.T("statusLaunchCheckLocal");
        StatusLaunchCheckRemote = localizer.T("statusLaunchCheckRemote");
        StatusLaunchCheckNone = localizer.T("statusLaunchCheckNone");
        GameLaunchedMinimized = localizer.T("gameLaunchedMinimized");
        MigrationWizardTitle = localizer.T("migrationWizardTitle");
        MigrationWizardDescription = localizer.T("migrationWizardDescription");
        MigrationGamePathLabel = localizer.T("migrationGamePathLabel");
        MigrationGamePathBrowse = localizer.T("migrationGamePathBrowse");
        MigrationProxyLabel = localizer.T("migrationProxyLabel");
        MigrationCloseBehaviorLabel = localizer.T("migrationCloseBehaviorLabel");
        MigrationClickCodeFound = localizer.T("migrationClickCodeFound");
        MigrationLevelDbFailed = localizer.T("migrationLevelDbFailed");
        MigrationGamePathNotFound = localizer.T("migrationGamePathNotFound");
        MigrationSkip = localizer.T("migrationSkip");
        MigrationApply = localizer.T("migrationApply");
        MigrationApplied = localizer.T("migrationApplied");
        MigrationNoOldLauncher = localizer.T("migrationNoOldLauncher");
    }
}

public sealed class LocalizationService
{
    private static readonly Dictionary<string, Dictionary<string, string>> Resources = new(StringComparer.Ordinal);
    private static readonly string[] SupportedLocales = [LauncherLanguages.English, LauncherLanguages.SimplifiedChinese, LauncherLanguages.Japanese];
    private static bool resourcesLoaded;
    private static readonly object LoadLock = new();

    /// <summary>
    /// Pre-populates resources for unit testing without AssetLoader.
    /// Call once before creating LocalizationService instances in tests.
    /// </summary>
    internal static void InitializeForTesting(Dictionary<string, Dictionary<string, string>> resources)
    {
        lock (LoadLock)
        {
            Resources.Clear();
            foreach (var (locale, dict) in resources)
            {
                Resources[locale] = new Dictionary<string, string>(dict, StringComparer.Ordinal);
            }

            resourcesLoaded = true;
        }
    }

    public string CurrentLanguage { get; private set; } = LauncherLanguages.English;

    public event EventHandler? LanguageChanged;

    private static void EnsureResourcesLoaded()
    {
        if (resourcesLoaded)
            return;

        lock (LoadLock)
        {
            if (resourcesLoaded)
                return;

            foreach (var locale in SupportedLocales)
            {
                try
                {
                    var dict = LoadLocaleFromJson(locale);
                    Resources[locale] = dict;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LocalizationService: failed to load {locale}.json: {ex.Message}");
                    Resources[locale] = new Dictionary<string, string>(StringComparer.Ordinal);
                }
            }

            resourcesLoaded = true;
        }
    }

    private static Dictionary<string, string> LoadLocaleFromJson(string locale)
    {
        var assemblyName = typeof(LocalizationService).Assembly.GetName().Name;
        var uri = new Uri($"avares://{assemblyName}/Assets/Locales/{locale}.json");
        using var stream = global::Avalonia.Platform.AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return dict is not null
            ? new Dictionary<string, string>(dict, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public string SetLanguage(string language)
    {
        CurrentLanguage = ResolveLanguage(language);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        return CurrentLanguage;
    }

    public string T(string key)
    {
        EnsureResourcesLoaded();

        if (Resources.TryGetValue(CurrentLanguage, out var current)
            && current.TryGetValue(key, out var value))
        {
            return value;
        }

        return Resources.TryGetValue(LauncherLanguages.English, out var english)
            && english.TryGetValue(key, out var fallback)
                ? fallback
                : key;
    }

    public string F(string key, params object?[] args)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), args);
        }
        catch (FormatException)
        {
            return T(key);
        }
    }

    public static IReadOnlyList<LanguageOption> GetLanguageOptions()
    {
        return
        [
            new LanguageOption { Code = LauncherLanguages.Auto, DisplayName = "Auto" },
            new LanguageOption { Code = LauncherLanguages.English, DisplayName = "English" },
            new LanguageOption { Code = LauncherLanguages.SimplifiedChinese, DisplayName = "简体中文" },
            new LanguageOption { Code = LauncherLanguages.Japanese, DisplayName = "日本語" }
        ];
    }

    public static string ResolveLanguage(string? language)
    {
        return language switch
        {
            LauncherLanguages.English => LauncherLanguages.English,
            LauncherLanguages.SimplifiedChinese => LauncherLanguages.SimplifiedChinese,
            LauncherLanguages.Japanese => LauncherLanguages.Japanese,
            LauncherLanguages.Auto => ResolveSystemLanguage(),
            _ => ResolveSystemLanguage()
        };
    }

    private static string ResolveSystemLanguage()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return LauncherLanguages.SimplifiedChinese;
        }

        if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return LauncherLanguages.Japanese;
        }

        return LauncherLanguages.English;
    }
}
