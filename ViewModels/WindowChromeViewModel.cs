using System;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class WindowChromeViewModel : ViewModelBase
{
    private readonly ExternalLinkService externalLinkService;
    private SettingsViewModel? settings;
    private RemoteContentViewModel? remoteContent;
    private DialogsViewModel? dialogs;
    private GameOperationsViewModel? operations;

    [ObservableProperty]
    private bool isSettingsVisible;

    public Func<LauncherStatusSnapshot?>? GetSnapshot { get; set; }

    public Action? MinimizeWindow { get; set; }

    public Action? CloseWindow { get; set; }

    public Action? RestoreWindow { get; set; }

    public WindowChromeViewModel(ExternalLinkService externalLinkService)
    {
        this.externalLinkService = externalLinkService;
    }

    public void Configure(
        SettingsViewModel settings,
        RemoteContentViewModel remoteContent,
        DialogsViewModel dialogs,
        GameOperationsViewModel operations)
    {
        this.settings = settings;
        this.remoteContent = remoteContent;
        this.dialogs = dialogs;
        this.operations = operations;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        if (IsSettingsVisible && settings!.IsSettingsDirty)
        {
            settings.IsUnsavedChangesVisible = true;
            return;
        }

        IsSettingsVisible = !IsSettingsVisible;
        if (IsSettingsVisible && GetSnapshot?.Invoke() is { } snapshot)
        {
            settings!.LoadFromSnapshot(snapshot.Settings);
        }
    }

    [RelayCommand]
    private void DiscardSettingsChanges()
    {
        settings!.IsUnsavedChangesVisible = false;
        IsSettingsVisible = false;
        if (GetSnapshot?.Invoke() is { } snapshot)
        {
            settings.LoadFromSnapshot(snapshot.Settings);
        }
    }

    [RelayCommand]
    private void KeepEditingSettings()
    {
        settings!.IsUnsavedChangesVisible = false;
    }

    [RelayCommand]
    private void Minimize()
    {
        remoteContent!.StopCarouselTimer();
        MinimizeWindow?.Invoke();
    }

    [RelayCommand]
    private void ExecuteRestoreWindow()
    {
        if (remoteContent!.HasBannerItems)
        {
            remoteContent.StartCarouselTimer();
        }

        RestoreWindow?.Invoke();
    }

    [RelayCommand]
    private void OpenOfficialSite()
    {
        externalLinkService.Open(LauncherConstants.OfficialWebsiteUrl);
    }

    [RelayCommand]
    private void OpenGitHubRepository()
    {
        externalLinkService.Open(LauncherConstants.GitHubReleaseRepositoryUrl);
    }

    public void OpenExternalUrl(string? url)
    {
        externalLinkService.Open(url);
    }

    [RelayCommand]
    private void Close()
    {
        if (operations!.IsDownloadRunning)
        {
            dialogs!.ShowDownloadRunningCloseConfirm();
            return;
        }

        CloseWindow?.Invoke();
    }

    public void CloseAfterStoppingDownload()
    {
        operations!.StopDownload(clearPersistedState: true);
        CloseWindow?.Invoke();
    }
}
