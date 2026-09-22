using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class DialogsViewModel : ViewModelBase, IModalContentViewModel, ILanguageAwarePresentation
{
    private readonly LocalizationService localizer;
    private readonly NoticeStateService noticeStateService;
    private readonly Func<Action, Task> invokeOnUiAsync;
    private readonly LocalDiagnostics diagnostics;
    private bool closeOnNoticeDismiss;

    /// <summary>
    /// 确认对话框家族：可见性、文案与「隐藏 → 顺序调用订阅者 → 记录失败」的
    /// 确认契约由 <see cref="ConfirmationDialogViewModel"/> 单点实现，这里只
    /// 聚合实例；新增一个确认对话框 = 一个字段加一段 AXAML。
    /// </summary>
    public ConfirmationDialogViewModel StopConfirm { get; }

    public ConfirmationDialogViewModel DownloadRunningCloseConfirm { get; }

    public ConfirmationDialogViewModel UninstallConfirm { get; }

    public ConfirmationDialogViewModel RepairConfirm { get; }

    public ConfirmationDialogViewModel ResourcePanelSourceConfirm { get; }

    public ConfirmationDialogViewModel DebugResetConfirm { get; }

    public ConfirmationDialogViewModel SettingsResetConfirm { get; }

    public ConfirmationDialogViewModel SetupWizardExitConfirm { get; }

    // ── Setup wizard ─────────────────────────────────────────────────────

    public SetupWizardViewModel SetupWizard { get; }

    /// <summary>调试用设计画廊；归属对话框族以便经 ModalHost 栈管理（ADR-015）。</summary>
    public DesignGalleryViewModel Gallery { get; }

    [ObservableProperty]
    private bool isSetupWizardVisible;

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public void ShowSetupWizard()
    {
        // UI 线程上不能阻塞等待日志写入；TryLogAsync 自吞异常，丢弃 Task 是安全的。
        _ = diagnostics.MessageAsync("SetupWizardShow", "Setup wizard visibility requested.");
        IsSetupWizardVisible = true;
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

    /// <summary>True when this host can download and apply the update in-app.</summary>
    [ObservableProperty]
    private bool updateSupportsInAppApply;

    /// <summary>True while the in-app download/verify/ready flow owns the dialog.</summary>
    [ObservableProperty]
    private bool isUpdateApplying;

    /// <summary>True while the update package is being transferred.</summary>
    [ObservableProperty]
    private bool isUpdateDownloading;

    /// <summary>True once the package is verified and the helper can be launched.</summary>
    [ObservableProperty]
    private bool isUpdateReadyToRestart;

    /// <summary>Transfer completion percentage (0-100).</summary>
    [ObservableProperty]
    private double updateProgress;

    private string updateStatusKey = "";

    /// <summary>Localized status line shown while the in-app update flow is active.</summary>
    public string UpdateStatusText => updateStatusKey.Length == 0 ? "" : localizer.T(updateStatusKey);

    /// <summary>Whether the dialog's primary action can run right now.</summary>
    public bool CanConfirmUpdate =>
        IsUpdateReadyToRestart
        || (UpdateSupportsInAppApply && !IsUpdateApplying)
        || HasSelectedUpdateFile;

    public event Action? CloseRequested;

    public event Action<string>? ConfirmUpdateAvailableRequested;

    /// <summary>Raised when the user confirms an in-app update download.</summary>
    public event Action<string, IReadOnlyList<ReleaseFile>>? SelfUpdateStartRequested;

    /// <summary>Raised when the user confirms restarting into the verified update.</summary>
    public event Action? ApplyUpdateRequested;

    /// <summary>Raised when the user cancels an in-progress in-app update.</summary>
    public event Action? CancelUpdateRequested;

    /// <summary>Creates the application dialog family and its confirmation children.</summary>
    public DialogsViewModel(
        LocalizationService localizer,
        NoticeStateService noticeStateService,
        SetupWizardViewModel setupWizard,
        LocalDiagnostics diagnostics)
        : this(
            localizer,
            noticeStateService,
            setupWizard,
            diagnostics,
            async action => await Dispatcher.UIThread.InvokeAsync(action))
    {
    }

    /// <summary>Creates the dialog family with an injectable UI dispatcher for deterministic tests.</summary>
    internal DialogsViewModel(
        LocalizationService localizer,
        NoticeStateService noticeStateService,
        SetupWizardViewModel setupWizard,
        LocalDiagnostics diagnostics,
        Func<Action, Task> invokeOnUiAsync)
    {
        this.localizer = localizer;
        this.noticeStateService = noticeStateService;
        this.invokeOnUiAsync = invokeOnUiAsync;
        this.diagnostics = diagnostics;
        LanguageOptions = LocalizationService.GetLanguageOptions(localizer);
        SetupWizard = setupWizard;
        Gallery = new DesignGalleryViewModel(key => localizer.T(key));
        StopConfirm = new ConfirmationDialogViewModel(diagnostics, "Stop");
        DownloadRunningCloseConfirm = new ConfirmationDialogViewModel(diagnostics, "CloseWhileDownloading");
        UninstallConfirm = new ConfirmationDialogViewModel(diagnostics, "Uninstall");
        RepairConfirm = new ConfirmationDialogViewModel(diagnostics, "Repair");
        ResourcePanelSourceConfirm = new ConfirmationDialogViewModel(diagnostics, "ResourceSourceSwitch");
        DebugResetConfirm = new ConfirmationDialogViewModel(diagnostics, "DebugReset");
        SettingsResetConfirm = new ConfirmationDialogViewModel(diagnostics, "SettingsReset");
        SetupWizardExitConfirm = new ConfirmationDialogViewModel(diagnostics, "SetupWizardExit");
        SetupWizardExitConfirm.Confirmed += () => SetupWizard.SkipCommand.ExecuteAsync(null);
    }

    public void RefreshLocalizedText()
    {
        LanguageOptions.First(option => option.Code == LauncherLanguages.Auto).DisplayName = localizer.T(LocalizationKeys.LanguageAuto);
        if (StopConfirm.IsVisible)
        {
            StopConfirm.Message = localizer.T(LocalizationKeys.StopDownloadMessage);
        }

        if (DownloadRunningCloseConfirm.IsVisible)
        {
            DownloadRunningCloseConfirm.Message = localizer.T(LocalizationKeys.CloseDownloadMessage);
        }

        if (IsUpdateAvailableVisible)
        {
            UpdateAvailableText = localizer.F(LocalizationKeys.LauncherUpdateAvailableMessage, UpdateAvailableVersion);
            OnPropertyChanged(nameof(UpdateStatusText));
        }
        SetupWizard.RefreshLocalizedText();
    }

    /// <summary>Presents the stop-download confirmation with the localized stop message.</summary>
    public void ShowStopConfirm()
    {
        StopConfirm.Show(localizer.T(LocalizationKeys.StopDownloadMessage));
    }

    /// <summary>Presents the close-while-downloading confirmation with the localized stop message.</summary>
    public void ShowDownloadRunningCloseConfirm()
    {
        DownloadRunningCloseConfirm.Show(localizer.T(LocalizationKeys.CloseDownloadMessage));
    }

    public void ShowUpdateAvailable(string version, IReadOnlyList<ReleaseFile> files, bool canSelfUpdate)
    {
        UpdateAvailableVersion = version;
        UpdateAvailableText = localizer.F(LocalizationKeys.LauncherUpdateAvailableMessage, version);
        SelectedUpdateFile = null;
        UpdateAvailableFiles.Clear();
        foreach (var file in files)
        {
            UpdateAvailableFiles.Add(file);
        }

        UpdateSupportsInAppApply = canSelfUpdate;
        ResetUpdateApply();
        IsUpdateAvailableVisible = true;
    }

    /// <summary>Switches the dialog into the in-app download state.</summary>
    public void BeginUpdateApply()
    {
        IsUpdateApplying = true;
        IsUpdateDownloading = true;
        IsUpdateReadyToRestart = false;
        UpdateProgress = 0;
        updateStatusKey = LocalizationKeys.LauncherUpdateDownloading;
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(CanConfirmUpdate));
    }

    /// <summary>Reports transfer progress as a completion fraction in [0, 1].</summary>
    public void ReportUpdateProgress(double fraction)
    {
        UpdateProgress = Math.Clamp(fraction, 0d, 1d) * 100d;
    }

    /// <summary>Shows that a verified update is ready and a restart will apply it.</summary>
    public void MarkUpdateReady()
    {
        IsUpdateDownloading = false;
        IsUpdateReadyToRestart = true;
        UpdateProgress = 100;
        updateStatusKey = LocalizationKeys.LauncherUpdateReadyToRestart;
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(CanConfirmUpdate));
    }

    /// <summary>Clears the in-app apply state and returns the dialog to its neutral form.</summary>
    public void ResetUpdateApply()
    {
        IsUpdateApplying = false;
        IsUpdateDownloading = false;
        IsUpdateReadyToRestart = false;
        UpdateProgress = 0;
        updateStatusKey = "";
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(CanConfirmUpdate));
    }

    [RelayCommand]
    private void CancelUpdateAvailable()
    {
        if (IsUpdateApplying && !IsUpdateReadyToRestart)
        {
            CancelUpdateRequested?.Invoke();
        }

        IsUpdateAvailableVisible = false;
        SelectedUpdateFile = null;
        UpdateAvailableFiles.Clear();
        ResetUpdateApply();
    }

    [RelayCommand]
    private void ConfirmUpdateAvailable()
    {
        if (IsUpdateReadyToRestart)
        {
            ApplyUpdateRequested?.Invoke();
            return;
        }

        if (UpdateSupportsInAppApply)
        {
            SelfUpdateStartRequested?.Invoke(UpdateAvailableVersion, UpdateAvailableFiles.ToArray());
            return;
        }

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
        OnPropertyChanged(nameof(CanConfirmUpdate));
    }

    partial void OnIsUpdateApplyingChanged(bool value) => OnPropertyChanged(nameof(CanConfirmUpdate));

    partial void OnUpdateSupportsInAppApplyChanged(bool value) => OnPropertyChanged(nameof(CanConfirmUpdate));

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
                    ? localizer.T(LocalizationKeys.NoticeExit)
                    : localizer.T(LocalizationKeys.NoticeConfirm);
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
            await diagnostics.WarningAsync("NoticeDialogLoadFailed", ex.Message, CancellationToken.None);
        }
    }

    private static string ComputeNoticeHash(string input)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }
}
