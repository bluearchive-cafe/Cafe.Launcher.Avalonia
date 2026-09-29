using System;
using System.IO;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.Diagnostics;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.ViewModels;

internal partial class WindowChromeViewModel : ViewModelBase
{
    private readonly LauncherProductProfile productProfile;
    private readonly YostarGameProfile gameProfile;
    private readonly LauncherDataRoot dataRoot;
    private readonly SettingsViewModel settings;
    private readonly RemoteContentViewModel remoteContent;
    private readonly DialogsViewModel dialogs;
    private readonly IGameOperationActivity operations;
    private readonly DebugViewModel debug;
    private readonly Action<string?> openExternalUrl;
    private readonly Action<string> openDirectory;

    [ObservableProperty]
    private bool isSettingsVisible;

    public event Action? MinimizeRequested;
    public event Action? CloseRequested;
    public event Action? ShutdownRequested;
    public event Action? RestoreRequested;

    public WindowChromeViewModel(
        LauncherProductProfile productProfile,
        YostarGameProfile gameProfile,
        LauncherDataRoot dataRoot,
        SettingsViewModel settings,
        RemoteContentViewModel remoteContent,
        DialogsViewModel dialogs,
        IGameOperationActivity operations,
        DebugViewModel debug)
        : this(
            productProfile,
            gameProfile,
            dataRoot,
            settings,
            remoteContent,
            dialogs,
            operations,
            debug,
            ExternalLinkService.Open,
            static path => ShellFolderOpener.OpenInFileManager(path))
    {
    }

    internal WindowChromeViewModel(
        LauncherProductProfile productProfile,
        YostarGameProfile gameProfile,
        LauncherDataRoot dataRoot,
        SettingsViewModel settings,
        RemoteContentViewModel remoteContent,
        DialogsViewModel dialogs,
        IGameOperationActivity operations,
        DebugViewModel debug,
        Action<string?> openExternalUrl,
        Action<string> openDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.productProfile = productProfile;
        this.gameProfile = gameProfile;
        this.dataRoot = dataRoot;
        this.settings = settings;
        this.remoteContent = remoteContent;
        this.dialogs = dialogs;
        this.operations = operations;
        this.debug = debug;
        this.openExternalUrl = openExternalUrl;
        this.openDirectory = openDirectory;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        if (settings.IsSaving)
        {
            return;
        }

        if (IsSettingsVisible && settings.IsSettingsDirty)
        {
            settings.IsUnsavedChangesVisible = true;
            return;
        }

        IsSettingsVisible = !IsSettingsVisible;
        if (IsSettingsVisible)
        {
            settings.LoadFromSnapshot(settings.Editor.GetSavedSnapshot());
        }
    }

    [RelayCommand]
    private async Task DiscardSettingsChangesAsync()
    {
        await settings.DiscardChangesAsync();
        IsSettingsVisible = false;
    }

    [RelayCommand]
    private void KeepEditingSettings()
    {
        settings.KeepEditing();
    }

    [RelayCommand]
    private void Minimize()
    {
        remoteContent.StopCarouselTimer();
        MinimizeRequested?.Invoke();
    }

    [RelayCommand]
    private void ExecuteRestoreWindow()
    {
        if (remoteContent.HasBannerItems)
        {
            remoteContent.StartCarouselTimer();
        }

        RestoreRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenOfficialSite()
    {
        openExternalUrl(ResolveOfficialSiteUrl(
            settings.Editor.GetSavedSnapshot().PatchUrlGroup,
            productProfile.CafeWebsiteUrl,
            gameProfile.OfficialWebsiteUrl));
    }

    internal static string ResolveOfficialSiteUrl(
        string patchUrlGroup,
        string cafeWebsiteUrl,
        string officialGameWebsiteUrl) =>
        patchUrlGroup == PatchUrlGroups.Cafe
            ? cafeWebsiteUrl
            : officialGameWebsiteUrl;

    [RelayCommand]
    private void OpenAboutOfficialSite()
    {
        openExternalUrl(productProfile.CafeWebsiteUrl);
    }

    [RelayCommand]
    private void OpenGitHubRepository()
    {
        openExternalUrl(productProfile.GitHubReleaseRepositoryUrl);
    }

    [RelayCommand]
    private void OpenGitHubReleaseRepository()
    {
        openExternalUrl(productProfile.GitHubReleaseRepositoryUrl);
    }

    [RelayCommand]
    private void OpenIssueTracker()
    {
        openExternalUrl(productProfile.IssueTrackerUrl);
    }

    [RelayCommand]
    private void OpenHelpDocs()
    {
        openExternalUrl(productProfile.HelpDocsUrl);
    }

    [RelayCommand]
    private void OpenPrivacyPolicy()
    {
        openExternalUrl(productProfile.PrivacyPolicyUrl);
    }

    [RelayCommand]
    private void OpenDefaultBackgroundArtwork()
    {
        openExternalUrl(productProfile.DefaultBackgroundArtworkUrl);
    }

    [RelayCommand]
    private void OpenDataDirectory()
    {
        openDirectory(dataRoot.Root);
    }

    [RelayCommand]
    private async Task OpenDebugPanelAsync()
    {
        await debug.OpenCommand.ExecuteAsync(null);
    }

    public void OpenExternalUrl(string? url)
    {
        openExternalUrl(url);
    }

    [RelayCommand]
    private void Close()
    {
        if (operations.IsDownloadRunning)
        {
            dialogs.ShowDownloadRunningCloseConfirm();
            return;
        }

        CloseRequested?.Invoke();
    }

    public Task CloseAfterStoppingDownload()
    {
        operations.StopOperation(GameOperationStopIntent.UserStop);
        CloseRequested?.Invoke();
        return Task.CompletedTask;
    }

    public void RequestClose() => CloseRequested?.Invoke();

    public void RequestShutdown() => ShutdownRequested?.Invoke();
}
