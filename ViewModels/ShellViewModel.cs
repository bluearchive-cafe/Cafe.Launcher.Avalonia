using System;
using System.Runtime.InteropServices;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    private readonly LocalizationService localizer;
    private readonly EasterEggAudioService? easterEggAudioService;
    private int launcherVersionClickCount;

    private static readonly string FrameworkVersion = RuntimeInformation.FrameworkDescription;
    private static readonly string PlatformName = OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsLinux() ? "Linux"
        : OperatingSystem.IsMacOS() ? "macOS"
        : "Unknown";

    [ObservableProperty]
    private string productName = ResolveProductName(DateTime.Now, Random.Shared.Next(2));

    [ObservableProperty]
    private string launcherVersionText = "";

    [ObservableProperty]
    private string commitShaText = "";

    [ObservableProperty]
    private string frameworkVersionText = "";

    [ObservableProperty]
    private string avaloniaVersionText = "";

    [ObservableProperty]
    private string platformText = "";

    [ObservableProperty]
    private string buildConfigText = "";

    [ObservableProperty]
    private string buildTimeText = "";

    [ObservableProperty]
    private string currentViewTitle = "";

    [ObservableProperty]
    private string statusIconKind = "HelpCircleOutline";

    [ObservableProperty]
    private string statusText = "";

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
    private string networkStatusValueText = "";

    [ObservableProperty]
    private string launchCheckValueText = "";

    [ObservableProperty]
    private string diskSpaceText = "";

    [ObservableProperty]
    private string settingsSummary = "";

    [ObservableProperty]
    private string operationNote = "";

    [ObservableProperty]
    private bool isBusy = true;

    public LocalizedStrings I18n { get; } = new();

    public string GameFolderPickerTitle { get; private set; } = "";

    public ShellViewModel(
        LocalizationService localizer,
        EasterEggAudioService? easterEggAudioService = null)
    {
        this.localizer = localizer;
        this.easterEggAudioService = easterEggAudioService;
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

    public bool RegisterLauncherVersionClick()
    {
        launcherVersionClickCount++;
        if (launcherVersionClickCount != 8)
        {
            return false;
        }

        launcherVersionClickCount = 0;
        easterEggAudioService?.PlayKuyashi();
        return true;
    }

    public void ApplyLanguage(
        string language,
        SettingsViewModel settings,
        ResourcePanelViewModel resourcePanel,
        bool hasSnapshot)
    {
        localizer.SetLanguage(language);
        I18n.Apply(localizer);
        LauncherVersionText = localizer.F("launcherVersionLabel", BuildInfo.LauncherVersion);
        CommitShaText = localizer.F("commitLabel", BuildInfo.CommitSha);
        FrameworkVersionText = FrameworkVersion;
        AvaloniaVersionText = $"Avalonia {BuildInfo.AvaloniaVersion}";
        PlatformText = localizer.F("platformLabel", PlatformName);
        BuildConfigText = localizer.F("buildConfigurationLabel", BuildInfo.BuildConfiguration);
        BuildTimeText = localizer.F("buildTimeLabel", BuildInfo.BuildTime);
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
            StatusIconKind = "HelpCircleOutline";
            CurrentViewTitle = localizer.T("launcherLoadingTitle");
            StatusText = localizer.T("launcherLoadingStatus");
            PathText = localizer.T("pathLoading");
            VersionText = localizer.T("versionLoading");
            NetworkText = localizer.T("networkLoading");
            LaunchCheckText = localizer.T("launchCheckLoading");
            ExecutableText = localizer.T("executableLoading");
            ExecutableNameText = localizer.T("launcherLoadingValue");
            NetworkStatusValueText = localizer.T("launcherLoadingValue");
            LaunchCheckValueText = localizer.T("launcherLoadingValue");
            SettingsSummary = localizer.T("settings");
            OperationNote = localizer.T("operationTelemetryLocal");
        }
    }

    public void SetLoading()
    {
        StatusIconKind = "HelpCircleOutline";
        CurrentViewTitle = localizer.T("launcherLoadingTitle");
        StatusText = localizer.T("connectingApi");
        ExecutableNameText = localizer.T("launcherLoadingValue");
        NetworkStatusValueText = localizer.T("launcherLoadingValue");
        LaunchCheckValueText = localizer.T("launcherLoadingValue");
        OperationNote = localizer.T("launcherLoadingStatus");
    }

    public void SetRefreshError(Exception exception)
    {
        StatusIconKind = "Alert";
        CurrentViewTitle = localizer.T("networkUnavailableTitle");
        StatusText = localizer.T("networkError");
        NetworkText = localizer.F("networkWithMessage", exception.Message);
        NetworkStatusValueText = exception.Message;
        VersionText = localizer.T("versionUnavailable");
        OperationNote = localizer.T("apiFailedNoFileChange");
        PathText = localizer.T("pathLoading");
        ExecutableText = localizer.T("executableLoading");
        ExecutableNameText = localizer.T("launcherLoadingValue");
        LaunchCheckValueText = localizer.T("launcherLoadingValue");
    }

    public void ApplySnapshot(LauncherStatusSnapshot snapshot, SettingsViewModel settings)
    {
        var gameConfig = snapshot.Remote.GameConfig;
        var baseConfig = snapshot.Remote.BaseConfig;
        var localGame = snapshot.LocalGame;
        var localConfig = localGame.GameConfig;
        var status = ResolveStatusText(snapshot);

        StatusIconKind = ResolveStatusIconKind(snapshot);
        CurrentViewTitle = status;
        StatusText = status;
        PathText = localGame.GamePath;
        VersionText = snapshot.RuntimeState != LauncherRuntimeState.NotInstalled
            ? localizer.F("versionInstalled", localConfig?.Version, gameConfig?.GameLatestVersion ?? localizer.T("unknown"))
            : localizer.F("versionLatest", gameConfig?.GameLatestVersion ?? localizer.T("unknown"));
        var networkStatus = snapshot.RuntimeState == LauncherRuntimeState.RemoteUnavailable
            ? localizer.T("gameRemoteStateUnavailable")
            : localizer.T("statusNetworkLoaded");
        NetworkText = networkStatus;
        NetworkStatusValueText = networkStatus;
        var launchCheckValue = settings.Options.ResolveLaunchCheckDisplayName(snapshot.Settings.LaunchCheckMode);
        SetLaunchCheckResult(launchCheckValue);
        var executableName = string.IsNullOrWhiteSpace(localConfig?.Name)
            ? gameConfig?.GameStartExeName ?? localizer.T("unknown")
            : localConfig.Name;
        ExecutableText = localizer.F("executableValue", executableName);
        ExecutableNameText = localizer.F("executableNameValue", executableName);
        DiskSpaceText = settings.Options.ResolveDiskSpaceText(localGame.GamePath, gameConfig?.DecompressionSize);
        SettingsSummary = localizer.F(
            "settingsSummaryWithTheme",
            snapshot.Settings.ProxyMode,
            snapshot.Settings.CloseBehavior,
            settings.Options.ResolveLanguageDisplayName(snapshot.Settings.Language),
            settings.Options.ResolveThemeDisplayName(snapshot.Settings.ThemeMode));
        OperationNote = ResolveOperationNote(snapshot, localGame, baseConfig);
    }

    public void SetLaunchCheckResult(string value)
    {
        LaunchCheckText = localizer.F("launchCheckWithMessage", value);
        LaunchCheckValueText = value;
    }

    private string ResolveStatusText(LauncherStatusSnapshot snapshot)
    {
        return snapshot.RuntimeState switch
        {
            LauncherRuntimeState.NotInstalled => localizer.T("gameNotInstalled"),
            LauncherRuntimeState.Corrupted => localizer.T("gameCorruptedInstallationState"),
            LauncherRuntimeState.IoFailure => localizer.T("gameInstallationStateReadFailed"),
            LauncherRuntimeState.RemoteUnavailable => localizer.T("gameRemoteStateUnavailable"),
            LauncherRuntimeState.BelowLowestVersion => localizer.T("updateRequired"),
            LauncherRuntimeState.UpdateAvailable => localizer.T("updateAvailable"),
            LauncherRuntimeState.Ready => localizer.T("ready"),
            _ => localizer.T("gameInstallationStateReadFailed")
        };
    }

    internal static string ResolveStatusIconKind(LauncherStatusSnapshot snapshot)
    {
        return snapshot.RuntimeState switch
        {
            LauncherRuntimeState.NotInstalled => "HelpCircleOutline",
            LauncherRuntimeState.Corrupted or
                LauncherRuntimeState.IoFailure or
                LauncherRuntimeState.RemoteUnavailable or
                LauncherRuntimeState.BelowLowestVersion => "Alert",
            LauncherRuntimeState.UpdateAvailable => "AlertCircle",
            LauncherRuntimeState.Ready => "CheckAll",
            _ => "Alert"
        };
    }

    private string ResolveOperationNote(
        LauncherStatusSnapshot snapshot,
        LocalInstallationState localGame,
        BaseConfigResponse? baseConfig)
    {
        if (snapshot.RuntimeState == LauncherRuntimeState.IoFailure
            && !string.IsNullOrWhiteSpace(localGame.Error))
        {
            return localizer.F("localGameReadError", localGame.Error);
        }

        if (baseConfig?.NoticePopOpen == true && !string.IsNullOrWhiteSpace(baseConfig.NoticeContent))
        {
            return baseConfig.NoticeContent;
        }

        return snapshot.RuntimeState switch
        {
            LauncherRuntimeState.NotInstalled => localizer.T("choosePathInstall"),
            LauncherRuntimeState.Corrupted => localizer.T("gameCorruptedInstallationState"),
            LauncherRuntimeState.IoFailure => localizer.T("gameInstallationStateReadFailed"),
            LauncherRuntimeState.RemoteUnavailable => localizer.T("gameRemoteStateUnavailable"),
            LauncherRuntimeState.BelowLowestVersion => localizer.T("gameBelowLowestVersion"),
            LauncherRuntimeState.UpdateAvailable => localizer.T("updateAvailable"),
            LauncherRuntimeState.Ready => localizer.T("operationTelemetryLocal"),
            _ => localizer.T("gameInstallationStateReadFailed")
        };
    }
}
