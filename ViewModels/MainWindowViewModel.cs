using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Security.Cryptography;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILauncherCoreService launcherCoreService;
    private readonly LauncherSettingsService settingsService;
    private readonly LocalGameStateService localGameStateService;
    private readonly GameLaunchService gameLaunchService;
    private readonly GameDownloadService gameDownloadService;
    private readonly GameUninstallService gameUninstallService;
    private readonly ExternalLinkService externalLinkService;
    private readonly DiskSpaceService diskSpaceService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly LocalDiagnostics diagnostics;
    private readonly NoticeStateService noticeStateService;
    private readonly ImageCacheService imageCacheService;
    private readonly ResourcePanelUidService resourcePanelUidService;
    private readonly ResourcePanelApiClient resourcePanelApiClient;
    private readonly CancellationTokenSource lifetimeCts = new();
    private int initialized;
    private bool disposed;
    private bool skipNextPersistedResume;
    private bool suppressSettingsDirty;
    private LauncherStatusSnapshot? currentSnapshot;

    private static readonly string FrameworkVersion = RuntimeInformation.FrameworkDescription;
    private static readonly string PlatformName = OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsLinux() ? "Linux"
        : OperatingSystem.IsMacOS() ? "macOS"
        : "Unknown";

    /// <summary>
    /// Active toast notifications displayed in the toast overlay.
    /// </summary>
    public ObservableCollection<ToastNotification> ActiveToasts { get; } = [];

    [ObservableProperty]
    private string productName = LauncherConstants.ProductName;

    [ObservableProperty]
    private string launcherVersionText = LauncherConstants.LauncherVersion;

    [ObservableProperty]
    private string runtimeInfoText = "";

    [ObservableProperty]
    private string buildInfoText = "";

    [ObservableProperty]
    private IImage? backgroundImageSource;

    [ObservableProperty]
    private string customBackgroundPath = "";

    [ObservableProperty]
    private bool isCustomBackground;

    [ObservableProperty]
    private string selectedBackgroundSource = BackgroundSources.Bundled;

    [ObservableProperty]
    private bool isCustomBackgroundSelected;

    [ObservableProperty]
    private string selectedThemeColorMode = ThemeColorModes.Default;

    [ObservableProperty]
    private Color selectedCustomThemeColor = Color.Parse(LauncherConstants.DefaultThemeColor);

    [ObservableProperty]
    private IBrush themeColorPreviewBrush = new SolidColorBrush(Color.Parse(LauncherConstants.DefaultThemeColor));

    [ObservableProperty]
    private bool isCustomThemeColorSelected;

    [ObservableProperty]
    private bool isWallpaperThemeColorSelected;

    [ObservableProperty]
    private int selectedThemeColorPaletteIndex;

    public ObservableCollection<ThemeColorPaletteItem> ThemeColorPaletteItems { get; } = [];

    public ObservableCollection<SettingOption> BackgroundSourceOptions { get; } =
    [
        new SettingOption { Code = Models.BackgroundSources.Bundled },
        new SettingOption { Code = Models.BackgroundSources.Remote },
        new SettingOption { Code = Models.BackgroundSources.Custom }
    ];

    public ObservableCollection<SettingOption> ThemeColorOptions { get; } =
    [
        new SettingOption { Code = ThemeColorModes.Default },
        new SettingOption { Code = ThemeColorModes.System },
        new SettingOption { Code = ThemeColorModes.Wallpaper },
        new SettingOption { Code = ThemeColorModes.Custom }
    ];

    [ObservableProperty]
    private string currentViewTitle = "Loading launcher configuration";

    [ObservableProperty]
    private string statusText = "Loading production API and local game state.";

    [ObservableProperty]
    private string pathText = "Game path: loading";

    [ObservableProperty]
    private string versionText = "Version: loading";

    [ObservableProperty]
    private string networkText = "Network: loading";

    [ObservableProperty]
    private string launchCheckText = "Launch check: loading";

    [ObservableProperty]
    private string executableText = "Executable: loading";

    [ObservableProperty]
    private string diskSpaceText = "Required -- / Available --";

    [ObservableProperty]
    private string settingsSummary = "Settings";

    [ObservableProperty]
    private string operationNote = "Remote telemetry is excluded. Diagnostics stay local.";

    [ObservableProperty]
    private string noticeText = "";

    [ObservableProperty]
    private bool hasNotice;

    [ObservableProperty]
    private bool hasBannerItems;

    [ObservableProperty]
    private bool hasNewsItems;

    [ObservableProperty]
    private bool hasSocialMediaItems;

    [ObservableProperty]
    private bool hasRemoteContent;

    [ObservableProperty]
    private bool isBusy = true;

    [ObservableProperty]
    private bool isSettingsVisible;

    // I1: Dirty tracking for unsaved settings changes
    [ObservableProperty]
    private bool isSettingsDirty;

    // M4: Unsaved changes confirmation dialog
    [ObservableProperty]
    private bool isUnsavedChangesVisible;

    // I4: Stop download confirmation dialog
    [ObservableProperty]
    private bool isStopConfirmVisible;

    [ObservableProperty]
    private string stopConfirmText = "";

    [ObservableProperty]
    private bool isInstallPanelVisible = true;

    [ObservableProperty]
    private bool isControlPanelVisible;

    [ObservableProperty]
    private bool isProgressPanelVisible;

    [ObservableProperty]
    private bool isUninstallConfirmVisible;

    [ObservableProperty]
    private string uninstallConfirmText = "";

    // Phase 1.3: Repair confirmation dialog
    [ObservableProperty]
    private bool isRepairConfirmVisible;

    [ObservableProperty]
    private string repairConfirmText = "";

    // Phase 2.3: Notice popup dialog
    [ObservableProperty]
    private bool isNoticeDialogVisible;

    [ObservableProperty]
    private string noticeDialogContent = "";

    [ObservableProperty]
    private string noticeDialogConfirmText = "";

    [ObservableProperty]
    private bool isResourcePanelVisible;

    [ObservableProperty]
    private bool isResourcePanelBusy;

    [ObservableProperty]
    private bool isResourcePanelUidMissing;

    [ObservableProperty]
    private string resourcePanelUid = "";

    [ObservableProperty]
    private string resourcePanelUidText = "";

    [ObservableProperty]
    private string manualResourcePanelUid = "";

    [ObservableProperty]
    private string resourcePanelMessage = "";

    // Carousel / Banner rotation
    [ObservableProperty]
    private int carouselSelectedIndex;

    [ObservableProperty]
    private bool bannerIsLooping = true;

    // A3: Carousel pause state
    [ObservableProperty]
    private bool isCarouselPaused;

    [ObservableProperty]
    private string carouselPauseIcon = "Pause";

    [ObservableProperty]
    private string carouselPauseTooltip = "";

    // L5: Carousel page text and multi-banner indicator
    [ObservableProperty]
    private string carouselPageText = "";

    [ObservableProperty]
    private bool hasMultipleBanners;

    [ObservableProperty]
    private int bannerIntervalMs = 5000;

    private DispatcherTimer? carouselTimer;
    // I6: Countdown after manual navigation before resuming auto-advance
    private const int ManualNavResumeDelayMs = 5000;
    private CancellationTokenSource? carouselDelayCts;

    /// <summary>
    /// Dot indicators for the banner carousel. Each dot tracks its own active state.
    /// </summary>
    public ObservableCollection<BannerDot> BannerDots { get; } = [];

    [ObservableProperty]
    private string selectedLaunchCheckMode = Cafe.Launcher.Avalonia.Models.LaunchCheckModes.LocalManifest;

    [ObservableProperty]
    private string selectedProxyMode = Cafe.Launcher.Avalonia.Models.ProxyModes.Direct;

    [ObservableProperty]
    private string selectedPatchUrlGroup = Cafe.Launcher.Avalonia.Models.PatchUrlGroups.Official;

    [ObservableProperty]
    private string selectedCloseBehavior = Cafe.Launcher.Avalonia.Models.CloseBehaviors.Minimize;

    [ObservableProperty]
    private string selectedLanguage = Cafe.Launcher.Avalonia.Models.LauncherLanguages.Auto;

    [ObservableProperty]
    private string selectedThemeMode = Cafe.Launcher.Avalonia.Models.ThemeModes.System;

    [ObservableProperty]
    private string selectedDownloadSpeedLimit = Cafe.Launcher.Avalonia.Models.DownloadSpeedLimits.Unlimited;

    [ObservableProperty]
    private bool toastNotificationsEnabled = true;

    [ObservableProperty]
    private bool showRemoteContentCard = true;

    [ObservableProperty]
    private string selectedGamePath = "";

    [ObservableProperty]
    private string installButtonText = "Install Game";

    [ObservableProperty]
    private string progressTitle = "Preparing";

    [ObservableProperty]
    private int progressValue;

    [ObservableProperty]
    private string progressDetail = "";

    [ObservableProperty]
    private string progressSpeed = "";

    [ObservableProperty]
    private string progressSize = "";

    [ObservableProperty]
    private string progressEstimated = "";

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private bool canPauseOperation;

    [ObservableProperty]
    private string pauseResumeText = "";

    [ObservableProperty]
    private string pauseResumeIcon = "Pause";

    public Func<string, Task<string?>>? PickGameFolderAsync { get; set; }
    public Func<Task<string?>>? PickBackgroundImageAsync { get; set; }

    public Func<Task<string?>>? PickBackgroundFolderAsync { get; set; }

    public Action? MinimizeWindow { get; set; }

    public Action? CloseWindow { get; set; }

    public Action? RestoreWindow { get; set; }

    // I5: Localized native dialog titles
    public string GameFolderPickerTitle { get; private set; } = "Choose install folder";
    public string BackgroundImagePickerTitle { get; private set; } = "Choose Background Image";

    public string BackgroundFolderPickerTitle { get; private set; } = "Choose Background Folder";

    public ObservableCollection<RemoteContentItem> BannerItems { get; } = [];

    public ObservableCollection<RemoteContentItem> NewsItems { get; } = [];

    public ObservableCollection<NewsCategory> NewsCategories { get; } = [];

    [ObservableProperty]
    private NewsCategory? selectedNewsCategory;

    public ObservableCollection<RemoteContentItem> SocialMediaItems { get; } = [];

    public ObservableCollection<ResourcePanelItem> ResourcePanelItems { get; } =
    [
        new ResourcePanelItem(ResourcePanelResourceCodes.Text),
        new ResourcePanelItem(ResourcePanelResourceCodes.Voice),
        new ResourcePanelItem(ResourcePanelResourceCodes.Media)
    ];

    public LocalizedStrings I18n { get; } = new();

    public MainWindowViewModel(
        ILauncherCoreService launcherCoreService,
        LauncherSettingsService settingsService,
        LocalGameStateService localGameStateService,
        GameLaunchService gameLaunchService,
        GameDownloadService gameDownloadService,
        GameUninstallService gameUninstallService,
        ExternalLinkService externalLinkService,
        DiskSpaceService diskSpaceService,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics,
        NoticeStateService noticeStateService,
        ImageCacheService imageCacheService,
        ResourcePanelUidService resourcePanelUidService,
        ResourcePanelApiClient resourcePanelApiClient)
    {
        this.launcherCoreService = launcherCoreService;
        this.settingsService = settingsService;
        this.localGameStateService = localGameStateService;
        this.gameLaunchService = gameLaunchService;
        this.gameDownloadService = gameDownloadService;
        this.gameUninstallService = gameUninstallService;
        this.externalLinkService = externalLinkService;
        this.diskSpaceService = diskSpaceService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.diagnostics = diagnostics;
        this.noticeStateService = noticeStateService;
        this.imageCacheService = imageCacheService;
        this.resourcePanelUidService = resourcePanelUidService;
        this.resourcePanelApiClient = resourcePanelApiClient;
        toastService.ToastRaised += OnToastRaised;
        ApplyLanguage(LauncherLanguages.Auto);
        backgroundImageSource = LoadBundledBackground();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref initialized, 1) == 1)
        {
            return;
        }

        await RefreshAsync(cancellationToken);
    }

    public ObservableCollection<SettingOption> LaunchCheckModeOptions { get; } =
    [
        new SettingOption { Code = Models.LaunchCheckModes.LocalManifest },
        new SettingOption { Code = Models.LaunchCheckModes.RemoteManifest },
        new SettingOption { Code = Models.LaunchCheckModes.None }
    ];

    public ObservableCollection<SettingOption> ProxyModeOptions { get; } =
    [
        new SettingOption { Code = Models.ProxyModes.Direct },
        new SettingOption { Code = Models.ProxyModes.System }
    ];

    public ObservableCollection<SettingOption> PatchUrlGroupOptions { get; } =
    [
        new SettingOption { Code = Models.PatchUrlGroups.Official },
        new SettingOption { Code = Models.PatchUrlGroups.Cafe }
    ];

    public ObservableCollection<SettingOption> DownloadSpeedLimitOptions { get; } =
    [
        new SettingOption { Code = DownloadSpeedLimits.Unlimited },
        new SettingOption { Code = DownloadSpeedLimits._1MBs },
        new SettingOption { Code = DownloadSpeedLimits._5MBs },
        new SettingOption { Code = DownloadSpeedLimits._10MBs },
        new SettingOption { Code = DownloadSpeedLimits._25MBs },
        new SettingOption { Code = DownloadSpeedLimits._50MBs }
    ];

    public ObservableCollection<SettingOption> CloseBehaviorOptions { get; } =
    [
        new SettingOption { Code = Models.CloseBehaviors.Minimize },
        new SettingOption { Code = Models.CloseBehaviors.Exit }
    ];

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } = LocalizationService.GetLanguageOptions();

    public ObservableCollection<ThemeOption> ThemeOptions { get; } =
    [
        new ThemeOption { Code = ThemeModes.System },
        new ThemeOption { Code = ThemeModes.Light },
        new ThemeOption { Code = ThemeModes.Dark }
    ];

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var settingsForLanguage = await settingsService.ReadAsync(cancellationToken);
            ApplyLanguage(settingsForLanguage.Language);
            SelectedThemeMode = settingsForLanguage.ThemeMode;
            LoadThemeColorState(settingsForLanguage);
            ApplyTheme(settingsForLanguage.ThemeMode);
            ApplyThemeColor(settingsForLanguage.ThemeColorMode, ParseColorOrDefault(settingsForLanguage.CustomThemeColor));
            CurrentViewTitle = localizer.T("loadingTitle");
            StatusText = localizer.T("connectingApi");
            OperationNote = localizer.T("loadingStatus");
            var snapshot = await launcherCoreService.LoadAsync(cancellationToken);
            currentSnapshot = snapshot;
            await ApplySnapshotAsync(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            CurrentViewTitle = localizer.T("networkUnavailableTitle");
            StatusText = localizer.T("networkError");
            NetworkText = localizer.F("networkWithMessage", exception.Message);
            VersionText = localizer.T("versionUnavailable");
            OperationNote = localizer.T("apiFailedNoFileChange");
            toastService.ShowError(localizer.F("networkWithMessage", exception.Message));
            PathText = localizer.T("pathLoading");
            ExecutableText = localizer.T("executableLoading");
            SetIdlePanels();
            await TryLogErrorAsync("Launcher core refresh failed.", exception);
        }
        finally
        {
            IsBusy = false;
        }

        if (skipNextPersistedResume)
        {
            skipNextPersistedResume = false;
            return;
        }

        await ResumePersistedDownloadAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ChooseGamePathAsync()
    {
        if (PickGameFolderAsync is null)
        {
            OperationNote = localizer.T("folderPickerUnavailable");
            return;
        }

        var pickedPath = await PickGameFolderAsync(SelectedGamePath);
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            OperationNote = localizer.T("pathCanceled");
            return;
        }

        var normalizedPath = localGameStateService.NormalizeGamePath(pickedPath);
        var settings = await settingsService.ReadAsync();
        settings.GamePath = normalizedPath;
        settings.LaunchCheckMode = SelectedLaunchCheckMode;
        settings.ProxyMode = SelectedProxyMode;
        settings.PatchUrlGroup = SelectedPatchUrlGroup;
        settings.CloseBehavior = SelectedCloseBehavior;
        settings.Language = SelectedLanguage;
        settings.ThemeMode = SelectedThemeMode;
        settings.ThemeColorMode = SelectedThemeColorMode;
        settings.CustomThemeColor = ToColorHex(SelectedCustomThemeColor);
        settings.ThemeColorPalette = GetThemeColorPaletteHexes();
        settings.SelectedThemeColorPaletteIndex = SelectedThemeColorPaletteIndex;
        await settingsService.SaveAsync(settings);
        ApplyLanguage(settings.Language);
        ApplyTheme(settings.ThemeMode);
        ApplyThemeColor(settings.ThemeColorMode, ParseColorOrDefault(settings.CustomThemeColor));
        OperationNote = localizer.F("pathSaved", normalizedPath);
        toastService.ShowSuccess(localizer.F("pathSaved", normalizedPath));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ChooseBackgroundImageAsync()
    {
        if (PickBackgroundImageAsync is null)
        {
            OperationNote = localizer.T("folderPickerUnavailable");
            return;
        }

        var pickedPath = await PickBackgroundImageAsync();
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            return;
        }

        CustomBackgroundPath = pickedPath;
        IsCustomBackground = true;
        SelectedBackgroundSource = BackgroundSources.Custom;
        IsCustomBackgroundSelected = true;
        await UpdateBackgroundImageAsync();
        await SaveSettingsAsync();
        OperationNote = localizer.T("backgroundSet");
        toastService.ShowSuccess(localizer.T("backgroundSet"));
    }

    [RelayCommand]
    private async Task ChooseBackgroundFolderAsync()
    {
        if (PickBackgroundFolderAsync is null)
        {
            OperationNote = localizer.T("folderPickerUnavailable");
            return;
        }

        var pickedPath = await PickBackgroundFolderAsync();
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            return;
        }

        CustomBackgroundPath = pickedPath;
        IsCustomBackground = true;
        SelectedBackgroundSource = BackgroundSources.Custom;
        IsCustomBackgroundSelected = true;
        await UpdateBackgroundImageAsync();
        await SaveSettingsAsync();
        OperationNote = localizer.T("backgroundSet");
        toastService.ShowSuccess(localizer.T("backgroundSet"));
    }

    [RelayCommand]
    private async Task ClearBackgroundAsync()
    {
        CustomBackgroundPath = "";
        IsCustomBackground = false;
        SelectedBackgroundSource = BackgroundSources.Bundled;
        IsCustomBackgroundSelected = false;
        await UpdateBackgroundImageAsync();
        await SaveSettingsAsync();
        OperationNote = localizer.T("backgroundCleared");
        toastService.ShowSuccess(localizer.T("backgroundCleared"));
    }

    private async Task UpdateBackgroundImageAsync()
    {
        switch (SelectedBackgroundSource)
        {
            case BackgroundSources.Remote:
                var snapshot = currentSnapshot;
                var bgImg = snapshot?.Remote.BaseConfig?.LauncherBackgroundImg;
                var crc64 = snapshot?.Remote.BaseConfig?.LauncherBackgroundImgCrc64;
                if (!string.IsNullOrWhiteSpace(bgImg) && !string.IsNullOrWhiteSpace(crc64))
                {
                    try
                    {
                        var proxyMode = snapshot?.Settings.ProxyMode ?? ProxyModes.Direct;
                        var cachedPath = await imageCacheService.GetCachedPathAsync(crc64)
                            ?? await imageCacheService.CacheImageAsync(bgImg, crc64, proxyMode, lifetimeCts.Token);
                        SetBackgroundImage(new Bitmap(cachedPath));
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Fall through to bundled if remote download fails
                        _ = diagnostics.MessageAsync(
                            "Remote background image download failed",
                            $"url: {bgImg}\ncrc64: {crc64}\nexception: {ex.Message}");
                    }
                }
                break;

            case BackgroundSources.Custom:
                if (!string.IsNullOrWhiteSpace(CustomBackgroundPath))
                {
                    var customBitmap = await LoadCustomBackgroundAsync(CustomBackgroundPath);
                    if (customBitmap is not null)
                    {
                        SetBackgroundImage(customBitmap);
                        return;
                    }
                }
                break;
        }

        SetBackgroundImage(LoadBundledBackground());
    }

    private async Task<Bitmap?> LoadCustomBackgroundAsync(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                return new Bitmap(path);
            }
            catch (Exception ex)
            {
                await diagnostics.MessageAsync(
                    "Custom background image load failed",
                    $"path: {path}\nexception: {ex.Message}");
                CustomBackgroundPath = "";
                IsCustomBackground = false;
                return null;
            }
        }

        if (Directory.Exists(path))
        {
            string? imagePath;
            try
            {
                imagePath = ResolveRandomBackgroundImage(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await diagnostics.MessageAsync(
                    "Custom background folder scan failed",
                    $"path: {path}\nexception: {ex.Message}");
                return null;
            }

            if (imagePath is null)
            {
                await diagnostics.MessageAsync(
                    "Custom background folder contains no supported images",
                    $"path: {path}");
                return null;
            }

            try
            {
                return new Bitmap(imagePath);
            }
            catch (Exception ex)
            {
                await diagnostics.MessageAsync(
                    "Custom background folder image load failed",
                    $"folder: {path}\npath: {imagePath}\nexception: {ex.Message}");
                return null;
            }
        }

        await diagnostics.MessageAsync(
            "Custom background path does not exist",
            $"path: {path}");
        return null;
    }

    internal static string? ResolveRandomBackgroundImage(string folderPath)
    {
        var imagePaths = Directory
            .EnumerateFiles(folderPath)
            .Where(IsSupportedBackgroundImage)
            .ToArray();

        return imagePaths.Length == 0
            ? null
            : imagePaths[Random.Shared.Next(imagePaths.Length)];
    }

    internal static bool IsSupportedBackgroundImage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private void SetBackgroundImage(Bitmap? bitmap)
    {
        var old = BackgroundImageSource as IDisposable;
        BackgroundImageSource = bitmap;
        if (SelectedThemeColorMode == ThemeColorModes.Wallpaper)
        {
            RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
            ApplyThemeColor(SelectedThemeColorMode, SelectedCustomThemeColor);
        }
        // Defer disposal to next frame to avoid disposing a bitmap the renderer may still be using
        if (old is not null)
            Dispatcher.UIThread.Post(() => old.Dispose(), DispatcherPriority.Background);
    }

    private static Bitmap? LoadBundledBackground()
    {
        try
        {
            var uri = new Uri("avares://Cafe.Launcher.Avalonia/Assets/bg-7b36e4e0.png");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            // Bundled background should always load — if this fails the install is corrupted
            return null;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (SelectedThemeColorMode == ThemeColorModes.Wallpaper && ThemeColorPaletteItems.Count == 0)
        {
            RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
        }

        var settings = await settingsService.ReadAsync();
        var previousPatchUrlGroup = settings.PatchUrlGroup;
        var shouldPromptRepairAfterSourceChange = currentSnapshot?.IsInstalled == true
            && !string.Equals(previousPatchUrlGroup, SelectedPatchUrlGroup, StringComparison.Ordinal);
        settings.GamePath = SelectedGamePath;
        settings.LaunchCheckMode = SelectedLaunchCheckMode;
        settings.ProxyMode = SelectedProxyMode;
        settings.PatchUrlGroup = SelectedPatchUrlGroup;
        settings.CloseBehavior = SelectedCloseBehavior;
        settings.Language = SelectedLanguage;
        settings.ThemeMode = SelectedThemeMode;
        settings.ThemeColorMode = SelectedThemeColorMode;
        settings.CustomThemeColor = ToColorHex(SelectedCustomThemeColor);
        settings.ThemeColorPalette = GetThemeColorPaletteHexes();
        settings.SelectedThemeColorPaletteIndex = SelectedThemeColorPaletteIndex;
        settings.DownloadSpeedLimit = SelectedDownloadSpeedLimit;
        settings.ToastNotificationsEnabled = ToastNotificationsEnabled;
        settings.ShowRemoteContentCard = ShowRemoteContentCard;
        settings.CustomBackgroundPath = CustomBackgroundPath;
        settings.BackgroundSource = SelectedBackgroundSource;
        await settingsService.SaveAsync(settings);
        ApplyLanguage(settings.Language);
        ApplyTheme(settings.ThemeMode);
        ApplyThemeColor(settings.ThemeColorMode, ParseColorOrDefault(settings.CustomThemeColor));
        IsSettingsDirty = false;
        OperationNote = localizer.T("settingsSaved");
        toastService.ShowSuccess(localizer.T("settingsSaved"));
        await RefreshAsync();
        if (shouldPromptRepairAfterSourceChange)
        {
            RepairConfirmText = localizer.T("downloadSourceChangedRepairPrompt");
            IsRepairConfirmVisible = true;
        }
    }

    [RelayCommand]
    private async Task StartGameAsync()
    {
        if (IsBusy)
        {
            OperationNote = localizer.T("busy");
            return;
        }

        if (currentSnapshot is null)
        {
            OperationNote = localizer.T("stateNotLoaded");
            return;
        }

        IsBusy = true;
        OperationNote = localizer.T("runningLaunchCheck");

        try
        {
            var launchResult = await gameLaunchService.StartAsync(currentSnapshot);
            LaunchCheckText = localizer.F("launchCheckWithMessage", launchResult.Validation.Message);
            OperationNote = launchResult.Message;

            if (launchResult.Success)
            {
                // I3: Show toast before minimizing so user knows what happened
                toastService.ShowSuccess(localizer.T("gameLaunchedMinimized"));
                // Brief delay so the toast is visible before minimize
                await Task.Delay(600);
                MinimizeWindow?.Invoke();
            }
            else
            {
                toastService.ShowWarning(launchResult.Message);
                await diagnostics.MessageAsync("Game launch blocked.", launchResult.Message);
            }
        }
        catch (Exception exception)
        {
            OperationNote = localizer.F("gameLaunchFailed", exception.Message);
            toastService.ShowError(exception.Message);
            await TryLogErrorAsync("Game launch failed.", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallOrUpdateAsync()
    {
        if (!PrepareOperation())
        {
            return;
        }

        try
        {
            var result = await gameDownloadService.InstallOrUpdateAsync(currentSnapshot!, ApplyProgress);
            OperationNote = result.Message;
            if (result.Success)
                toastService.ShowSuccess(result.Message);
            else
                toastService.ShowError(result.Message);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            OperationNote = localizer.F("networkWithMessage", exception.Message);
            toastService.ShowError(exception.Message);
            await TryLogErrorAsync("Game install/update failed.", exception);
        }
        finally
        {
            IsBusy = false;
            if (currentSnapshot is not null)
            {
                await ApplySnapshotAsync(currentSnapshot);
            }
        }
    }

    [RelayCommand]
    private async Task RequestRepairAsync()
    {
        if (currentSnapshot is null)
        {
            OperationNote = localizer.T("stateNotLoaded");
            return;
        }
        RepairConfirmText = localizer.T("repairWarning");
        IsRepairConfirmVisible = true;
    }

    [RelayCommand]
    private void CancelRepair()
    {
        IsRepairConfirmVisible = false;
    }

    [RelayCommand]
    private async Task ConfirmRepairAsync()
    {
        IsRepairConfirmVisible = false;
        await RepairAsync();
    }

    private async Task RepairAsync()
    {
        if (!PrepareOperation())
        {
            return;
        }

        try
        {
            var result = await gameDownloadService.RepairAsync(currentSnapshot!, ApplyProgress);
            OperationNote = result.Message;
            if (result.Success)
                toastService.ShowSuccess(result.Message);
            else
                toastService.ShowError(result.Message);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            OperationNote = localizer.F("networkWithMessage", exception.Message);
            toastService.ShowError(exception.Message);
            await TryLogErrorAsync("Game repair failed.", exception);
        }
        finally
        {
            IsBusy = false;
            if (currentSnapshot is not null)
            {
                await ApplySnapshotAsync(currentSnapshot);
            }
        }
    }

    [RelayCommand]
    private void StopOperation()
    {
        // I4: Show confirmation before stopping download
        if (gameDownloadService.IsRunning)
        {
            StopConfirmText = localizer.T("stopDownloadConfirm");
            IsStopConfirmVisible = true;
            return;
        }
        PerformStop();
    }

    [RelayCommand]
    private void ConfirmStop()
    {
        IsStopConfirmVisible = false;
        PerformStop();
    }

    [RelayCommand]
    private void CancelStop()
    {
        IsStopConfirmVisible = false;
    }

    private void PerformStop()
    {
        gameDownloadService.Stop();
        OperationNote = localizer.T("stopRequested");
        try { toastService.ShowWarning(localizer.T("stopRequested")); } catch { }
    }

    // A3: Toggle carousel auto-rotation
    [RelayCommand]
    private void ToggleCarouselLoop()
    {
        IsCarouselPaused = !IsCarouselPaused;
        if (IsCarouselPaused)
        {
            StopCarouselTimer();
            CarouselPauseIcon = "Play";
            CarouselPauseTooltip = localizer.T("resumeCarousel");
        }
        else
        {
            StartCarouselTimer();
            CarouselPauseIcon = "Pause";
            CarouselPauseTooltip = localizer.T("pauseCarousel");
        }
    }

    // L5: Previous/Next banner navigation
    [RelayCommand]
    private void SelectPreviousBanner()
    {
        if (BannerItems.Count == 0) return;
        var prev = CarouselSelectedIndex - 1;
        if (prev < 0) prev = BannerItems.Count - 1;
        SelectBanner(prev);
    }

    [RelayCommand]
    private void SelectNextBanner()
    {
        if (BannerItems.Count == 0) return;
        var next = CarouselSelectedIndex + 1;
        if (next >= BannerItems.Count) next = 0;
        SelectBanner(next);
    }

    [RelayCommand]
    private void PauseResume()
    {
        if (!CanPauseOperation)
        {
            return;
        }

        if (gameDownloadService.IsPaused)
        {
            gameDownloadService.Resume();
            IsPaused = false;
            PauseResumeText = localizer.T("pause");
            PauseResumeIcon = "Pause";
            ProgressDetail = localizer.T("downloading");
            OperationNote = localizer.T("resumeRequested");
        }
        else
        {
            gameDownloadService.Pause();
            IsPaused = true;
            PauseResumeText = localizer.T("resume");
            PauseResumeIcon = "Play";
            ProgressDetail = localizer.T("paused");
            ProgressSpeed = "";
            ProgressEstimated = "";
            OperationNote = localizer.T("pauseRequested");
        }
    }

    [RelayCommand]
    private async Task RequestUninstallAsync()
    {
        if (currentSnapshot is null)
        {
            OperationNote = localizer.T("stateNotLoaded");
            return;
        }

        var validation = await gameUninstallService.ValidateAsync(currentSnapshot.LocalGame.GamePath);
        if (!validation.Success)
        {
            OperationNote = validation.Message;
            return;
        }

        UninstallConfirmText = localizer.F(
            "uninstallConfirmText",
            currentSnapshot.LocalGame.GamePath,
            Math.Max(0, validation.AffectedFileCount - 2));
        IsUninstallConfirmVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmUninstallAsync()
    {
        if (currentSnapshot is null)
        {
            OperationNote = localizer.T("stateNotLoaded");
            return;
        }

        IsUninstallConfirmVisible = false;
        IsBusy = true;
        IsProgressPanelVisible = true;
        IsInstallPanelVisible = false;
        IsControlPanelVisible = false;
        ProgressTitle = localizer.T("uninstalling");
        ProgressDetail = localizer.T("deletingManifestFiles");

        try
        {
            var result = await gameUninstallService.UninstallAsync(currentSnapshot, ApplyProgress);
            OperationNote = result.Message;
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            OperationNote = localizer.F("networkWithMessage", exception.Message);
            await TryLogErrorAsync("Game uninstall failed.", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelUninstall()
    {
        IsUninstallConfirmVisible = false;
        OperationNote = localizer.T("uninstallCanceled");
    }

    // Phase 2.1: Toast notification handling

    [RelayCommand]
    private void DismissToast(string toastId)
    {
        var toast = ActiveToasts.FirstOrDefault(t => t.Id == toastId);
        if (toast is not null)
        {
            ActiveToasts.Remove(toast);
        }
    }

    private void OnToastRaised(ToastNotification notification)
    {
        _ = ShowToastAsync(notification, lifetimeCts.Token);
    }

    private async Task ShowToastAsync(ToastNotification notification, CancellationToken cancellationToken)
    {
        if (!ToastNotificationsEnabled) return;
        try
        {
            if (notification is null || string.IsNullOrWhiteSpace(notification.Message))
                return;

            await Dispatcher.UIThread.InvokeAsync(() => ActiveToasts.Add(notification));
            await Task.Delay(notification.DurationMs, cancellationToken);
            // Use Background priority to avoid colliding with ItemsControl rendering iteration
            await Dispatcher.UIThread.InvokeAsync(
                () => { try { ActiveToasts.Remove(notification); } catch (InvalidOperationException) { } },
                DispatcherPriority.Background);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Toast display is best-effort.
        }
    }

    // Carousel auto-play logic

    partial void OnCarouselSelectedIndexChanged(int value)
    {
        for (int i = 0; i < BannerDots.Count; i++)
            BannerDots[i].IsActive = i == value;
        UpdateCarouselPageText();
    }

    private void UpdateCarouselPageText()
    {
        if (BannerItems.Count > 1)
        {
            CarouselPageText = localizer.F("carouselPage", CarouselSelectedIndex + 1, BannerItems.Count);
        }
        else
        {
            CarouselPageText = "";
        }
    }

    private async Task PreloadBannerImagesAsync()
    {
        // Snapshot to avoid collection-modified exception when BannerItems is cleared during iteration
        var snapshot = BannerItems.ToArray();
        foreach (var item in snapshot)
        {
            if (string.IsNullOrWhiteSpace(item.ImageUrl))
                continue;

            try
            {
                var proxyMode = currentSnapshot?.Settings.ProxyMode ?? ProxyModes.Direct;
                var bytes = await imageCacheService.GetImageBytesAsync(item.ImageUrl, proxyMode, lifetimeCts.Token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!BannerItems.Contains(item))
                    {
                        return;
                    }

                    // Dispose previous bitmap to avoid leaking unmanaged resources
                    // when banner images are preloaded multiple times (e.g. after ApplyRemoteContent)
                    item.BannerBitmap?.Dispose();
                    item.BannerBitmap = new global::Avalonia.Media.Imaging.Bitmap(
                        new System.IO.MemoryStream(bytes));
                });
            }
            catch
            {
                // Banner image failed to load — will show as blank in carousel
            }
        }
    }

    private void StartCarouselTimer()
    {
        StopCarouselTimer();
        // A3: Don't start if carousel is paused
        if (!BannerIsLooping || BannerItems.Count <= 1 || IsCarouselPaused)
            return;

        carouselTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(BannerIntervalMs)
        };
        carouselTimer.Tick += (_, _) =>
        {
            if (BannerItems.Count == 0) return;
            var next = CarouselSelectedIndex + 1;
            CarouselSelectedIndex = next % BannerItems.Count;
        };
        carouselTimer.Start();
    }

    private void StopCarouselTimer()
    {
        carouselTimer?.Stop();
        carouselTimer = null;
    }

    [RelayCommand]
    private void SelectNewsCategory(NewsCategory? category)
    {
        if (category is null) return;
        foreach (var c in NewsCategories)
            c.IsActive = c == category;
        SelectedNewsCategory = category;
    }

    [RelayCommand]
    private void SelectBanner(int index)
    {
        if (index >= 0 && index < BannerItems.Count)
        {
            CarouselSelectedIndex = index;
            // I6: Pause auto-advance briefly on manual navigation, then resume
            StopCarouselTimer();
            _ = ScheduleCarouselResumeAfterDelayAsync();
        }
    }

    // I6: Resume carousel auto-advance after a delay following manual navigation
    private async Task ScheduleCarouselResumeAfterDelayAsync()
    {
        carouselDelayCts?.Cancel();
        carouselDelayCts = new CancellationTokenSource();
        var token = carouselDelayCts.Token;
        try
        {
            await Task.Delay(ManualNavResumeDelayMs, token);
            if (!IsCarouselPaused && BannerIsLooping && BannerItems.Count > 1)
            {
                StartCarouselTimer();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when another navigation happens before delay expires
        }
    }

    [RelayCommand]
    private void ShowSettings()
    {
        // M4: If closing settings with unsaved changes, show confirmation dialog
        if (IsSettingsVisible && IsSettingsDirty)
        {
            IsUnsavedChangesVisible = true;
            return;
        }

        IsSettingsVisible = !IsSettingsVisible;

        // When opening, reload settings from the snapshot so the UI always
        // reflects the last-saved state (not stale values from a prior discard).
        if (IsSettingsVisible && currentSnapshot is { } s)
        {
            SelectedGamePath = s.Settings.GamePath;
            SelectedLaunchCheckMode = s.Settings.LaunchCheckMode;
            SelectedProxyMode = s.Settings.ProxyMode;
            SelectedPatchUrlGroup = s.Settings.PatchUrlGroup;
            SelectedCloseBehavior = s.Settings.CloseBehavior;
            SelectedLanguage = s.Settings.Language;
            SelectedThemeMode = s.Settings.ThemeMode;
            LoadThemeColorState(s.Settings);
            SelectedDownloadSpeedLimit = s.Settings.DownloadSpeedLimit;
            ToastNotificationsEnabled = s.Settings.ToastNotificationsEnabled;
            ShowRemoteContentCard = s.Settings.ShowRemoteContentCard;
            CustomBackgroundPath = s.Settings.CustomBackgroundPath;
            IsCustomBackground = !string.IsNullOrWhiteSpace(s.Settings.CustomBackgroundPath);
            SelectedBackgroundSource = s.Settings.BackgroundSource;
            IsCustomBackgroundSelected = s.Settings.BackgroundSource == BackgroundSources.Custom;
        }
    }

    // M4: User chose to discard unsaved changes — close dialog and reset properties to saved state
    [RelayCommand]
    private void DiscardSettingsChanges()
    {
        IsUnsavedChangesVisible = false;
        IsSettingsVisible = false;
        IsSettingsDirty = false;
        // Reset ViewModel properties to the last-saved values so the dialog
        // shows correct state when reopened. Sourced from currentSnapshot
        // (which reflects what was written to settings.json on the last save)
        // rather than a fresh disk read that could race with a concurrent write.
        if (currentSnapshot is { } s)
        {
            SelectedGamePath = s.Settings.GamePath;
            SelectedLaunchCheckMode = s.Settings.LaunchCheckMode;
            SelectedProxyMode = s.Settings.ProxyMode;
            SelectedPatchUrlGroup = s.Settings.PatchUrlGroup;
            SelectedCloseBehavior = s.Settings.CloseBehavior;
            SelectedLanguage = s.Settings.Language;
            SelectedThemeMode = s.Settings.ThemeMode;
            LoadThemeColorState(s.Settings);
            SelectedDownloadSpeedLimit = s.Settings.DownloadSpeedLimit;
            ToastNotificationsEnabled = s.Settings.ToastNotificationsEnabled;
            ShowRemoteContentCard = s.Settings.ShowRemoteContentCard;
            CustomBackgroundPath = s.Settings.CustomBackgroundPath;
            IsCustomBackground = !string.IsNullOrWhiteSpace(s.Settings.CustomBackgroundPath);
            SelectedBackgroundSource = s.Settings.BackgroundSource;
            IsCustomBackgroundSelected = s.Settings.BackgroundSource == BackgroundSources.Custom;
        }
    }

    // M4: User chose to keep editing
    [RelayCommand]
    private void KeepEditingSettings()
    {
        IsUnsavedChangesVisible = false;
    }

    [RelayCommand]
    private void ExecuteRestoreWindow()
    {
        RestoreWindow?.Invoke();
    }

    [RelayCommand]
    private void OpenOfficialSite()
    {
        externalLinkService.Open(LauncherConstants.OfficialWebsiteUrl);
    }

    [RelayCommand]
    private async Task OpenResourcePanelAsync()
    {
        IsResourcePanelVisible = true;
        await LoadResourcePanelAsync(lifetimeCts.Token);
    }

    [RelayCommand]
    private void CloseResourcePanel()
    {
        IsResourcePanelVisible = false;
    }

    [RelayCommand]
    private async Task RefreshResourcePanelAsync()
    {
        await LoadResourcePanelAsync(lifetimeCts.Token);
    }

    [RelayCommand]
    private async Task SaveManualResourcePanelUidAsync()
    {
        var uid = ManualResourcePanelUid.Trim();
        if (string.IsNullOrWhiteSpace(uid))
        {
            ResourcePanelMessage = localizer.T("resourcePanelUidEmpty");
            return;
        }

        IsResourcePanelBusy = true;
        try
        {
            await resourcePanelUidService.SaveManualUidAsync(uid, lifetimeCts.Token);
            ResourcePanelUid = uid;
            ResourcePanelUidText = localizer.F("resourcePanelCurrentUid", uid);
            IsResourcePanelUidMissing = false;
            ResourcePanelMessage = localizer.T("resourcePanelUidSaved");
            await LoadResourcePanelDataAsync(uid, lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ResourcePanelMessage = localizer.F("resourcePanelLoadFailed", exception.Message);
            await TryLogErrorAsync("Resource panel manual UID save failed.", exception);
        }
        finally
        {
            IsResourcePanelBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveResourcePanelAsync()
    {
        if (string.IsNullOrWhiteSpace(ResourcePanelUid))
        {
            IsResourcePanelUidMissing = true;
            ResourcePanelMessage = localizer.F("resourcePanelUidMissing", resourcePanelUidService.CookieLibraryPath);
            return;
        }

        IsResourcePanelBusy = true;
        try
        {
            await resourcePanelApiClient.SaveConfigAsync(
                ResourcePanelUid,
                ToResourcePanelMode(GetResourcePanelItem(ResourcePanelResourceCodes.Text).IsEnabled),
                ToResourcePanelMode(GetResourcePanelItem(ResourcePanelResourceCodes.Voice).IsEnabled),
                ToResourcePanelMode(GetResourcePanelItem(ResourcePanelResourceCodes.Media).IsEnabled),
                lifetimeCts.Token);
            ResourcePanelMessage = localizer.T("resourcePanelSaved");
            toastService.ShowSuccess(localizer.T("resourcePanelSaved"));
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ResourcePanelMessage = localizer.F("resourcePanelLoadFailed", exception.Message);
            toastService.ShowError(ResourcePanelMessage);
            await TryLogErrorAsync("Resource panel save failed.", exception);
        }
        finally
        {
            IsResourcePanelBusy = false;
        }
    }

    [RelayCommand]
    private void OpenGitHubRepository()
    {
        externalLinkService.Open(LauncherConstants.GitHubRepositoryUrl);
    }

    [RelayCommand]
    private void OpenExternalUrl(string? url)
    {
        externalLinkService.Open(url);
    }

    [RelayCommand]
    private void Minimize()
    {
        MinimizeWindow?.Invoke();
    }

    [RelayCommand]
    private void Close()
    {
        CloseWindow?.Invoke();
    }

    private bool PrepareOperation()
    {
        if (IsBusy)
        {
            OperationNote = localizer.T("busy");
            return false;
        }

        if (currentSnapshot is null)
        {
            OperationNote = localizer.T("stateNotLoaded");
            return false;
        }

        IsBusy = true;
        IsProgressPanelVisible = true;
        IsInstallPanelVisible = false;
        IsControlPanelVisible = false;
        ProgressTitle = localizer.T("preparing");
        ProgressValue = 0;
        ProgressDetail = localizer.T("buildingFileList");
        ProgressSpeed = "";
        ProgressSize = "";
        ProgressEstimated = "";
        IsPaused = false;
        CanPauseOperation = false;
        PauseResumeText = localizer.T("pause");
        PauseResumeIcon = "Pause";
        return true;
    }

    private void ApplyProgress(GameOperationProgress progress)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            if (Application.Current is null)
            {
                ApplyProgressCore(progress);
                return;
            }

            Dispatcher.UIThread.Post(() => ApplyProgress(progress));
            return;
        }

        ApplyProgressCore(progress);
    }

    private void ApplyProgressCore(GameOperationProgress progress)
    {
        IsProgressPanelVisible = true;
        IsInstallPanelVisible = false;
        IsControlPanelVisible = false;
        ProgressValue = Math.Clamp(progress.Progress, 0, 100);
        ProgressTitle = ResolveProgressTitle(progress);
        ProgressDetail = progress.Stage switch
        {
            "repair-confirm" => progress.AffectedFileCount > 0
                ? $"{progress.AffectedFileCount} files need repair ({FileSizeFormatter.Format(progress.DownloadedSize)})"
                : "No files need repair",
            "paused" => localizer.T("paused"),
            _ => progress.Stage
        };
        ProgressSpeed = progress.Stage == "repair-confirm" || progress.Stage == "paused" ? "" : progress.Speed;
        ProgressSize = progress.TotalSize > 0 && progress.Stage != "repair-confirm" && progress.Stage != "paused"
            ? $"{FileSizeFormatter.Format(progress.DownloadedSize)} / {FileSizeFormatter.Format(progress.TotalSize)}"
            : "";
        ProgressEstimated = progress.TotalSize > 0 && progress.Stage == "download" && !string.IsNullOrWhiteSpace(progress.Estimated)
            ? $"ETA {progress.Estimated}"
            : "";
        IsPaused = progress.IsPaused;
        CanPauseOperation = progress.CanPause;
        PauseResumeText = progress.IsPaused ? localizer.T("resume") : localizer.T("pause");
        PauseResumeIcon = progress.IsPaused ? "Play" : "Pause";
    }

    private async Task ResumePersistedDownloadAsync(CancellationToken cancellationToken)
    {
        if (currentSnapshot is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = await gameDownloadService.ResumePersistedAsync(currentSnapshot, ApplyProgress, cancellationToken);
            if (result is null)
            {
                return;
            }

            OperationNote = result.Message;
            if (result.Success)
                toastService.ShowSuccess(result.Message);
            else
                toastService.ShowError(result.Message);
            skipNextPersistedResume = true;
            await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            OperationNote = localizer.F("networkWithMessage", exception.Message);
            await TryLogErrorAsync("Persisted game download resume failed.", exception);
        }
        finally
        {
            IsBusy = false;
            CanPauseOperation = false;
        }
    }

    private async Task ApplySnapshotAsync(LauncherStatusSnapshot snapshot)
    {
        var gameConfig = snapshot.Remote.GameConfig;
        var baseConfig = snapshot.Remote.BaseConfig;
        var localGame = snapshot.LocalGame;
        var localConfig = localGame.GameConfig;

        SelectedGamePath = localGame.GamePath;
        SelectedLaunchCheckMode = snapshot.Settings.LaunchCheckMode;
        SelectedProxyMode = snapshot.Settings.ProxyMode;
        SelectedPatchUrlGroup = snapshot.Settings.PatchUrlGroup;
        SelectedCloseBehavior = snapshot.Settings.CloseBehavior;
        SelectedLanguage = snapshot.Settings.Language;
        SelectedThemeMode = snapshot.Settings.ThemeMode;
        LoadThemeColorState(snapshot.Settings);
        SelectedDownloadSpeedLimit = snapshot.Settings.DownloadSpeedLimit;
        ToastNotificationsEnabled = snapshot.Settings.ToastNotificationsEnabled;
        ShowRemoteContentCard = snapshot.Settings.ShowRemoteContentCard;
        CustomBackgroundPath = snapshot.Settings.CustomBackgroundPath;
        IsCustomBackground = !string.IsNullOrWhiteSpace(CustomBackgroundPath);
        SelectedBackgroundSource = snapshot.Settings.BackgroundSource;
        IsCustomBackgroundSelected = SelectedBackgroundSource == BackgroundSources.Custom;
        ApplyLanguage(snapshot.Settings.Language);
        ApplyTheme(snapshot.Settings.ThemeMode);
        await UpdateBackgroundImageAsync();
        ApplyThemeColor(snapshot.Settings.ThemeColorMode, ParseColorOrDefault(snapshot.Settings.CustomThemeColor));

        CurrentViewTitle = ResolveStatusText(snapshot);
        StatusText = ResolveStatusText(snapshot);
        PathText = localGame.GamePath;
        VersionText = snapshot.IsInstalled
            ? localizer.F("versionInstalled", localConfig?.Version, gameConfig?.GameLatestVersion ?? localizer.T("unknown"))
            : localizer.F("versionLatest", gameConfig?.GameLatestVersion ?? localizer.T("unknown"));
        NetworkText = localizer.T("statusNetworkLoaded");
        LaunchCheckText = localizer.F("launchCheckWithMessage", ResolveLaunchCheckDisplayName(snapshot.Settings.LaunchCheckMode));
        ExecutableText = string.IsNullOrWhiteSpace(localConfig?.Name)
            ? localizer.F("executableValue", gameConfig?.GameStartExeName ?? localizer.T("unknown"))
            : localizer.F("executableValue", localConfig.Name);
        DiskSpaceText = ResolveDiskSpaceText(localGame.GamePath, gameConfig?.DecompressionSize);
        SettingsSummary = localizer.F(
            "settingsSummaryWithTheme",
            snapshot.Settings.ProxyMode,
            snapshot.Settings.CloseBehavior,
            ResolveLanguageDisplayName(snapshot.Settings.Language),
            ResolveThemeDisplayName(snapshot.Settings.ThemeMode));
        InstallButtonText = snapshot.IsInstalled ? localizer.T("updateGame") : localizer.T("installGame");
        OperationNote = ResolveOperationNote(snapshot, localGame, baseConfig);
        ApplyRemoteContent(snapshot.Remote);

        SetIdlePanels();
    }

    private void ApplyRemoteContent(LauncherRemoteState remote)
    {
        DisposeBannerBitmaps();
        BannerItems.Clear();
        NewsItems.Clear();
        SocialMediaItems.Clear();

        var operations = remote.OperationsResource;
        if (operations?.OperationsResourceOpen == true)
        {
            // Apply carousel settings from API
            BannerIsLooping = operations.BannerLoop;
            BannerIntervalMs = operations.TimeInterval > 0 ? operations.TimeInterval * 1000 : 5000;

            foreach (var item in operations.OperationsBannerList)
            {
                BannerItems.Add(new RemoteContentItem
                {
                    Title = localizer.T("banner"),
                    Subtitle = item.BannerImg ?? "",
                    Url = item.JumpUrl ?? "",
                    ImageUrl = item.BannerImg ?? ""
                });
            }

            // Rebuild dot indicators
            BannerDots.Clear();
            for (int i = 0; i < BannerItems.Count; i++)
                BannerDots.Add(new BannerDot { Index = i, IsActive = i == 0 });
            CarouselSelectedIndex = 0;
            HasMultipleBanners = BannerItems.Count > 1;

            // L5/A3: Initialize carousel controls
            UpdateCarouselPageText();
            IsCarouselPaused = false;
            CarouselPauseIcon = "Pause";
            CarouselPauseTooltip = localizer.T("pauseCarousel");

            // Preload banner images asynchronously
            _ = PreloadBannerImagesAsync();
        }

        NewsCategories.Clear();
        if (operations?.NewsList?.Code == 0)
        {
            foreach (var item in operations.NewsList.Data?.News ?? [])
            {
                var category = new NewsCategory { Label = item.TypeLabel ?? "" };
                // I7: Limit to max 50 items per category for performance
                const int maxItemsPerCategory = 50;
                foreach (var row in item.Rows.Take(maxItemsPerCategory))
                {
                    category.Items.Add(new RemoteContentItem
                    {
                        Title = row.Title ?? "",
                        Subtitle = FormatUnixMilliseconds(row.PublishTime, null),
                        Url = row.Link ?? ""
                    });
                }

                if (category.Items.Count > 0)
                    NewsCategories.Add(category);
            }
        }
        foreach (var noticeType in operations?.NoticeList ?? [])
        {
            var category = new NewsCategory { Label = noticeType.NoticeType ?? "" };
            // M5: Limit notices per category to match the news item cap
            const int maxNoticeItemsPerCategory = 50;
            foreach (var notice in noticeType.NoticeDetailList.Take(maxNoticeItemsPerCategory))
            {
                category.Items.Add(new RemoteContentItem
                {
                    Title = notice.NoticeTitle ?? "",
                    Subtitle = notice.NoticeTime ?? "",
                    Url = notice.JumpUrl ?? ""
                });
            }

            if (category.Items.Count > 0)
                NewsCategories.Add(category);
        }

        // Select first category by default
        SelectedNewsCategory = NewsCategories.FirstOrDefault();
        if (SelectedNewsCategory is not null)
            SelectedNewsCategory.IsActive = true;

        // Also populate flat list for backward compatibility
        NewsItems.Clear();
        foreach (var cat in NewsCategories)
            foreach (var item in cat.Items)
                NewsItems.Add(item);

        var social = remote.SocialMediaResource;
        if (social?.SocialMediaResourceOpen == true)
        {
            foreach (var item in social.SocialMediaResourceList)
            {
                SocialMediaItems.Add(new RemoteContentItem
                {
                    Title = item.SocialMediaChannel ?? "",
                    Subtitle = string.IsNullOrWhiteSpace(item.QrImg) ? item.JumpUrl ?? "" : item.QrImg,
                    Url = item.JumpUrl ?? "",
                    ImageUrl = item.QrImg ?? "",
                    SocialIconKind = ResolveSocialIconKind(item.SocialMediaChannel)
                });
            }
        }

        if (social?.ContactCustomerComplaint == true)
        {
            SocialMediaItems.Add(new RemoteContentItem
            {
                Title = localizer.T("contactCustomerSupport"),
                Subtitle = ResolveContactSubtitle(social),
                Url = ResolveContactUrl(social),
                SocialIconKind = "Headset"
            });
        }

        NoticeText = remote.BaseConfig?.NoticePopOpen == true ? remote.BaseConfig.NoticeContent ?? "" : "";
        HasNotice = !string.IsNullOrWhiteSpace(NoticeText);
        HasBannerItems = BannerItems.Count > 0;
        HasNewsItems = NewsCategories.Count > 0;
        HasSocialMediaItems = SocialMediaItems.Count > 0;
        UpdateRemoteContentVisibility();

        // Start carousel auto-play if banners are available
        if (HasBannerItems)
            StartCarouselTimer();

        _ = ShowNoticeDialogIfNeededAsync(remote.BaseConfig, lifetimeCts.Token);
    }

    [RelayCommand]
    private void DismissNotice()
    {
        IsNoticeDialogVisible = false;
        if (currentSnapshot?.Remote.BaseConfig?.ExitLauncherOpen == true)
        {
            CloseWindow?.Invoke();
        }
    }

    /// <summary>
    /// Returns a hex SHA256 hash (first 16 chars) of the input for notice deduplication.
    /// Uses SHA256 instead of a simple linear hash to avoid collisions.
    /// </summary>
    private static string ComputeNoticeHash(string input)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }

    private async Task ShowNoticeDialogIfNeededAsync(BaseConfigResponse? baseConfig, CancellationToken cancellationToken)
    {
        if (baseConfig?.NoticePopOpen != true
            || string.IsNullOrWhiteSpace(baseConfig.NoticeContent))
        {
            return;
        }

        try
        {
            var noticeHash = ComputeNoticeHash(baseConfig.NoticeContent);
            var shownNotices = await noticeStateService.ReadShownNoticesAsync(cancellationToken);
            if (shownNotices.Contains(noticeHash))
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                NoticeDialogContent = baseConfig.NoticeContent;
                NoticeDialogConfirmText = baseConfig.ExitLauncherOpen
                    ? localizer.T("noticeExit")
                    : localizer.T("noticeConfirm");
                IsNoticeDialogVisible = true;
            });
            await noticeStateService.SaveShownNoticeAsync(noticeHash, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Notice display state is best-effort.
        }
    }

    private void SetIdlePanels()
    {
        IsProgressPanelVisible = false;
        CanPauseOperation = false;
        IsControlPanelVisible = currentSnapshot?.IsInstalled == true && currentSnapshot.BelowLowestVersion == false;
        IsInstallPanelVisible = !IsControlPanelVisible;
    }

    private void ApplyLanguage(string language)
    {
        localizer.SetLanguage(language);
        I18n.Apply(localizer);
        RuntimeInfoText = localizer.F("runtimeInfo", FrameworkVersion, LauncherConstants.AvaloniaVersion);
        BuildInfoText = localizer.F("buildInfo", PlatformName, LauncherConstants.BuildConfiguration);
        RefreshThemeOptions();
        RefreshThemeColorOptions();
        RefreshLaunchCheckModeOptions();
        RefreshProxyModeOptions();
        RefreshPatchUrlGroupOptions();
        RefreshCloseBehaviorOptions();
        RefreshDownloadSpeedLimitOptions();
        RefreshBackgroundSourceOptions();
        RefreshResourcePanelItems();
        if (!string.IsNullOrWhiteSpace(ResourcePanelUid))
        {
            ResourcePanelUidText = localizer.F("resourcePanelCurrentUid", ResourcePanelUid);
        }
        SelectedLanguage = language;
        DiskSpaceText = localizer.T("diskSpaceEmpty");
        // I5: Localized native dialog titles
        GameFolderPickerTitle = localizer.T("chooseInstallFolder");
        BackgroundImagePickerTitle = localizer.T("chooseBackgroundImageTitle");
        BackgroundFolderPickerTitle = localizer.T("chooseBackgroundFolderTitle");
        // A3: Carousel pause tooltip
        CarouselPauseTooltip = localizer.T("pauseCarousel");
        if (currentSnapshot is null)
        {
            CurrentViewTitle = localizer.T("loadingTitle");
            StatusText = localizer.T("loadingStatus");
            PathText = localizer.T("pathLoading");
            VersionText = localizer.T("versionLoading");
            NetworkText = localizer.T("networkLoading");
            LaunchCheckText = localizer.T("launchCheckLoading");
            ExecutableText = localizer.T("executableLoading");
            SettingsSummary = localizer.T("settings");
            OperationNote = localizer.T("operationTelemetryLocal");
            InstallButtonText = localizer.T("installGame");
            ProgressTitle = localizer.T("preparing");
        }
    }

    private string ResolveLanguageDisplayName(string language)
    {
        return LanguageOptions.FirstOrDefault(option => option.Code == language)?.DisplayName
            ?? LanguageOptions.First(option => option.Code == LauncherLanguages.Auto).DisplayName;
    }

    // C2: User-friendly launch check display names
    private string ResolveLaunchCheckDisplayName(string launchCheckMode)
    {
        return launchCheckMode switch
        {
            Models.LaunchCheckModes.RemoteManifest => localizer.T("statusLaunchCheckRemote"),
            Models.LaunchCheckModes.None => localizer.T("statusLaunchCheckNone"),
            _ => localizer.T("statusLaunchCheckLocal")
        };
    }

    // I1: Settings dirty tracking — mark dirty when settings panel is open and a setting changes
    private void MarkSettingsDirtyIfVisible()
    {
        if (suppressSettingsDirty)
            return;

        if (IsSettingsVisible && !IsSettingsDirty)
            IsSettingsDirty = true;
    }

    partial void OnSelectedLaunchCheckModeChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnSelectedProxyModeChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnSelectedPatchUrlGroupChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnSelectedCloseBehaviorChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnSelectedLanguageChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnSelectedDownloadSpeedLimitChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnSelectedGamePathChanged(string value) => MarkSettingsDirtyIfVisible();
    partial void OnToastNotificationsEnabledChanged(bool value) => MarkSettingsDirtyIfVisible();
    // (merged into existing OnShowRemoteContentCardChanged below)

    partial void OnSelectedThemeModeChanged(string value)
    {
        MarkSettingsDirtyIfVisible();
    }

    partial void OnSelectedThemeColorModeChanged(string value)
    {
        IsCustomThemeColorSelected = value == ThemeColorModes.Custom;
        IsWallpaperThemeColorSelected = value == ThemeColorModes.Wallpaper;
        if (IsWallpaperThemeColorSelected && ThemeColorPaletteItems.Count == 0)
        {
            RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
        }

        UpdateThemeColorPreview();
        MarkSettingsDirtyIfVisible();
    }

    partial void OnSelectedCustomThemeColorChanged(Color value)
    {
        UpdateThemeColorPreview();
        MarkSettingsDirtyIfVisible();
    }

    partial void OnSelectedThemeColorPaletteIndexChanged(int value)
    {
        UpdateThemeColorPaletteSelection();
        UpdateThemeColorPreview();
        if (SelectedThemeColorMode == ThemeColorModes.Wallpaper)
        {
            ApplyThemeColor(SelectedThemeColorMode, SelectedCustomThemeColor);
        }

        MarkSettingsDirtyIfVisible();
    }

    partial void OnSelectedBackgroundSourceChanged(string value)
    {
        IsCustomBackgroundSelected = value == BackgroundSources.Custom;
        MarkSettingsDirtyIfVisible();
    }

    partial void OnShowRemoteContentCardChanged(bool value)
    {
        MarkSettingsDirtyIfVisible();
    }

    private void UpdateRemoteContentVisibility()
    {
        HasRemoteContent = ShowRemoteContentCard
            && (HasNotice || HasBannerItems || HasNewsItems || HasSocialMediaItems);
    }

    private void UpdateThemeColorPreview()
    {
        var color = ResolveThemeColor(SelectedThemeColorMode, SelectedCustomThemeColor);
        ThemeColorPreviewBrush = new SolidColorBrush(color);
    }

    [RelayCommand]
    private void RefreshThemeColorPalette()
    {
        RefreshThemeColorPaletteFromCurrentBackground(markDirty: true);
        if (SelectedThemeColorMode == ThemeColorModes.Wallpaper)
        {
            ApplyThemeColor(SelectedThemeColorMode, SelectedCustomThemeColor);
        }
    }

    private void LoadThemeColorState(LauncherSettings settings)
    {
        var oldSuppressSettingsDirty = suppressSettingsDirty;
        suppressSettingsDirty = true;
        try
        {
            SelectedThemeColorMode = settings.ThemeColorMode;
            SelectedCustomThemeColor = ParseColorOrDefault(settings.CustomThemeColor);
            IsCustomThemeColorSelected = settings.ThemeColorMode == ThemeColorModes.Custom;
            IsWallpaperThemeColorSelected = settings.ThemeColorMode == ThemeColorModes.Wallpaper;
            ReplaceThemeColorPalette(settings.ThemeColorPalette, settings.SelectedThemeColorPaletteIndex, markDirty: false);
        }
        finally
        {
            suppressSettingsDirty = oldSuppressSettingsDirty;
        }
    }

    private void RefreshThemeColorPaletteFromCurrentBackground(bool markDirty)
    {
        if (BackgroundImageSource is not Bitmap bitmap)
        {
            ReplaceThemeColorPalette([], 0, markDirty);
            return;
        }

        var colors = ThemeColorExtractionService.ExtractPalette(bitmap)
            .Select(ThemeColorExtractionService.ToColorHex)
            .ToArray();
        var selectedIndex = SelectedThemeColorPaletteIndex < colors.Length
            ? SelectedThemeColorPaletteIndex
            : 0;
        ReplaceThemeColorPalette(colors, selectedIndex, markDirty);
    }

    private void ReplaceThemeColorPalette(IEnumerable<string> colors, int selectedIndex, bool markDirty)
    {
        var normalizedColors = colors
            .Select(ParseThemeColorPaletteColor)
            .OfType<Color>()
            .Select(ThemeColorExtractionService.ToColorHex)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var oldColors = ThemeColorPaletteItems.Select(item => item.ColorHex).ToArray();
        var oldSelectedIndex = SelectedThemeColorPaletteIndex;
        var oldSuppressSettingsDirty = suppressSettingsDirty;
        suppressSettingsDirty = true;
        try
        {
            ThemeColorPaletteItems.Clear();
            for (var i = 0; i < normalizedColors.Length; i++)
            {
                var color = ParseColorOrDefault(normalizedColors[i]);
                ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
                {
                    Index = i,
                    ColorHex = normalizedColors[i],
                    Brush = new SolidColorBrush(color)
                });
            }

            SelectedThemeColorPaletteIndex = normalizedColors.Length == 0
                ? 0
                : Math.Clamp(selectedIndex, 0, normalizedColors.Length - 1);
            UpdateThemeColorPaletteSelection();
        }
        finally
        {
            suppressSettingsDirty = oldSuppressSettingsDirty;
        }

        UpdateThemeColorPreview();
        if (markDirty
            && (!oldColors.SequenceEqual(normalizedColors, StringComparer.Ordinal)
                || oldSelectedIndex != SelectedThemeColorPaletteIndex))
        {
            MarkSettingsDirtyIfVisible();
        }
    }

    private void UpdateThemeColorPaletteSelection()
    {
        for (var i = 0; i < ThemeColorPaletteItems.Count; i++)
        {
            ThemeColorPaletteItems[i].IsSelected = i == SelectedThemeColorPaletteIndex;
        }
    }

    private List<string> GetThemeColorPaletteHexes() =>
        ThemeColorPaletteItems.Select(item => item.ColorHex).ToList();

    private static void ApplyTheme(string themeMode)
    {
        var themeVariant = themeMode switch
        {
            ThemeModes.Light => ThemeVariant.Light,
            ThemeModes.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = themeVariant;
        }
    }

    private void ApplyThemeColor(string themeColorMode, Color customColor)
    {
        if (themeColorMode == ThemeColorModes.Wallpaper && ThemeColorPaletteItems.Count == 0)
        {
            RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
        }

        var color = ResolveThemeColor(themeColorMode, customColor);
        ThemeColorPreviewBrush = new SolidColorBrush(color);
        ApplyAccentBrushes(color);
    }

    private Color ResolveThemeColor(string themeColorMode, Color customColor)
    {
        return themeColorMode switch
        {
            ThemeColorModes.System => GetSystemAccentColor(),
            ThemeColorModes.Custom => customColor,
            ThemeColorModes.Wallpaper => ResolveThemeColorFromPalette() ?? Color.Parse(LauncherConstants.DefaultThemeColor),
            _ => Color.Parse(LauncherConstants.DefaultThemeColor)
        };
    }

    private Color? ResolveThemeColorFromPalette()
    {
        if (ThemeColorPaletteItems.Count == 0)
        {
            return null;
        }

        var selectedIndex = Math.Clamp(SelectedThemeColorPaletteIndex, 0, ThemeColorPaletteItems.Count - 1);
        return ParseThemeColorPaletteColor(ThemeColorPaletteItems[selectedIndex].ColorHex);
    }

    private static Color GetSystemAccentColor()
    {
        if (Application.Current?.TryGetResource("SystemAccentColor", ThemeVariant.Default, out var value) == true
            && value is Color color)
        {
            return color;
        }

        return Color.Parse(LauncherConstants.DefaultThemeColor);
    }

    private static void ApplyAccentBrushes(Color color)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        SetBrush(application, "LauncherAccentBrush", color);
        SetBrush(application, "LauncherAccentHoverBrush", AdjustColor(color, 1.15));
        SetBrush(application, "LauncherAccentPressedBrush", AdjustColor(color, 0.85));
        SetBrush(application, "LauncherAccentSoftBrush", Color.FromArgb(0x24, color.R, color.G, color.B));
        SetBrush(application, "LauncherAccentBorderBrush", Color.FromArgb(0x80, color.R, color.G, color.B));
        SetBrush(application, "LauncherFocusRingBrush", Color.FromArgb(0x99, color.R, color.G, color.B));
        SetBrush(application, "LauncherCarouselDotActiveBrush", color);
        SetBrush(application, "LauncherToastInfoBrush", color);
        SetBrush(application, "LauncherInfoTextBrush", color);
        SetBrush(application, "LauncherOnAccentBrush", GetReadableOnAccentColor(color));
    }

    private static void SetBrush(Application application, string key, Color color)
    {
        if (application.Resources.TryGetResource(key, ThemeVariant.Default, out var value)
            && value is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        application.Resources[key] = new SolidColorBrush(color);
    }

    private static Color AdjustColor(Color color, double factor)
    {
        static byte Adjust(byte value, double factor) =>
            (byte)Math.Clamp((int)Math.Round(value * factor), 0, 255);

        return Color.FromArgb(color.A, Adjust(color.R, factor), Adjust(color.G, factor), Adjust(color.B, factor));
    }

    private static Color GetReadableOnAccentColor(Color color)
    {
        var luminance = (0.2126 * SrgbToLinear(color.R / 255d))
            + (0.7152 * SrgbToLinear(color.G / 255d))
            + (0.0722 * SrgbToLinear(color.B / 255d));
        return luminance > 0.45 ? Color.FromRgb(0x12, 0x18, 0x20) : Colors.White;
    }

    private static double SrgbToLinear(double value) =>
        value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static string ToColorHex(Color color) =>
        ThemeColorExtractionService.ToColorHex(color);

    private static Color ParseColorOrDefault(string? value) =>
        Color.TryParse(value, out var color) ? color : Color.Parse(LauncherConstants.DefaultThemeColor);

    private static Color? ParseThemeColorPaletteColor(string? value) =>
        Color.TryParse(value, out var color) ? Color.FromArgb(0xFF, color.R, color.G, color.B) : null;

    private void RefreshThemeOptions()
    {
        foreach (var option in ThemeOptions)
        {
            option.DisplayName = option.Code switch
            {
                ThemeModes.Light => localizer.T("themeLight"),
                ThemeModes.Dark => localizer.T("themeDark"),
                _ => localizer.T("themeSystem")
            };
        }
    }

    private void RefreshLaunchCheckModeOptions()
    {
        foreach (var option in LaunchCheckModeOptions)
        {
            option.DisplayName = option.Code switch
            {
                Models.LaunchCheckModes.RemoteManifest => localizer.T("launchCheckRemoteManifest"),
                Models.LaunchCheckModes.None => localizer.T("launchCheckNone"),
                _ => localizer.T("launchCheckLocalManifest")
            };
        }
    }

    private void RefreshProxyModeOptions()
    {
        foreach (var option in ProxyModeOptions)
        {
            option.DisplayName = option.Code switch
            {
                Models.ProxyModes.System => localizer.T("proxySystem"),
                _ => localizer.T("proxyDirect")
            };
        }
    }

    private void RefreshPatchUrlGroupOptions()
    {
        foreach (var option in PatchUrlGroupOptions)
        {
            option.DisplayName = option.Code switch
            {
                Models.PatchUrlGroups.Cafe => localizer.T("downloadSourceCafe"),
                _ => localizer.T("downloadSourceOfficial")
            };
        }
    }

    private void RefreshCloseBehaviorOptions()
    {
        foreach (var option in CloseBehaviorOptions)
        {
            option.DisplayName = option.Code switch
            {
                Models.CloseBehaviors.Exit => localizer.T("closeBehaviorExit"),
                _ => localizer.T("closeBehaviorMinimize")
            };
        }
    }

    private void RefreshDownloadSpeedLimitOptions()
    {
        foreach (var option in DownloadSpeedLimitOptions)
        {
            option.DisplayName = option.Code switch
            {
                DownloadSpeedLimits._1MBs => localizer.T("speed1MBs"),
                DownloadSpeedLimits._5MBs => localizer.T("speed5MBs"),
                DownloadSpeedLimits._10MBs => localizer.T("speed10MBs"),
                DownloadSpeedLimits._25MBs => localizer.T("speed25MBs"),
                DownloadSpeedLimits._50MBs => localizer.T("speed50MBs"),
                _ => localizer.T("speedUnlimited")
            };
        }
    }

    private void RefreshBackgroundSourceOptions()
    {
        foreach (var option in BackgroundSourceOptions)
        {
            option.DisplayName = option.Code switch
            {
                BackgroundSources.Remote => localizer.T("backgroundSourceRemote"),
                BackgroundSources.Custom => localizer.T("backgroundSourceCustom"),
                _ => localizer.T("backgroundSourceBundled")
            };
        }
    }

    private string ResolveThemeDisplayName(string themeMode)
    {
        return ThemeOptions.FirstOrDefault(option => option.Code == themeMode)?.DisplayName
            ?? localizer.T("themeSystem");
    }

    private async Task TryLogErrorAsync(string title, Exception exception)
    {
        try
        {
            await diagnostics.ErrorAsync(title, exception);
        }
        catch
        {
            OperationNote = $"{OperationNote} Local diagnostics log write failed.";
        }
    }

    private string ResolveStatusText(LauncherStatusSnapshot snapshot)
    {
        if (!snapshot.IsInstalled)
        {
            return localizer.T("notInstalled");
        }

        if (snapshot.BelowLowestVersion)
        {
            return localizer.T("updateRequired");
        }

        if (snapshot.NeedsUpdate)
        {
            return localizer.T("updateAvailable");
        }

        return localizer.T("ready");
    }

    private async Task LoadResourcePanelAsync(CancellationToken cancellationToken)
    {
        IsResourcePanelBusy = true;
        ResourcePanelMessage = localizer.T("resourcePanelLoading");
        SetResourcePanelStatusText(localizer.T("resourcePanelLoading"));
        try
        {
            var uid = await resourcePanelUidService.ResolveUidAsync(cancellationToken);
            ResourcePanelUid = uid;
            ResourcePanelUidText = string.IsNullOrWhiteSpace(uid)
                ? ""
                : localizer.F("resourcePanelCurrentUid", uid);
            ManualResourcePanelUid = uid;
            if (string.IsNullOrWhiteSpace(uid))
            {
                IsResourcePanelUidMissing = true;
                ResourcePanelMessage = localizer.F("resourcePanelUidMissing", resourcePanelUidService.CookieLibraryPath);
                SetResourcePanelStatusText(localizer.T("resourcePanelFailed"));
                return;
            }

            IsResourcePanelUidMissing = false;
            await LoadResourcePanelDataAsync(uid, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ResourcePanelMessage = localizer.F("resourcePanelLoadFailed", exception.Message);
            SetResourcePanelStatusText(localizer.T("resourcePanelFailed"));
            await TryLogErrorAsync("Resource panel load failed.", exception);
        }
        finally
        {
            IsResourcePanelBusy = false;
        }
    }

    private async Task LoadResourcePanelDataAsync(string uid, CancellationToken cancellationToken)
    {
        ResourcePanelMessage = localizer.T("resourcePanelLoading");
        SetResourcePanelStatusText(localizer.T("resourcePanelLoading"));
        resourcePanelApiClient.SetProxyMode(currentSnapshot?.Settings.ProxyMode ?? ProxyModes.Direct);
        var statusTask = resourcePanelApiClient.GetStatusAsync(cancellationToken);
        var configTask = resourcePanelApiClient.GetConfigAsync(uid, cancellationToken);
        await Task.WhenAll(statusTask, configTask);
        ApplyResourcePanelStatus(await statusTask);
        ApplyResourcePanelConfig(await configTask);
        ResourcePanelMessage = localizer.T("statusNetworkLoaded");
    }

    private void ApplyResourcePanelStatus(ResourcePanelStatusResponse status)
    {
        ApplyResourcePanelStatus(
            GetResourcePanelItem(ResourcePanelResourceCodes.Text),
            status.Text);
        ApplyResourcePanelStatus(
            GetResourcePanelItem(ResourcePanelResourceCodes.Voice),
            status.Voice);
        ApplyResourcePanelStatus(
            GetResourcePanelItem(ResourcePanelResourceCodes.Media),
            status.Media);
    }

    private void ApplyResourcePanelStatus(ResourcePanelItem item, ResourcePanelStatusGroup status)
    {
        item.OfficialVersion = string.IsNullOrWhiteSpace(status.Official.Version)
            ? "--"
            : status.Official.Version;
        item.LocalizedVersion = string.IsNullOrWhiteSpace(status.Localized.Version)
            ? "--"
            : status.Localized.Version;
        item.StatusText = string.Equals(item.OfficialVersion, item.LocalizedVersion, StringComparison.Ordinal)
            ? localizer.T("resourcePanelReady")
            : localizer.T("resourcePanelWaiting");
    }

    private void ApplyResourcePanelConfig(ResourcePanelConfigResponse config)
    {
        GetResourcePanelItem(ResourcePanelResourceCodes.Text).IsEnabled =
            config.Text == ResourcePanelResourceModes.Chinese;
        GetResourcePanelItem(ResourcePanelResourceCodes.Voice).IsEnabled =
            config.Voice == ResourcePanelResourceModes.Chinese;
        GetResourcePanelItem(ResourcePanelResourceCodes.Media).IsEnabled =
            config.Media == ResourcePanelResourceModes.Chinese;
    }

    private void SetResourcePanelStatusText(string statusText)
    {
        foreach (var item in ResourcePanelItems)
        {
            item.StatusText = statusText;
            item.OfficialVersion = "--";
            item.LocalizedVersion = "--";
        }
    }

    private ResourcePanelItem GetResourcePanelItem(string code)
    {
        return ResourcePanelItems.First(item => item.Code == code);
    }

    private void RefreshResourcePanelItems()
    {
        GetResourcePanelItem(ResourcePanelResourceCodes.Text).DisplayName = localizer.T("resourcePanelGameText");
        GetResourcePanelItem(ResourcePanelResourceCodes.Voice).DisplayName = localizer.T("resourcePanelMainVoice");
        GetResourcePanelItem(ResourcePanelResourceCodes.Media).DisplayName = localizer.T("resourcePanelMedia");
        if (ResourcePanelItems.All(item => string.IsNullOrWhiteSpace(item.StatusText)))
        {
            SetResourcePanelStatusText(localizer.T("resourcePanelLoading"));
        }
    }

    private static string ToResourcePanelMode(bool enabled)
    {
        return enabled
            ? ResourcePanelResourceModes.Chinese
            : ResourcePanelResourceModes.Japanese;
    }

    private string ResolveOperationNote(
        LauncherStatusSnapshot snapshot,
        LocalGameState localGame,
        BaseConfigResponse? baseConfig)
    {
        if (!string.IsNullOrWhiteSpace(localGame.Error))
        {
            return localizer.F("localGameReadError", localGame.Error);
        }

        if (baseConfig?.NoticePopOpen == true && !string.IsNullOrWhiteSpace(baseConfig.NoticeContent))
        {
            return baseConfig.NoticeContent;
        }

        if (!snapshot.IsInstalled)
        {
            return localizer.T("choosePathInstall");
        }

        if (snapshot.BelowLowestVersion)
        {
            return localizer.T("belowLowestVersion");
        }

        if (snapshot.NeedsUpdate)
        {
            return localizer.T("updateAvailableCanStart");
        }

        return localizer.T("operationTelemetryLocal");
    }

    private string ResolveDiskSpaceText(string gamePath, string? requiredSize)
    {
        var required = string.IsNullOrWhiteSpace(requiredSize)
            ? "--"
            : requiredSize.Replace(" ", "", StringComparison.Ordinal);
        var availableBytes = diskSpaceService.GetAvailableBytes(gamePath);
        var available = availableBytes.HasValue ? FileSizeFormatter.Format(availableBytes.Value) : "--";
        return localizer.F("diskSpace", required, available);
    }

    private static string FormatUnixMilliseconds(long value, string? typeLabel)
    {
        var prefix = string.IsNullOrWhiteSpace(typeLabel) ? "" : $"{typeLabel} | ";
        if (value <= 0)
        {
            return prefix.TrimEnd(' ', '|');
        }

        try
        {
            var date = DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime;
            return $"{prefix}{date:yyyy/MM/dd}";
        }
        catch (ArgumentOutOfRangeException)
        {
            return prefix.TrimEnd(' ', '|');
        }
    }

    // C4: Map social media channel names to Material Icon kinds
    private static string ResolveSocialIconKind(string? channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName))
            return "Link";

        var name = channelName.ToLowerInvariant();
        if (name.Contains("twitter") || name.Contains("x")) return "Twitter";
        if (name.Contains("youtube")) return "Youtube";
        if (name.Contains("discord")) return "Discord";
        if (name.Contains("line")) return "Chat";
        if (name.Contains("公式") || name.Contains("official") || name.Contains("website")) return "Web";
        if (name.Contains("niconico") || name.Contains("ニコ")) return "Television";
        if (name.Contains("pixiv")) return "Palette";
        if (name.Contains("forum") || name.Contains("コミュ")) return "Forum";
        if (name.Contains("mail") || name.Contains("メール")) return "Email";
        if (name.Contains("instagram")) return "Instagram";
        if (name.Contains("facebook")) return "Facebook";
        if (name.Contains("tiktok")) return "MusicNote";
        return "Link";
    }

    private static string ResolveContactSubtitle(SocialMediaResourceResponse social)
    {
        return social.ContactCustomerComplaintType switch
        {
            0 => social.WebCustomerComplaintUrl ?? "",
            1 => social.AiHelpCustomerComplaint?.AihelpDomain ?? "",
            _ => social.MailCustomerComplaintUrl ?? ""
        };
    }

    private static string ResolveContactUrl(SocialMediaResourceResponse social)
    {
        return social.ContactCustomerComplaintType switch
        {
            0 => social.WebCustomerComplaintUrl ?? "",
            1 => "",
            _ => string.IsNullOrWhiteSpace(social.MailCustomerComplaintUrl)
                ? ""
                : $"mailto:{social.MailCustomerComplaintUrl}"
        };
    }

    private string ResolveProgressTitle(GameOperationProgress progress)
    {
        return progress.OperationKind switch
        {
            GameOperationKinds.Repair => localizer.T("repairing"),
            GameOperationKinds.Uninstall => localizer.T("uninstalling"),
            GameOperationKinds.Download => localizer.T("downloading"),
            _ => localizer.T("working")
        };
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        (BackgroundImageSource as IDisposable)?.Dispose();
        toastService.ToastRaised -= OnToastRaised;
        StopCarouselTimer();
        carouselDelayCts?.Cancel();
        carouselDelayCts?.Dispose();
        DisposeBannerBitmaps();
        gameDownloadService.Stop(clearPersistedState: false);
        lifetimeCts.Cancel();
        lifetimeCts.Dispose();
    }

    private void DisposeBannerBitmaps()
    {
        foreach (var item in BannerItems)
        {
            item.BannerBitmap?.Dispose();
            item.BannerBitmap = null;
        }
    }

    private void RefreshThemeColorOptions()
    {
        foreach (var option in ThemeColorOptions)
        {
            option.DisplayName = option.Code switch
            {
                ThemeColorModes.System => localizer.T("themeColorSystem"),
                ThemeColorModes.Wallpaper => localizer.T("themeColorWallpaper"),
                ThemeColorModes.Custom => localizer.T("themeColorCustom"),
                _ => localizer.T("themeColorDefault")
            };
        }
    }
}
