using System;
using System.IO;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.Diagnostics;
using Cafe.Launcher.UI.Features.Games;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.ViewModels;

internal partial class WindowChromeViewModel : ObservableObject
{
    private readonly LauncherProductProfile productProfile;
    private readonly YostarGameProfile gameProfile;
    private readonly LauncherDataRoot dataRoot;
    private readonly SettingsViewModel settings;
    private readonly RemoteContentViewModel defaultRemoteContent;
    private RemoteContentViewModel remoteContent => games?.Active.RemoteContent ?? defaultRemoteContent;
    private readonly GameLibraryViewModel? games;
    private readonly IGameFileOperationCoordinator? fileOperations;
    private bool shutdownConfirmed;
    public bool IsShutdownPrepared => fileOperations?.IsShuttingDown ?? false;
    private bool HasUninstall => operations.IsUninstallExecuting
        || fileOperations?.ActiveOperation?.Kind == GameFileOperationKind.Uninstall;
    private bool HasDownloadJourney => operations.IsDownloadRunning
        || fileOperations?.ActiveOperation?.Kind is GameFileOperationKind.Download or GameFileOperationKind.Repair;
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
        DebugViewModel debug,
        GameLibraryViewModel? games = null,
        IGameFileOperationCoordinator? fileOperations = null)
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
            static path => ShellFolderOpener.OpenInFileManager(path),
            games,
            fileOperations)
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
        Action<string> openDirectory,
        GameLibraryViewModel? games = null,
        IGameFileOperationCoordinator? fileOperations = null)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.productProfile = productProfile;
        this.gameProfile = gameProfile;
        this.dataRoot = dataRoot;
        this.settings = settings;
        defaultRemoteContent = remoteContent;
        this.games = games;
        this.fileOperations = fileOperations;
        this.dialogs = dialogs;
        this.operations = operations;
        this.debug = debug;
        this.openExternalUrl = openExternalUrl;
        this.openDirectory = openDirectory;
    }

    [RelayCommand]
    private async Task ToggleSettings()
    {
        if (settings.IsSaving || games?.IsSwitching == true)
        {
            return;
        }

        if (IsSettingsVisible && settings.IsPreviewing)
        {
            settings.ReturnToSettings();
            return;
        }

        if (IsSettingsVisible && settings.IsSettingsDirty)
        {
            settings.IsUnsavedChangesVisible = true;
            return;
        }

        if (IsSettingsVisible)
        {
            // Returning every field to its saved value clears dirty state, but
            // the global background preview still needs its end-of-session reset.
            if (settings.IsGlobalBackgroundPreview)
            {
                await settings.DiscardChangesAsync();
            }
            IsSettingsVisible = false;
        }
        else
        {
            settings.BeginEdit(gameSpecific: false);
            settings.LoadFromSnapshot(settings.Editor.GetSavedSnapshot());
            IsSettingsVisible = true;
        }
    }

    /// <summary>Opens the fixed settings target for the game currently shown on the home page.</summary>
    [RelayCommand]
    private void ShowGameSettings()
    {
        if (IsSettingsVisible || settings.IsSaving || games?.IsSwitching == true)
        {
            return;
        }

        settings.BeginEdit(gameSpecific: true);
        settings.LoadFromSnapshot(settings.Editor.GetSavedSnapshot());
        IsSettingsVisible = true;
    }

    private Func<Task>? actionAfterDiscard;

    /// <summary>Resolves the current draft before entering a separate fixed-target workflow.</summary>
    public async Task RunAfterSettingsAsync(Func<Task> action)
    {
        if (settings.IsSaving)
        {
            return;
        }

        if (settings.IsSettingsDirty)
        {
            actionAfterDiscard = action;
            settings.IsUnsavedChangesVisible = true;
            return;
        }

        await settings.DiscardChangesAsync();
        IsSettingsVisible = false;
        await action();
    }

    [RelayCommand]
    private async Task DiscardSettingsChangesAsync()
    {
        var action = actionAfterDiscard;
        actionAfterDiscard = null;
        await settings.DiscardChangesAsync();
        IsSettingsVisible = false;
        if (action is not null) await action();
    }

    [RelayCommand]
    private void KeepEditingSettings()
    {
        actionAfterDiscard = null;
        settings.KeepEditing();
    }

    [RelayCommand]
    private void Minimize()
    {
        NotifyWindowMinimized();
        MinimizeRequested?.Invoke();
    }

    /// <summary>Pauses the banner carousel; the window reports native minimize state.</summary>
    public void NotifyWindowMinimized() => remoteContent.StopCarouselTimer();

    /// <summary>Resumes the banner carousel when the window returns to its normal state.</summary>
    public void NotifyWindowRestored()
    {
        if (remoteContent.HasBannerItems)
        {
            remoteContent.StartCarouselTimer();
        }
    }

    [RelayCommand]
    private void ExecuteRestoreWindow()
    {
        NotifyWindowRestored();
        RestoreRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenOfficialSite()
    {
        openExternalUrl(ResolveOfficialSiteUrl(
            settings.Editor.GetSavedSnapshot().GetGameSettings(games?.Active.GameId ?? gameProfile.RuntimeId).PatchUrlGroup,
            productProfile.CafeWebsiteUrl,
            (games?.Active.Core.Profile ?? gameProfile).OfficialWebsiteUrl));
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

    [RelayCommand]
    public void OpenExternalUrl(string? url)
    {
        openExternalUrl(url);
    }

    [RelayCommand]
    private void Close()
    {
        if (HasUninstall)
        {
            return;
        }
        if (HasDownloadJourney)
        {
            dialogs.ShowDownloadRunningCloseConfirm();
            return;
        }

        CloseRequested?.Invoke();
    }

    public Task CloseAfterStoppingDownload()
    {
        shutdownConfirmed = true;
        operations.StopOperation(GameOperationStopIntent.UserStop);
        CloseRequested?.Invoke();
        return Task.CompletedTask;
    }

    public void RequestClose()
    {
        if (!HasUninstall)
        {
            CloseRequested?.Invoke();
        }
    }

    public void RequestShutdown()
    {
        if (HasUninstall)
        {
            return;
        }
        if (HasDownloadJourney && !shutdownConfirmed)
        {
            dialogs.ShowDownloadRunningCloseConfirm();
            return;
        }
        if (fileOperations is not null && !fileOperations.TryBeginShutdown(out _))
        {
            return;
        }
        ShutdownRequested?.Invoke();
    }
}
