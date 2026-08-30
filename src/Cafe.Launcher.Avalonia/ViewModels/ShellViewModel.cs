using System;
using System.Runtime.InteropServices;
using Avalonia.Media;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly LocalizationService localizer;

    private static readonly string RuntimeDescription =
        $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.RuntimeIdentifier}";
    private static readonly string PlatformDescription =
        $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}";

    [ObservableProperty]
    private string productName = ResolveProductName(DateTime.Now, Random.Shared.Next(2));

    [ObservableProperty]
    private string launcherVersionText = "";

    [ObservableProperty]
    private string frameworkVersionText = "";

    [ObservableProperty]
    private string avaloniaVersionText = "";

    /// <summary>Whether the host platform is Linux; gates Linux-only settings such as the game runtime.</summary>
    public bool IsLinuxPlatform => OperatingSystem.IsLinux();

    // 关于分区（ADR-018 融合变体）：身份卡版本 caption 与 key-value 行的值。
    [ObservableProperty]
    private string versionCaptionText = "";

    [ObservableProperty]
    private string commitShaValue = "";

    [ObservableProperty]
    private string buildConfigValue = "";

    [ObservableProperty]
    private string platformValue = "";

    [ObservableProperty]
    private string pathText = "";

    [ObservableProperty]
    private string versionText = "";

    [ObservableProperty]
    private string networkText = "";

    [ObservableProperty]
    private string launchCheckText = "";

    [ObservableProperty]
    private string executableText = "";

    [ObservableProperty]
    private string executableNameText = "";

    [ObservableProperty]
    private string launchCheckValueText = "";

    [ObservableProperty]
    private string diskSpaceText = "";

    [ObservableProperty]
    private bool isInstallBlockedByDiskSpace;

    [ObservableProperty]
    private string installDiskSpaceMessage = "";

    [ObservableProperty]
    private string settingsSummary = "";

    [ObservableProperty]
    private bool isBusy = true;

    [ObservableProperty]
    private FontFamily fontFamily =
        LanguageFontFamilyService.GetForEffectiveLanguage(LauncherLanguages.English);

    public LocalizedTextCatalog I18n { get; }

    public string GameFolderPickerTitle { get; private set; } = "";

    public ShellViewModel(LocalizationService localizer)
    {
        this.localizer = localizer;
        I18n = new LocalizedTextCatalog(localizer);
    }

    internal static string ResolveProductName(DateTime date, int randomIndex)
    {
        if (date.Month != 12 || date.Day != 8)
        {
            return LauncherConstants.ProductName;
        }

        return randomIndex switch
        {
            0 => "Midori Launcher",
            1 => "Momoi Launcher",
            _ => throw new ArgumentOutOfRangeException(nameof(randomIndex)),
        };
    }

    public void ApplyLanguage(
        string language,
        SettingsViewModel settings,
        ResourcePanelViewModel resourcePanel,
        bool hasSnapshot)
    {
        var effectiveLanguage = localizer.SetLanguage(language);
        FontFamily = LanguageFontFamilyService.GetForEffectiveLanguage(effectiveLanguage);
        LauncherVersionText = localizer.F("launcherVersionLabel", BuildInfo.LauncherVersion);
        FrameworkVersionText = RuntimeDescription;
        AvaloniaVersionText = BuildInfo.AvaloniaVersion;
        VersionCaptionText = string.IsNullOrWhiteSpace(BuildInfo.BuildTime)
            ? localizer.F("launcherVersionLabel", BuildInfo.LauncherVersion)
            : localizer.F("aboutVersionCaption", BuildInfo.LauncherVersion, BuildInfo.BuildTime);
        CommitShaValue = BuildInfo.CommitSha;
        BuildConfigValue = BuildInfo.BuildConfiguration;
        PlatformValue = PlatformDescription;
        settings.RefreshOptionDisplayNames();
        resourcePanel.RefreshDisplayNames();
        if (!string.IsNullOrWhiteSpace(resourcePanel.ResourcePanelUid))
        {
            resourcePanel.ResourcePanelUidText = localizer.F("resourcePanelCurrentUid", resourcePanel.ResourcePanelUid);
        }

        settings.Editor.Current.Language = language;
        DiskSpaceText = localizer.T("diskSpaceEmpty");
        GameFolderPickerTitle = localizer.T("chooseInstallFolder");

        if (!hasSnapshot)
        {
            PathText = localizer.T("pathLoading");
            VersionText = localizer.T("versionLoading");
            NetworkText = localizer.T("networkLoading");
            LaunchCheckText = localizer.T("launchCheckLoading");
            ExecutableText = localizer.T("executableLoading");
            ExecutableNameText = localizer.T("launcherLoadingValue");
            LaunchCheckValueText = localizer.T("launcherLoadingValue");
            SettingsSummary = localizer.T("settings");
        }
    }

    public void SetLoading()
    {
        ExecutableNameText = localizer.T("launcherLoadingValue");
        LaunchCheckValueText = localizer.T("launcherLoadingValue");
    }

    public void SetRefreshError(Exception exception)
    {
        NetworkText = localizer.F("networkWithMessage", exception.Message);
        VersionText = localizer.T("versionUnavailable");
        PathText = localizer.T("pathLoading");
        ExecutableText = localizer.T("executableLoading");
        ExecutableNameText = localizer.T("launcherLoadingValue");
        LaunchCheckValueText = localizer.T("launcherLoadingValue");
    }

    public void ApplySnapshot(LauncherStatusSnapshot snapshot, SettingsViewModel settings)
    {
        var gameConfig = snapshot.Remote.GameConfig;
        var localGame = snapshot.LocalGame;
        var localConfig = localGame.GameConfig;

        PathText = snapshot.Settings.GamePath;
        VersionText = snapshot.RuntimeState != LauncherRuntimeState.NotInstalled
            ? localizer.F("versionInstalled", localConfig?.Version, gameConfig?.GameLatestVersion ?? localizer.T("unknown"))
            : localizer.F("versionLatest", gameConfig?.GameLatestVersion ?? localizer.T("unknown"));
        var networkStatus = snapshot.RuntimeState == LauncherRuntimeState.RemoteUnavailable
            ? localizer.T("gameRemoteStateUnavailable")
            : localizer.T("statusNetworkLoaded");
        NetworkText = networkStatus;
        var launchCheckValue = settings.Options.ResolveLaunchCheckDisplayName(snapshot.Settings.LaunchCheckMode);
        SetLaunchCheckResult(launchCheckValue);
        var executableName = string.IsNullOrWhiteSpace(localConfig?.Name)
            ? gameConfig?.GameStartExeName ?? localizer.T("unknown")
            : localConfig.Name;
        ExecutableText = localizer.F("executableValue", executableName);
        ExecutableNameText = localizer.F("executableNameValue", executableName);
        var diskCheck = settings.Options.ResolveDiskSpaceCheck(localGame.GamePath, gameConfig?.DecompressionSize);
        DiskSpaceText = settings.Options.ResolveDiskSpaceText(gameConfig?.DecompressionSize, diskCheck);
        IsInstallBlockedByDiskSpace = snapshot.RuntimeState == LauncherRuntimeState.NotInstalled
            && diskCheck.RequiredBytes > 0
            && diskCheck.IsAvailableKnown
            && !diskCheck.HasEnoughSpace;
        InstallDiskSpaceMessage = IsInstallBlockedByDiskSpace
            ? localizer.F(
                "diskSpaceInsufficientDetail",
                FileSizeFormatter.Format(diskCheck.RequiredBytes),
                FileSizeFormatter.Format(diskCheck.AvailableBytes!.Value))
            : "";
        SettingsSummary = localizer.F(
            "settingsSummaryWithTheme",
            snapshot.Settings.ProxyMode,
            snapshot.Settings.CloseBehavior,
            settings.Options.ResolveLanguageDisplayName(snapshot.Settings.Language),
            settings.Options.ResolveThemeDisplayName(snapshot.Settings.ThemeMode));
    }

    public void SetLaunchCheckResult(string value)
    {
        LaunchCheckText = localizer.F("launchCheckWithMessage", value);
        LaunchCheckValueText = value;
    }

    public void Dispose()
    {
        I18n.Dispose();
    }
}
