using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class WindowChromeViewModel : ViewModelBase
{
    private readonly SettingsViewModel settings;
    private readonly RemoteContentViewModel remoteContent;
    private readonly DialogsViewModel dialogs;
    private readonly GameOperationsViewModel operations;

    [ObservableProperty]
    private bool isSettingsVisible;

    public Func<LauncherStatusSnapshot?>? GetSnapshot { get; set; }

    public Action? MinimizeWindow { get; set; }

    public Action? CloseWindow { get; set; }

    public Action? RestoreWindow { get; set; }

    public WindowChromeViewModel(
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
        if (IsSettingsVisible && GetSnapshot?.Invoke() is { } snapshot)
        {
            settings.LoadFromSnapshot(snapshot.Settings);
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
        MinimizeWindow?.Invoke();
    }

    [RelayCommand]
    private void ExecuteRestoreWindow()
    {
        if (remoteContent.HasBannerItems)
        {
            remoteContent.StartCarouselTimer();
        }

        RestoreWindow?.Invoke();
    }

    [RelayCommand]
    private void OpenOfficialSite()
    {
        ExternalLinkService.Open(
            ResolveOfficialSiteUrl(settings.Editor.GetSavedSnapshot().PatchUrlGroup));
    }

    internal static string ResolveOfficialSiteUrl(string patchUrlGroup) =>
        patchUrlGroup == PatchUrlGroups.Cafe
            ? LauncherConstants.CafeWebsiteUrl
            : LauncherConstants.OfficialGameWebsiteUrl;

    [RelayCommand]
    private void OpenGitHubRepository()
    {
        ExternalLinkService.Open(LauncherConstants.GitHubReleaseRepositoryUrl);
    }

    [RelayCommand]
    private void OpenDataDirectory()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LauncherConstants.ProductName);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public void OpenExternalUrl(string? url)
    {
        ExternalLinkService.Open(url);
    }

    [RelayCommand]
    private void Close()
    {
        if (operations.IsDownloadRunning)
        {
            dialogs.ShowDownloadRunningCloseConfirm();
            return;
        }

        CloseWindow?.Invoke();
    }

    public void CloseAfterStoppingDownload()
    {
        operations.StopDownload(clearPersistedState: true);
        CloseWindow?.Invoke();
    }
}
