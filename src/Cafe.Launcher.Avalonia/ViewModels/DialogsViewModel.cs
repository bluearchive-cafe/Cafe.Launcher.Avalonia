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
        // UI 线程上不能阻塞等待日志写入；LogAsync 自吞异常，丢弃 Task 是安全的。
        _ = LocalDiagnostics.LogAsync(LogEntrySeverity.Info, "SetupWizardShow", "Setup wizard visibility requested.");
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

    public event Action? CloseRequested;

    public event Action<string>? ConfirmUpdateAvailableRequested;

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

    public void ShowUpdateAvailable(string version, IReadOnlyList<ReleaseFile> files)
    {
        UpdateAvailableVersion = version;
        UpdateAvailableText = localizer.F(LocalizationKeys.LauncherUpdateAvailableMessage, version);
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
            await LocalDiagnostics.LogAsync(LogEntrySeverity.Warn, "NoticeDialogLoadFailed", ex.Message);
        }
    }

    private static string ComputeNoticeHash(string input)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }
}
