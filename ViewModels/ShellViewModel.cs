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
        VersionText = snapshot.RuntimeState != LauncherRuntimeState.NotInstalled
            ? localizer.F("versionInstalled", localConfig?.Version, gameConfig?.GameLatestVersion ?? localizer.T("unknown"))
            : localizer.F("versionLatest", gameConfig?.GameLatestVersion ?? localizer.T("unknown"));
        var networkStatus = snapshot.RuntimeState == LauncherRuntimeState.RemoteUnavailable
            ? localizer.T("remoteStateUnavailable")
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
            LauncherRuntimeState.NotInstalled => localizer.T("notInstalled"),
            LauncherRuntimeState.Corrupted => localizer.T("corruptedInstallationState"),
            LauncherRuntimeState.IoFailure => localizer.T("installationStateReadFailed"),
            LauncherRuntimeState.RemoteUnavailable => localizer.T("remoteStateUnavailable"),
            LauncherRuntimeState.BelowLowestVersion => localizer.T("updateRequired"),
            LauncherRuntimeState.UpdateAvailable => localizer.T("updateAvailable"),
            LauncherRuntimeState.Ready => localizer.T("ready"),
            _ => localizer.T("installationStateReadFailed")
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
            LauncherRuntimeState.Corrupted => localizer.T("corruptedInstallationState"),
            LauncherRuntimeState.IoFailure => localizer.T("installationStateReadFailed"),
            LauncherRuntimeState.RemoteUnavailable => localizer.T("remoteStateUnavailable"),
            LauncherRuntimeState.BelowLowestVersion => localizer.T("belowLowestVersion"),
            LauncherRuntimeState.UpdateAvailable => localizer.T("updateAvailable"),
            LauncherRuntimeState.Ready => localizer.T("operationTelemetryLocal"),
            _ => localizer.T("installationStateReadFailed")
        };
    }
}
