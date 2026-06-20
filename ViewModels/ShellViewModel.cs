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

    private static readonly string FrameworkVersion = RuntimeInformation.FrameworkDescription;
    private static readonly string PlatformName = OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsLinux() ? "Linux"
        : OperatingSystem.IsMacOS() ? "macOS"
        : "Unknown";

    [ObservableProperty]
    private string productName = LauncherConstants.ProductName;

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

    public ShellViewModel(LocalizationService localizer)
    {
        this.localizer = localizer;
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
            CurrentViewTitle = localizer.T("loadingTitle");
            StatusText = localizer.T("loadingStatus");
            PathText = localizer.T("pathLoading");
            VersionText = localizer.T("versionLoading");
            NetworkText = localizer.T("networkLoading");
            LaunchCheckText = localizer.T("launchCheckLoading");
            ExecutableText = localizer.T("executableLoading");
            ExecutableNameText = localizer.T("loadingValue");
            NetworkStatusValueText = localizer.T("loadingValue");
            LaunchCheckValueText = localizer.T("loadingValue");
            SettingsSummary = localizer.T("settings");
            OperationNote = localizer.T("operationTelemetryLocal");
        }
    }

    public void SetLoading()
    {
        StatusIconKind = "HelpCircleOutline";
        CurrentViewTitle = localizer.T("loadingTitle");
        StatusText = localizer.T("connectingApi");
        ExecutableNameText = localizer.T("loadingValue");
        NetworkStatusValueText = localizer.T("loadingValue");
        LaunchCheckValueText = localizer.T("loadingValue");
        OperationNote = localizer.T("loadingStatus");
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
        ExecutableNameText = localizer.T("loadingValue");
        LaunchCheckValueText = localizer.T("loadingValue");
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
        VersionText = snapshot.IsInstalled
            ? localizer.F("versionInstalled", localConfig?.Version, gameConfig?.GameLatestVersion ?? localizer.T("unknown"))
            : localizer.F("versionLatest", gameConfig?.GameLatestVersion ?? localizer.T("unknown"));
        NetworkText = localizer.T("statusNetworkLoaded");
        NetworkStatusValueText = localizer.T("statusNetworkLoaded");
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

    internal static string ResolveStatusIconKind(LauncherStatusSnapshot snapshot)
    {
        if (!snapshot.IsInstalled)
        {
            return "HelpCircleOutline";
        }

        if (snapshot.BelowLowestVersion)
        {
            return "Alert";
        }

        if (snapshot.NeedsUpdate)
        {
            return "AlertCircle";
        }

        return "CheckAll";
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
}
