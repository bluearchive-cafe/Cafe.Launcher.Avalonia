using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class DialogsViewModel : ViewModelBase
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

    // ── Crash recovery ─────────────────────────────────────────────────

    [ObservableProperty]
    private bool isCrashRecoveryVisible;

    [ObservableProperty]
    private string crashRecoveryText = "";

    public event Action? CrashRecoveryContinueRequested;
    public event Func<Task>? CrashRecoveryResetSettingsRequested;
    public event Action? CrashRecoveryViewLogRequested;

    public void ShowCrashRecovery()
    {
        CrashRecoveryText = localizer.T("crashRecoveryMessage");
        IsCrashRecoveryVisible = true;
    }

    [RelayCommand]
    private void ContinueAfterCrash()
    {
        IsCrashRecoveryVisible = false;
        CrashRecoveryContinueRequested?.Invoke();
    }

    [RelayCommand]
    private async Task ResetSettingsAfterCrashAsync()
    {
        var handler = CrashRecoveryResetSettingsRequested;
        if (handler is not null)
            await handler();
        IsCrashRecoveryVisible = false;
    }

    [RelayCommand]
    private void ViewCrashLog()
    {
        IsCrashRecoveryVisible = false;
        CrashRecoveryViewLogRequested?.Invoke();
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

    public DialogsViewModel(LocalizationService localizer, NoticeStateService noticeStateService)
        : this(
            localizer,
            noticeStateService,
            async action => await Dispatcher.UIThread.InvokeAsync(action))
    {
    }

    internal DialogsViewModel(
        LocalizationService localizer,
        NoticeStateService noticeStateService,
        Func<Action, Task> invokeOnUiAsync)
    {
        this.localizer = localizer;
        this.noticeStateService = noticeStateService;
        this.invokeOnUiAsync = invokeOnUiAsync;
    }

    public void ApplyLanguage()
    {
        if (IsStopConfirmVisible)
        {
            StopConfirmText = localizer.T("stopDownloadConfirm");
        }

        if (IsDownloadRunningCloseConfirmVisible)
        {
            DownloadRunningCloseConfirmText = localizer.T("stopDownloadConfirm");
        }

        if (IsUpdateAvailableVisible)
        {
            UpdateAvailableText = localizer.F("updateAvailableMessage", UpdateAvailableVersion);
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
        StopConfirmText = localizer.T("stopDownloadConfirm");
        IsStopConfirmVisible = true;
    }

    public void ShowDownloadRunningCloseConfirm()
    {
        DownloadRunningCloseConfirmText = localizer.T("stopDownloadConfirm");
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
        if (ConfirmRepairRequested is not null)
        {
            await ConfirmRepairRequested.Invoke();
        }
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
        if (ConfirmUninstallRequested is not null)
        {
            await ConfirmUninstallRequested.Invoke();
        }
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
        UpdateAvailableText = localizer.F("updateAvailableMessage", version);
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
            System.Diagnostics.Debug.WriteLine(
                $"Dialogs: notice dialog load failed: {ex.Message}");
        }
    }

    private static string ComputeNoticeHash(string input)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }
}
