using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class DialogsViewModel : ViewModelBase, IModalContentViewModel
{
    private readonly LocalizationService localizer;
    private readonly NoticeStateService noticeStateService;
    private readonly Func<Action, Task> invokeOnUiAsync;
    private bool closeOnNoticeDismiss;

    [ObservableProperty]
    private bool isStopConfirmVisible;

    [ObservableProperty]
    private string stopConfirmText = "";

    [ObservableProperty]
    private bool isDownloadRunningCloseConfirmVisible;

    [ObservableProperty]
    private string downloadRunningCloseConfirmText = "";

    [ObservableProperty]
    private bool isUninstallConfirmVisible;

    [ObservableProperty]
    private string uninstallConfirmText = "";

    [ObservableProperty]
    private bool isRepairConfirmVisible;

    [ObservableProperty]
    private string repairConfirmText = "";

    [ObservableProperty]
    private bool isResourcePanelSourceConfirmVisible;

    [ObservableProperty]
    private string resourcePanelSourceConfirmText = "";

    private bool isDebugResetConfirmationVisible;

    public bool IsDebugResetConfirmationVisible
    {
        get => isDebugResetConfirmationVisible;
        set => SetProperty(ref isDebugResetConfirmationVisible, value);
    }

    public IRelayCommand CancelDebugResetCommand { get; }

    public IAsyncRelayCommand ConfirmDebugResetCommand { get; }

    public event Func<Task>? ConfirmDebugResetRequested;

    public void ShowDebugResetConfirmation()
    {
        IsDebugResetConfirmationVisible = true;
    }

    private void CancelDebugReset()
    {
        IsDebugResetConfirmationVisible = false;
    }

    private async Task ConfirmDebugResetAsync()
    {
        try
        {
            await AsyncEvent.InvokeSequentiallyAsync(ConfirmDebugResetRequested);
        }
        catch (Exception ex)
        {
            LocalDiagnostics.LogSync(
                LogEntrySeverity.Error,
                "DebugResetFailed",
                $"Failed to reset settings: {ex.Message}");
        }
        finally
        {
            IsDebugResetConfirmationVisible = false;
        }
    }

    // ── Setup wizard ─────────────────────────────────────────────────────

    public SetupWizardViewModel SetupWizard { get; }

    /// <summary>调试用设计画廊；归属对话框族以便经 ModalHost 栈管理（ADR-015）。</summary>
    public DesignGalleryViewModel Gallery { get; }

    [ObservableProperty]
    private bool isSetupWizardVisible;

    [ObservableProperty]
    private bool isSetupWizardExitConfirmVisible;

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public void ShowSetupWizard()
    {
        LocalDiagnostics.LogSync(LogEntrySeverity.Info, "SetupWizardShow", "Setup wizard visibility requested.");
        IsSetupWizardVisible = true;
    }

    [RelayCommand]
    private void RequestSetupWizardExit()
    {
        IsSetupWizardExitConfirmVisible = true;
    }

    [RelayCommand]
    private void CancelSetupWizardExit()
    {
        IsSetupWizardExitConfirmVisible = false;
    }

    [RelayCommand]
    private async Task ConfirmSetupWizardExitAsync()
    {
        IsSetupWizardExitConfirmVisible = false;
        await SetupWizard.SkipCommand.ExecuteAsync(null);
    }

    // ── Critical error ────────────────────────────────────────────────────

    [ObservableProperty]
    private bool isErrorDialogVisible;

    [ObservableProperty]
    private string errorDialogMessage = "";

    [ObservableProperty]
    private string errorDialogDetails = "";

    public event Action? ErrorViewLogRequested;
    public event Action<string>? ErrorCopyDetailsRequested;

    public void ShowCriticalError(string message, string details)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ErrorDialogMessage = message;
            ErrorDialogDetails = details;
            IsErrorDialogVisible = true;
            return;
        }

        Dispatcher.UIThread.Post(() => ShowCriticalError(message, details));
    }

    [RelayCommand]
    private void ContinueAfterError()
    {
        IsErrorDialogVisible = false;
    }

    [RelayCommand]
    private void ViewErrorLog()
    {
        IsErrorDialogVisible = false;
        ErrorViewLogRequested?.Invoke();
    }

    [RelayCommand]
    private void CopyErrorDetails()
    {
        ErrorCopyDetailsRequested?.Invoke(ErrorDialogDetails);
    }

    // ── Notice ─────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool isNoticeDialogVisible;

    [ObservableProperty]
    private string noticeDialogContent = "";

    [ObservableProperty]
    private string noticeDialogConfirmText = "";

    [ObservableProperty]
    private bool isUpdateAvailableVisible;

    [ObservableProperty]
    private string updateAvailableVersion = "";

    [ObservableProperty]
    private string updateAvailableText = "";

    [ObservableProperty]
    private ReleaseFile? selectedUpdateFile;

    public ObservableCollection<ReleaseFile> UpdateAvailableFiles { get; } = [];

    public bool HasSelectedUpdateFile => SelectedUpdateFile is not null;

    public event Func<Task>? ConfirmRepairRequested;

    public event Action? ConfirmResourcePanelSourceSwitchRequested;

    public event Func<Task>? ConfirmUninstallRequested;

    public event Action? ConfirmStopRequested;

    public event Action? CloseAfterStoppingDownloadRequested;

    public event Action? CloseRequested;

    public event Action<string>? ConfirmUpdateAvailableRequested;

    public DialogsViewModel(LocalizationService localizer, NoticeStateService noticeStateService, SetupWizardViewModel setupWizard)
        : this(
            localizer,
            noticeStateService,
            setupWizard,
            async action => await Dispatcher.UIThread.InvokeAsync(action))
    {
    }

    internal DialogsViewModel(
        LocalizationService localizer,
        NoticeStateService noticeStateService,
        SetupWizardViewModel setupWizard,
        Func<Action, Task> invokeOnUiAsync)
    {
        this.localizer = localizer;
        this.noticeStateService = noticeStateService;
        this.invokeOnUiAsync = invokeOnUiAsync;
        LanguageOptions = LocalizationService.GetLanguageOptions(localizer);
        SetupWizard = setupWizard;
        Gallery = new DesignGalleryViewModel(key => localizer.T(key));
        CancelDebugResetCommand = new RelayCommand(CancelDebugReset);
        ConfirmDebugResetCommand = new AsyncRelayCommand(ConfirmDebugResetAsync);
    }

    public void ApplyLanguage()
    {
        LanguageOptions.First(option => option.Code == LauncherLanguages.Auto).DisplayName = localizer.T("languageAuto");
        if (IsStopConfirmVisible)
        {
            StopConfirmText = localizer.T("stopDownloadMessage");
        }

        if (IsDownloadRunningCloseConfirmVisible)
        {
            DownloadRunningCloseConfirmText = localizer.T("stopDownloadMessage");
        }

        if (IsUpdateAvailableVisible)
        {
            UpdateAvailableText = localizer.F("launcherUpdateAvailableMessage", UpdateAvailableVersion);
        }
    }

    public void ShowRepairConfirm(string text)
    {
        RepairConfirmText = text;
        IsRepairConfirmVisible = true;
    }

    public void ShowUninstallConfirm(string text)
    {
        UninstallConfirmText = text;
        IsUninstallConfirmVisible = true;
    }

    public void ShowStopConfirm()
    {
        StopConfirmText = localizer.T("stopDownloadMessage");
        IsStopConfirmVisible = true;
    }

    public void ShowDownloadRunningCloseConfirm()
    {
        DownloadRunningCloseConfirmText = localizer.T("stopDownloadMessage");
        IsDownloadRunningCloseConfirmVisible = true;
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
        await AsyncEvent.InvokeSequentiallyAsync(ConfirmRepairRequested);
    }

    public void ShowResourcePanelSourceConfirm(string text)
    {
        ResourcePanelSourceConfirmText = text;
        IsResourcePanelSourceConfirmVisible = true;
    }

    [RelayCommand]
    private void CancelResourcePanelSourceSwitch()
    {
        IsResourcePanelSourceConfirmVisible = false;
    }

    [RelayCommand]
    private void ConfirmResourcePanelSourceSwitch()
    {
        IsResourcePanelSourceConfirmVisible = false;
        ConfirmResourcePanelSourceSwitchRequested?.Invoke();
    }

    [RelayCommand]
    private void CancelUninstall()
    {
        IsUninstallConfirmVisible = false;
    }

    [RelayCommand]
    private async Task ConfirmUninstallAsync()
    {
        IsUninstallConfirmVisible = false;
        await AsyncEvent.InvokeSequentiallyAsync(ConfirmUninstallRequested);
    }

    [RelayCommand]
    private void ConfirmStop()
    {
        IsStopConfirmVisible = false;
        ConfirmStopRequested?.Invoke();
    }

    [RelayCommand]
    private void CancelStop()
    {
        IsStopConfirmVisible = false;
    }

    [RelayCommand]
    private void ConfirmCloseWhileDownloading()
    {
        IsDownloadRunningCloseConfirmVisible = false;
        CloseAfterStoppingDownloadRequested?.Invoke();
    }

    [RelayCommand]
    private void CancelCloseWhileDownloading()
    {
        IsDownloadRunningCloseConfirmVisible = false;
    }

    public void ShowUpdateAvailable(string version, IReadOnlyList<ReleaseFile> files)
    {
        UpdateAvailableVersion = version;
        UpdateAvailableText = localizer.F("launcherUpdateAvailableMessage", version);
        SelectedUpdateFile = null;
        UpdateAvailableFiles.Clear();
        foreach (var file in files)
        {
            UpdateAvailableFiles.Add(file);
        }

        IsUpdateAvailableVisible = true;
    }

    [RelayCommand]
    private void CancelUpdateAvailable()
    {
        IsUpdateAvailableVisible = false;
        SelectedUpdateFile = null;
        UpdateAvailableFiles.Clear();
    }

    [RelayCommand]
    private void ConfirmUpdateAvailable()
    {
        if (SelectedUpdateFile is null)
        {
            return;
        }

        var downloadUrl = SelectedUpdateFile.Url;
        IsUpdateAvailableVisible = false;
        SelectedUpdateFile = null;
        UpdateAvailableFiles.Clear();
        ConfirmUpdateAvailableRequested?.Invoke(downloadUrl);
    }

    partial void OnSelectedUpdateFileChanged(ReleaseFile? value)
    {
        OnPropertyChanged(nameof(HasSelectedUpdateFile));
    }

    [RelayCommand]
    private void DismissNotice()
    {
        IsNoticeDialogVisible = false;
        if (closeOnNoticeDismiss)
        {
            CloseRequested?.Invoke();
        }
    }

    public async Task ShowNoticeDialogIfNeededAsync(BaseConfigResponse? baseConfig, CancellationToken cancellationToken)
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

            await invokeOnUiAsync(() =>
            {
                NoticeDialogContent = baseConfig.NoticeContent;
                NoticeDialogConfirmText = baseConfig.ExitLauncherOpen
                    ? localizer.T("noticeExit")
                    : localizer.T("noticeConfirm");
                closeOnNoticeDismiss = baseConfig.ExitLauncherOpen;
                IsNoticeDialogVisible = true;
            });
            await noticeStateService.SaveShownNoticeAsync(noticeHash, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Operation cancelled — nothing to do.
        }
        catch (Exception ex)
        {
            LocalDiagnostics.LogSync(LogEntrySeverity.Warn, "NoticeDialogLoadFailed", ex.Message);
        }
    }

    private static string ComputeNoticeHash(string input)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }
}
