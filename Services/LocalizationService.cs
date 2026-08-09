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
    [ObservableProperty] private string settingsGroupGameFiles = "";
    [ObservableProperty] private string settingsGroupAppPreferences = "";
    [ObservableProperty] private string settingsGroupAboutActions = "";
    [ObservableProperty] private string settingsGroupBackground = "";
    [ObservableProperty] private string settingsGroupThemeColor = "";
    [ObservableProperty] private string settingsCategoryGeneral = "";
    [ObservableProperty] private string settingsCategoryGame = "";
    [ObservableProperty] private string settingsCategoryDownloadNetwork = "";
    [ObservableProperty] private string settingsCategoryAppearance = "";
    [ObservableProperty] private string settingsCategoryAdvanced = "";
    [ObservableProperty] private string settingsCategoryAbout = "";
    [ObservableProperty] private string minimize = "";
    [ObservableProperty] private string close = "";
    [ObservableProperty] private string version = "";
    [ObservableProperty] private string executable = "";
    [ObservableProperty] private string network = "";
    [ObservableProperty] private string diskSpaceCheck = "";
    [ObservableProperty] private string diskSpaceInsufficientDetail = "";
    [ObservableProperty] private string diskSpaceOkSuffix = "";
    [ObservableProperty] private string diskSpaceShortSuffix = "";
    [ObservableProperty] private string verificationRetry = "";
    [ObservableProperty] private string verificationFailed = "";
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
    [ObservableProperty] private string resourcePanelUidInvalidFormat = "";
    [ObservableProperty] private string resourcePanelCurrentUid = "";
    [ObservableProperty] private string resourcePanelChangeUid = "";
    [ObservableProperty] private string resourcePanelUidSource = "";
    [ObservableProperty] private string resourcePanelUidSourceAuto = "";
    [ObservableProperty] private string resourcePanelUidSourceCustom = "";
    [ObservableProperty] private string resourcePanelEditUid = "";
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
    [ObservableProperty] private string motionMode = "";
    [ObservableProperty] private string motionModeDescription = "";
    [ObservableProperty] private string motionModeSystem = "";
    [ObservableProperty] private string motionModeFull = "";
    [ObservableProperty] private string motionModeReduced = "";
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
    [ObservableProperty] private string aboutActionsGeneral = "";
    [ObservableProperty] private string aboutDescription = "";
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
    [ObservableProperty] private string gitHubRepository = "";
    [ObservableProperty] private string helpDocs = "";
    [ObservableProperty] private string checkUpdates = "";
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
    [ObservableProperty] private string backgroundSource = "";
    [ObservableProperty] private string backgroundSourceDescription = "";
    [ObservableProperty] private string backgroundSourceBundled = "";
    [ObservableProperty] private string backgroundSourceRemote = "";
    [ObservableProperty] private string backgroundSourceCustom = "";
    [ObservableProperty] private string backgroundFit = "";
    [ObservableProperty] private string backgroundFitDescription = "";
    [ObservableProperty] private string backgroundFitFill = "";
    [ObservableProperty] private string backgroundFitUniform = "";
    [ObservableProperty] private string backgroundFitUniformToFill = "";
    [ObservableProperty] private string backgroundFillColor = "";
    [ObservableProperty] private string backgroundFillColorDescription = "";
    [ObservableProperty] private string versionInfo = "";
    [ObservableProperty] private string buildInfo = "";
    [ObservableProperty] private string debugPanel = "";
    [ObservableProperty] private string debugActionFailureMessage = "";
    [ObservableProperty] private string debugActionFailureTitle = "";
    [ObservableProperty] private string debugActionToastMessage = "";
    [ObservableProperty] private string debugActionToastTitle = "";
    [ObservableProperty] private string debugCriticalErrorMessage = "";
    [ObservableProperty] private string debugCriticalErrorTriggered = "";
    [ObservableProperty] private string debugExportCancelled = "";
    [ObservableProperty] private string debugGameOperations = "";
    [ObservableProperty] private string debugHandledErrorMessage = "";
    [ObservableProperty] private string debugHandledErrorSimulated = "";
    [ObservableProperty] private string debugIdle = "";
    [ObservableProperty] private string debugLogEntryWritten = "";
    [ObservableProperty] private string debugLogExportUnavailable = "";
    [ObservableProperty] private string debugLogLevelSet = "";
    [ObservableProperty] private string debugOutput = "";
    [ObservableProperty] private string debugPauseResume = "";
    [ObservableProperty] private string debugResetSettingsTitle = "";
    [ObservableProperty] private string debugResetSettingsDescription = "";
    [ObservableProperty] private string debugResetSettingsConfirm = "";
    [ObservableProperty] private string debugSettingsReadFailed = "";
    [ObservableProperty] private string debugSettingsReset = "";
    [ObservableProperty] private string debugSimulateError = "";
    [ObservableProperty] private string debugSimulateFailure = "";
    [ObservableProperty] private string debugSimulateSuccess = "";
    [ObservableProperty] private string debugStateRefreshTriggered = "";
    [ObservableProperty] private string debugStatus = "";
    [ObservableProperty] private string debugSystemInfo = "";
    [ObservableProperty] private string debugSystemInfoFormat = "";
    [ObservableProperty] private string debugTestActionToast = "";
    [ObservableProperty] private string debugTestErrorToast = "";
    [ObservableProperty] private string debugTestInfoToast = "";
    [ObservableProperty] private string debugTestLogMessage = "";
    [ObservableProperty] private string debugTestSuccessToast = "";
    [ObservableProperty] private string debugTestToastMessage = "";
    [ObservableProperty] private string debugTestWarningToast = "";
    [ObservableProperty] private string debugToastShown = "";
    [ObservableProperty] private string downloadSpeedLimit = "";
    [ObservableProperty] private string downloadSpeedLimitDescription = "";
    [ObservableProperty] private string notificationSettings = "";
    [ObservableProperty] private string remoteContentCard = "";
    [ObservableProperty] private string remoteContentLoading = "";
    [ObservableProperty] private string showRemoteContentCard = "";
    [ObservableProperty] private string toggleOn = "";
    [ObservableProperty] private string toggleOff = "";
    [ObservableProperty] private string toastSuccess = "";
    [ObservableProperty] private string toastWarning = "";
    [ObservableProperty] private string toastError = "";
    [ObservableProperty] private string toastActionFailedMessage = "";
    [ObservableProperty] private string toastActionFailedTitle = "";
    [ObservableProperty] private string toastInfo = "";
    [ObservableProperty] private string launchCheckDescription = "";
    [ObservableProperty] private string proxyDescription = "";
    [ObservableProperty] private string proxyAuto = "";
    [ObservableProperty] private string proxyDirect = "";
    [ObservableProperty] private string proxySystem = "";
    [ObservableProperty] private string downloadSource = "";
    [ObservableProperty] private string downloadSourceDescription = "";
    [ObservableProperty] private string downloadSourceOfficial = "";
    [ObservableProperty] private string downloadSourceCafe = "";
    [ObservableProperty] private string downloadSourceChangedRepairPrompt = "";
    [ObservableProperty] private string resourcePanelCafeOnlyTitle = "";
    [ObservableProperty] private string resourcePanelCafeOnlyDescription = "";
    [ObservableProperty] private string resourcePanelCafeOnlyMessage = "";
    [ObservableProperty] private string resourcePanelCafeOnlySwitch = "";
    [ObservableProperty] private string pause = "";
    [ObservableProperty] private string resume = "";
    [ObservableProperty] private string paused = "";
    [ObservableProperty] private string downloading = "";
    [ObservableProperty] private string pauseRequested = "";
    [ObservableProperty] private string resumeRequested = "";
    [ObservableProperty] private string stopRequested = "";
    [ObservableProperty] private string stopDownloadTitle = "";
    [ObservableProperty] private string stopDownloadMessage = "";
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
    [ObservableProperty] private string previousBanner = "";
    [ObservableProperty] private string nextBanner = "";
    [ObservableProperty] private string bannerLoadingFailed = "";
    [ObservableProperty] private string bannerLoading = "";
    [ObservableProperty] private string statusNetworkLoaded = "";
    [ObservableProperty] private string statusDetailMode = "";
    [ObservableProperty] private string statusDetailModeDescription = "";
    [ObservableProperty] private string statusDetailModeHidden = "";
    [ObservableProperty] private string statusDetailModeCompact = "";
    [ObservableProperty] private string statusDetailModeDetailed = "";
    [ObservableProperty] private string statusLaunchCheckLocal = "";
    [ObservableProperty] private string statusLaunchCheckRemote = "";
    [ObservableProperty] private string statusLaunchCheckNone = "";
    [ObservableProperty] private string gameLaunchedMinimized = "";
    [ObservableProperty] private string launcherUpdateChannel = "";
    [ObservableProperty] private string launcherUpdateChannelStable = "";
    [ObservableProperty] private string launcherUpdateChannelBeta = "";
    [ObservableProperty] private string launcherUpdateAvailableTitle = "";
    [ObservableProperty] private string launcherUpdateAvailableMessage = "";
    [ObservableProperty] private string launcherUpdateSelectFile = "";
    [ObservableProperty] private string launcherUpdateFileSize = "";
    [ObservableProperty] private string launcherUpdateDownload = "";
    [ObservableProperty] private string launcherUpdateLater = "";
    [ObservableProperty] private string crashRecoveryTitle = "";
    [ObservableProperty] private string crashRecoveryDescription = "";
    [ObservableProperty] private string crashRecoveryMessage = "";
    [ObservableProperty] private string crashRecoveryContinue = "";
    [ObservableProperty] private string crashRecoveryResetSettings = "";
    [ObservableProperty] private string crashRecoveryViewLog = "";
    [ObservableProperty] private string errorDialogTitle = "";
    [ObservableProperty] private string errorDialogDescription = "";
    [ObservableProperty] private string errorDialogContinue = "";
    [ObservableProperty] private string errorDialogViewLog = "";
    [ObservableProperty] private string errorDialogCopyDetails = "";
    [ObservableProperty] private string buildTimeLabel = "";
    [ObservableProperty] private string logViewerTitle = "";
    [ObservableProperty] private string logFilterAll = "";
    [ObservableProperty] private string logFilterVerbose = "";
    [ObservableProperty] private string logFilterDebug = "";
    [ObservableProperty] private string logFilterInfo = "";
    [ObservableProperty] private string logFilterWarn = "";
    [ObservableProperty] private string logFilterError = "";
    [ObservableProperty] private string logFilterFatal = "";
    [ObservableProperty] private string logSearchPlaceholder = "";
    [ObservableProperty] private string logLoadEarlier = "";
    [ObservableProperty] private string logNoMatchingEntries = "";
    [ObservableProperty] private string exportLogs = "";
    [ObservableProperty] private string viewLog = "";
    [ObservableProperty] private string installUpdateFailedTitle = "";
    [ObservableProperty] private string retry = "";
    [ObservableProperty] private string openDataDirectory = "";
    [ObservableProperty] private string logFiles = "";
    [ObservableProperty] private string logFilesDescription = "";
    [ObservableProperty] private string logExportFolderPickerTitle = "";
    [ObservableProperty] private string logExportSucceeded = "";
    [ObservableProperty] private string logExportFailed = "";
    [ObservableProperty] private string logLevel = "";
    [ObservableProperty] private string logLevelVerbose = "";
    [ObservableProperty] private string logLevelDebug = "";
    [ObservableProperty] private string logLevelInformation = "";
    [ObservableProperty] private string logLevelWarning = "";
    [ObservableProperty] private string logLevelError = "";
    [ObservableProperty] private string logLevelFatal = "";
    [ObservableProperty] private string selectInstalledGame = "";
    [ObservableProperty] private string gamePathUpdated = "";
    [ObservableProperty] private string gamePathUpdateFailed = "";
    [ObservableProperty] private string settingsGroupConnection = "";
    [ObservableProperty] private string settingsGroupDisplay = "";
    [ObservableProperty] private string settingsGroupDiagnostics = "";
    [ObservableProperty] private string languageDescription = "";
    [ObservableProperty] private string closeBehaviorDescription = "";
    [ObservableProperty] private string launcherUpdateChannelDescription = "";
    [ObservableProperty] private string logLevelDescription = "";
    [ObservableProperty] private string toastNotificationsDescription = "";
    [ObservableProperty] private string enableStartupUpdateCheck = "";
    [ObservableProperty] private string enableStartupUpdateCheckDescription = "";
    [ObservableProperty] private string startupUpdateAvailable = "";
    [ObservableProperty] private string remoteContentCardDescription = "";
    [ObservableProperty] private string setupWizardWelcomeTitle = "";
    [ObservableProperty] private string setupWizardWelcomeText = "";
    [ObservableProperty] private string setupWizardLanguage = "";
    [ObservableProperty] private string setupWizardLanguageHint = "";
    [ObservableProperty] private string setupWizardDownloadSource = "";
    [ObservableProperty] private string setupWizardDownloadSourceCafeDescription = "";
    [ObservableProperty] private string setupWizardDownloadSourceHint = "";
    [ObservableProperty] private string setupWizardDownloadSourceOfficialDescription = "";
    [ObservableProperty] private string setupWizardEditStep = "";
    [ObservableProperty] private string setupWizardGamePath = "";
    [ObservableProperty] private string setupWizardGamePathAvailable = "";
    [ObservableProperty] private string setupWizardGamePathChecking = "";
    [ObservableProperty] private string setupWizardGamePathCorrupted = "";
    [ObservableProperty] private string setupWizardGamePathHint = "";
    [ObservableProperty] private string setupWizardGamePathInaccessible = "";
    [ObservableProperty] private string setupWizardGamePathInstalled = "";
    [ObservableProperty] private string setupWizardGamePathEmpty = "";
    [ObservableProperty] private string setupWizardBrowse = "";
    [ObservableProperty] private string setupWizardProxy = "";
    [ObservableProperty] private string setupWizardProxyAutoDescription = "";
    [ObservableProperty] private string setupWizardProxyDirectDescription = "";
    [ObservableProperty] private string setupWizardProxyHint = "";
    [ObservableProperty] private string setupWizardProxySystemDescription = "";
    [ObservableProperty] private string setupWizardReview = "";
    [ObservableProperty] private string setupWizardReviewHint = "";
    [ObservableProperty] private string setupWizardStep0Title = "";
    [ObservableProperty] private string setupWizardStep1Title = "";
    [ObservableProperty] private string setupWizardStep2Title = "";
    [ObservableProperty] private string setupWizardStep3Title = "";
    [ObservableProperty] private string setupWizardStep4Title = "";
    [ObservableProperty] private string setupWizardNext = "";
    [ObservableProperty] private string setupWizardPrevious = "";
    [ObservableProperty] private string setupWizardFinish = "";
    [ObservableProperty] private string setupWizardSkip = "";
    [ObservableProperty] private string setupWizardExitTitle = "";
    [ObservableProperty] private string setupWizardExitMessage = "";
    [ObservableProperty] private string setupWizardExitConfirm = "";
    [ObservableProperty] private string setupWizardStepTitle = "";

    public void Apply(LocalizationService localizer)
    {
        Settings = localizer.T("settings");
        SettingsGroupGameFiles = localizer.T("settingsGroupGameFiles");
        SettingsGroupAppPreferences = localizer.T("settingsGroupAppPreferences");
        SettingsGroupAboutActions = localizer.T("settingsGroupAboutActions");
        SettingsGroupBackground = localizer.T("settingsGroupBackground");
        SettingsGroupThemeColor = localizer.T("settingsGroupThemeColor");
        SettingsCategoryGeneral = localizer.T("settingsCategoryGeneral");
        SettingsCategoryGame = localizer.T("settingsCategoryGame");
        SettingsCategoryDownloadNetwork = localizer.T("settingsCategoryDownloadNetwork");
        SettingsCategoryAppearance = localizer.T("settingsCategoryAppearance");
        SettingsCategoryAdvanced = localizer.T("settingsCategoryAdvanced");
        SettingsCategoryAbout = localizer.T("settingsCategoryAbout");
        Minimize = localizer.T("minimize");
        Close = localizer.T("close");
        Version = localizer.T("version");
        Executable = localizer.T("executable");
        Network = localizer.T("network");
        DiskSpaceCheck = localizer.T("diskSpaceCheck");
        DiskSpaceInsufficientDetail = localizer.T("diskSpaceInsufficientDetail");
        DiskSpaceOkSuffix = localizer.T("diskSpaceOkSuffix");
        DiskSpaceShortSuffix = localizer.T("diskSpaceShortSuffix");
        VerificationRetry = localizer.T("verificationRetry");
        VerificationFailed = localizer.T("verificationFailed");
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
        ResourcePanelUidInvalidFormat = localizer.T("resourcePanelUidInvalidFormat");
        ResourcePanelCurrentUid = localizer.T("resourcePanelCurrentUid");
        ResourcePanelChangeUid = localizer.T("resourcePanelChangeUid");
        ResourcePanelUidSource = localizer.T("resourcePanelUidSource");
        ResourcePanelUidSourceAuto = localizer.T("resourcePanelUidSourceAuto");
        ResourcePanelUidSourceCustom = localizer.T("resourcePanelUidSourceCustom");
        ResourcePanelEditUid = localizer.T("resourcePanelEditUid");
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
        MotionMode = localizer.T("motionMode");
        MotionModeDescription = localizer.T("motionModeDescription");
        MotionModeSystem = localizer.T("motionModeSystem");
        MotionModeFull = localizer.T("motionModeFull");
        MotionModeReduced = localizer.T("motionModeReduced");
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
        AboutActionsGeneral = localizer.T("aboutActionsGeneral");
        AboutDescription = localizer.T("aboutDescription");
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
        GitHubRepository = localizer.T("gitHubRepository");
        HelpDocs = localizer.T("helpDocs");
        CheckUpdates = localizer.T("checkUpdates");
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
        BackgroundSource = localizer.T("backgroundSource");
        BackgroundSourceDescription = localizer.T("backgroundSourceDescription");
        BackgroundSourceBundled = localizer.T("backgroundSourceBundled");
        BackgroundSourceRemote = localizer.T("backgroundSourceRemote");
        BackgroundSourceCustom = localizer.T("backgroundSourceCustom");
        BackgroundFit = localizer.T("backgroundFit");
        BackgroundFitDescription = localizer.T("backgroundFitDescription");
        BackgroundFitFill = localizer.T("backgroundFitFill");
        BackgroundFitUniform = localizer.T("backgroundFitUniform");
        BackgroundFitUniformToFill = localizer.T("backgroundFitUniformToFill");
        BackgroundFillColor = localizer.T("backgroundFillColor");
        BackgroundFillColorDescription = localizer.T("backgroundFillColorDescription");
        VersionInfo = localizer.T("versionInfo");
        BuildInfo = localizer.T("buildInfo");
        DebugPanel = localizer.T("debugPanel");
        DebugActionFailureMessage = localizer.T("debugActionFailureMessage");
        DebugActionFailureTitle = localizer.T("debugActionFailureTitle");
        DebugActionToastMessage = localizer.T("debugActionToastMessage");
        DebugActionToastTitle = localizer.T("debugActionToastTitle");
        DebugCriticalErrorMessage = localizer.T("debugCriticalErrorMessage");
        DebugCriticalErrorTriggered = localizer.T("debugCriticalErrorTriggered");
        DebugExportCancelled = localizer.T("debugExportCancelled");
        DebugGameOperations = localizer.T("debugGameOperations");
        DebugHandledErrorMessage = localizer.T("debugHandledErrorMessage");
        DebugHandledErrorSimulated = localizer.T("debugHandledErrorSimulated");
        DebugIdle = localizer.T("debugIdle");
        DebugLogEntryWritten = localizer.T("debugLogEntryWritten");
        DebugLogExportUnavailable = localizer.T("debugLogExportUnavailable");
        DebugLogLevelSet = localizer.T("debugLogLevelSet");
        DebugOutput = localizer.T("debugOutput");
        DebugPauseResume = localizer.T("debugPauseResume");
        DebugResetSettingsTitle = localizer.T("debugResetSettingsTitle");
        DebugResetSettingsDescription = localizer.T("debugResetSettingsDescription");
        DebugResetSettingsConfirm = localizer.T("debugResetSettingsConfirm");
        DebugSettingsReadFailed = localizer.T("debugSettingsReadFailed");
        DebugSettingsReset = localizer.T("debugSettingsReset");
        DebugSimulateError = localizer.T("debugSimulateError");
        DebugSimulateFailure = localizer.T("debugSimulateFailure");
        DebugSimulateSuccess = localizer.T("debugSimulateSuccess");
        DebugStateRefreshTriggered = localizer.T("debugStateRefreshTriggered");
        DebugStatus = localizer.T("debugStatus");
        DebugSystemInfo = localizer.T("debugSystemInfo");
        DebugSystemInfoFormat = localizer.T("debugSystemInfoFormat");
        DebugTestActionToast = localizer.T("debugTestActionToast");
        DebugTestErrorToast = localizer.T("debugTestErrorToast");
        DebugTestInfoToast = localizer.T("debugTestInfoToast");
        DebugTestLogMessage = localizer.T("debugTestLogMessage");
        DebugTestSuccessToast = localizer.T("debugTestSuccessToast");
        DebugTestToastMessage = localizer.T("debugTestToastMessage");
        DebugTestWarningToast = localizer.T("debugTestWarningToast");
        DebugToastShown = localizer.T("debugToastShown");
        DownloadSpeedLimit = localizer.T("downloadSpeedLimit");
        DownloadSpeedLimitDescription = localizer.T("downloadSpeedLimitDescription");
        NotificationSettings = localizer.T("notificationSettings");
        RemoteContentCard = localizer.T("remoteContentCard");
        RemoteContentLoading = localizer.T("remoteContentLoading");
        ShowRemoteContentCard = localizer.T("showRemoteContentCard");
        ToggleOn = localizer.T("toggleOn");
        ToggleOff = localizer.T("toggleOff");
        ToastSuccess = localizer.T("toastSuccess");
        ToastWarning = localizer.T("toastWarning");
        ToastError = localizer.T("toastError");
        ToastActionFailedMessage = localizer.T("toastActionFailedMessage");
        ToastActionFailedTitle = localizer.T("toastActionFailedTitle");
        ToastInfo = localizer.T("toastInfo");
        LaunchCheckDescription = localizer.T("launchCheckDescription");
        ProxyDescription = localizer.T("proxyDescription");
        ProxyAuto = localizer.T("proxyAuto");
        ProxyDirect = localizer.T("proxyDirect");
        ProxySystem = localizer.T("proxySystem");
        DownloadSource = localizer.T("downloadSource");
        DownloadSourceDescription = localizer.T("downloadSourceDescription");
        DownloadSourceOfficial = localizer.T("downloadSourceOfficial");
        DownloadSourceCafe = localizer.T("downloadSourceCafe");
        DownloadSourceChangedRepairPrompt = localizer.T("downloadSourceChangedRepairPrompt");
        ResourcePanelCafeOnlyTitle = localizer.T("resourcePanelCafeOnlyTitle");
        ResourcePanelCafeOnlyDescription = localizer.T("resourcePanelCafeOnlyDescription");
        ResourcePanelCafeOnlyMessage = localizer.T("resourcePanelCafeOnlyMessage");
        ResourcePanelCafeOnlySwitch = localizer.T("resourcePanelCafeOnlySwitch");
        Pause = localizer.T("pause");
        Resume = localizer.T("resume");
        Paused = localizer.T("paused");
        Downloading = localizer.T("downloading");
        PauseRequested = localizer.T("pauseRequested");
        ResumeRequested = localizer.T("resumeRequested");
        StopRequested = localizer.T("stopRequested");
        StopDownloadTitle = localizer.T("stopDownloadTitle");
        StopDownloadMessage = localizer.T("stopDownloadMessage");
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
        PreviousBanner = localizer.T("previousBanner");
        NextBanner = localizer.T("nextBanner");
        BannerLoadingFailed = localizer.T("bannerLoadingFailed");
        BannerLoading = localizer.T("bannerLoading");
        StatusNetworkLoaded = localizer.T("statusNetworkLoaded");
        StatusDetailMode = localizer.T("statusDetailMode");
        StatusDetailModeDescription = localizer.T("statusDetailModeDescription");
        StatusDetailModeHidden = localizer.T("statusDetailModeHidden");
        StatusDetailModeCompact = localizer.T("statusDetailModeCompact");
        StatusDetailModeDetailed = localizer.T("statusDetailModeDetailed");
        StatusLaunchCheckLocal = localizer.T("statusLaunchCheckLocal");
        StatusLaunchCheckRemote = localizer.T("statusLaunchCheckRemote");
        StatusLaunchCheckNone = localizer.T("statusLaunchCheckNone");
        GameLaunchedMinimized = localizer.T("gameLaunchedMinimized");
        LauncherUpdateChannel = localizer.T("launcherUpdateChannel");
        LauncherUpdateChannelStable = localizer.T("launcherUpdateChannelStable");
        LauncherUpdateChannelBeta = localizer.T("launcherUpdateChannelBeta");
        LauncherUpdateAvailableTitle = localizer.T("launcherUpdateAvailableTitle");
        LauncherUpdateAvailableMessage = localizer.T("launcherUpdateAvailableMessage");
        LauncherUpdateSelectFile = localizer.T("launcherUpdateSelectFile");
        LauncherUpdateFileSize = localizer.T("launcherUpdateFileSize");
        LauncherUpdateDownload = localizer.T("launcherUpdateDownload");
        LauncherUpdateLater = localizer.T("launcherUpdateLater");
        CrashRecoveryTitle = localizer.T("crashRecoveryTitle");
        CrashRecoveryDescription = localizer.T("crashRecoveryDescription");
        CrashRecoveryMessage = localizer.T("crashRecoveryMessage");
        CrashRecoveryContinue = localizer.T("crashRecoveryContinue");
        CrashRecoveryResetSettings = localizer.T("crashRecoveryResetSettings");
        CrashRecoveryViewLog = localizer.T("crashRecoveryViewLog");
        ErrorDialogTitle = localizer.T("errorDialogTitle");
        ErrorDialogDescription = localizer.T("errorDialogDescription");
        ErrorDialogContinue = localizer.T("errorDialogContinue");
        ErrorDialogViewLog = localizer.T("errorDialogViewLog");
        ErrorDialogCopyDetails = localizer.T("errorDialogCopyDetails");
        BuildTimeLabel = localizer.T("buildTimeLabel");
        LogViewerTitle = localizer.T("logViewerTitle");
        LogFilterAll = localizer.T("logFilterAll");
        LogFilterVerbose = localizer.T("logFilterVerbose");
        LogFilterDebug = localizer.T("logFilterDebug");
        LogFilterInfo = localizer.T("logFilterInfo");
        LogFilterWarn = localizer.T("logFilterWarn");
        LogFilterError = localizer.T("logFilterError");
        LogFilterFatal = localizer.T("logFilterFatal");
        LogSearchPlaceholder = localizer.T("logSearchPlaceholder");
        LogLoadEarlier = localizer.T("logLoadEarlier");
        LogNoMatchingEntries = localizer.T("logNoMatchingEntries");
        ExportLogs = localizer.T("exportLogs");
        ViewLog = localizer.T("viewLog");
        InstallUpdateFailedTitle = localizer.T("installUpdateFailedTitle");
        Retry = localizer.T("retry");
        OpenDataDirectory = localizer.T("openDataDirectory");
        LogFiles = localizer.T("logFiles");
        LogFilesDescription = localizer.T("logFilesDescription");
        LogExportFolderPickerTitle = localizer.T("logExportFolderPickerTitle");
        LogExportSucceeded = localizer.T("logExportSucceeded");
        LogExportFailed = localizer.T("logExportFailed");
        LogLevel = localizer.T("logLevel");
        LogLevelVerbose = localizer.T("logLevelVerbose");
        LogLevelDebug = localizer.T("logLevelDebug");
        LogLevelInformation = localizer.T("logLevelInformation");
        LogLevelWarning = localizer.T("logLevelWarning");
        LogLevelError = localizer.T("logLevelError");
        LogLevelFatal = localizer.T("logLevelFatal");
        SelectInstalledGame = localizer.T("selectInstalledGame");
        GamePathUpdated = localizer.T("gamePathUpdated");
        GamePathUpdateFailed = localizer.T("gamePathUpdateFailed");
        SettingsGroupConnection = localizer.T("settingsGroupConnection");
        SettingsGroupDisplay = localizer.T("settingsGroupDisplay");
        SettingsGroupDiagnostics = localizer.T("settingsGroupDiagnostics");
        LanguageDescription = localizer.T("languageDescription");
        CloseBehaviorDescription = localizer.T("closeBehaviorDescription");
        LauncherUpdateChannelDescription = localizer.T("launcherUpdateChannelDescription");
        LogLevelDescription = localizer.T("logLevelDescription");
        ToastNotificationsDescription = localizer.T("toastNotificationsDescription");
        EnableStartupUpdateCheck = localizer.T("enableStartupUpdateCheck");
        EnableStartupUpdateCheckDescription = localizer.T("enableStartupUpdateCheckDescription");
        StartupUpdateAvailable = localizer.T("startupUpdateAvailable");
        RemoteContentCardDescription = localizer.T("remoteContentCardDescription");
        SetupWizardWelcomeTitle = localizer.T("setupWizardWelcomeTitle");
        SetupWizardWelcomeText = localizer.T("setupWizardWelcomeText");
        SetupWizardLanguage = localizer.T("setupWizardLanguage");
        SetupWizardLanguageHint = localizer.T("setupWizardLanguageHint");
        SetupWizardDownloadSource = localizer.T("setupWizardDownloadSource");
        SetupWizardDownloadSourceCafeDescription = localizer.T("setupWizardDownloadSourceCafeDescription");
        SetupWizardDownloadSourceHint = localizer.T("setupWizardDownloadSourceHint");
        SetupWizardDownloadSourceOfficialDescription = localizer.T("setupWizardDownloadSourceOfficialDescription");
        SetupWizardEditStep = localizer.T("setupWizardEditStep");
        SetupWizardGamePath = localizer.T("setupWizardGamePath");
        SetupWizardGamePathAvailable = localizer.T("setupWizardGamePathAvailable");
        SetupWizardGamePathChecking = localizer.T("setupWizardGamePathChecking");
        SetupWizardGamePathCorrupted = localizer.T("setupWizardGamePathCorrupted");
        SetupWizardGamePathHint = localizer.T("setupWizardGamePathHint");
        SetupWizardGamePathInaccessible = localizer.T("setupWizardGamePathInaccessible");
        SetupWizardGamePathInstalled = localizer.T("setupWizardGamePathInstalled");
        SetupWizardGamePathEmpty = localizer.T("setupWizardGamePathEmpty");
        SetupWizardBrowse = localizer.T("setupWizardBrowse");
        SetupWizardProxy = localizer.T("setupWizardProxy");
        SetupWizardProxyAutoDescription = localizer.T("setupWizardProxyAutoDescription");
        SetupWizardProxyDirectDescription = localizer.T("setupWizardProxyDirectDescription");
        SetupWizardProxyHint = localizer.T("setupWizardProxyHint");
        SetupWizardProxySystemDescription = localizer.T("setupWizardProxySystemDescription");
        SetupWizardReview = localizer.T("setupWizardReview");
        SetupWizardReviewHint = localizer.T("setupWizardReviewHint");
        SetupWizardStep0Title = localizer.T("setupWizardStep0Title");
        SetupWizardStep1Title = localizer.T("setupWizardStep1Title");
        SetupWizardStep2Title = localizer.T("setupWizardStep2Title");
        SetupWizardStep3Title = localizer.T("setupWizardStep3Title");
        SetupWizardStep4Title = localizer.T("setupWizardStep4Title");
        SetupWizardNext = localizer.T("setupWizardNext");
        SetupWizardPrevious = localizer.T("setupWizardPrevious");
        SetupWizardFinish = localizer.T("setupWizardFinish");
        SetupWizardSkip = localizer.T("setupWizardSkip");
        SetupWizardExitTitle = localizer.T("setupWizardExitTitle");
        SetupWizardExitMessage = localizer.T("setupWizardExitMessage");
        SetupWizardExitConfirm = localizer.T("setupWizardExitConfirm");
        SetupWizardStepTitle = localizer.T("setupWizardStepTitle");
    }
}

public sealed class LocalizationService
{
    private static Dictionary<string, Dictionary<string, string>> Resources = new(StringComparer.Ordinal);
    private static readonly string[] SupportedLocales = [LauncherLanguages.English, LauncherLanguages.SimplifiedChinese, LauncherLanguages.TraditionalChinese, LauncherLanguages.Japanese];
    private static volatile bool resourcesLoaded;
    private static readonly object LoadLock = new();

    /// <summary>
    /// Pre-populates resources for unit testing without AssetLoader.
    /// Call once before creating LocalizationService instances in tests.
    /// </summary>
    internal static void InitializeForTesting(Dictionary<string, Dictionary<string, string>> resources)
    {
        // Build a complete replacement outside the lock so that concurrent T()
        // calls never observe a partially-cleared dictionary.  Swapping the static
        // reference is atomic (.NET object references are always atomic) and the
        // inner per-locale dictionaries are never mutated after creation.
        var newResources = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (locale, dict) in resources)
        {
            newResources[locale] = new Dictionary<string, string>(dict, StringComparer.Ordinal);
        }

        lock (LoadLock)
        {
            Resources = newResources;
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

    public static IReadOnlyList<LanguageOption> GetLanguageOptions() =>
        GetLanguageOptions(new LocalizationService());

    public static IReadOnlyList<LanguageOption> GetLanguageOptions(LocalizationService localizer)
    {
        return
        [
            new LanguageOption { Code = LauncherLanguages.Auto, DisplayName = localizer.T("languageAuto") },
            new LanguageOption { Code = LauncherLanguages.English, DisplayName = "English" },
            new LanguageOption { Code = LauncherLanguages.SimplifiedChinese, DisplayName = "简体中文" },
            new LanguageOption { Code = LauncherLanguages.TraditionalChinese, DisplayName = "繁體中文" },
            new LanguageOption { Code = LauncherLanguages.Japanese, DisplayName = "日本語" }
        ];
    }

    public static string ResolveLanguage(string? language)
    {
        return language switch
        {
            LauncherLanguages.English => LauncherLanguages.English,
            LauncherLanguages.SimplifiedChinese => LauncherLanguages.SimplifiedChinese,
            LauncherLanguages.TraditionalChinese => LauncherLanguages.TraditionalChinese,
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
            // zh-TW, zh-HK, zh-MO → Traditional; zh-CN, zh-SG, zh-Hans → Simplified.
            // Fall back to Simplified when the region/script is ambiguous.
            return IsTraditionalChineseRegion(name)
                ? LauncherLanguages.TraditionalChinese
                : LauncherLanguages.SimplifiedChinese;
        }

        if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return LauncherLanguages.Japanese;
        }

        return LauncherLanguages.English;
    }

    private static bool IsTraditionalChineseRegion(string cultureName)
    {
        // Match by script subtag (zh-Hant, zh-Hans) first, then by region.
        if (cultureName.Contains("Hant", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (cultureName.Contains("Hans", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Region fallback: TW (Taiwan), HK (Hong Kong), MO (Macau) use Traditional Chinese.
        return cultureName.EndsWith("TW", StringComparison.OrdinalIgnoreCase)
            || cultureName.EndsWith("HK", StringComparison.OrdinalIgnoreCase)
            || cultureName.EndsWith("MO", StringComparison.OrdinalIgnoreCase);
    }
}
