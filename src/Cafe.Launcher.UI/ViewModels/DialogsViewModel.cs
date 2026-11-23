using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.Diagnostics;
using Cafe.Launcher.UI.Features.SetupWizard;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.UI.ViewModels;

internal partial class DialogsViewModel : ObservableObject, IModalContentViewModel, ILanguageAwarePresentation, IDisposable
{
    private readonly LocalizationService localizer;
    private NoticeTarget noticeTarget;
    private readonly Func<Action, Task> invokeOnUiAsync;
    private readonly ILauncherDiagnostics diagnostics;
    private bool closeOnNoticeDismiss;

    /// <summary>
    /// 确认对话框家族：可见性、文案与「隐藏 → 顺序调用订阅者 → 记录失败」的
    /// 确认契约由 <see cref="ConfirmationDialogViewModel"/> 单点实现，这里只
    /// 聚合实例；新增一个确认对话框 = 一个字段加一段 AXAML。
    /// </summary>
    public ConfirmationDialogViewModel StopConfirm { get; }

    public ConfirmationDialogViewModel DownloadRunningCloseConfirm { get; }

    public ConfirmationDialogViewModel RepairConfirm { get; }

    public ConfirmationDialogViewModel ResourcePanelSourceConfirm { get; }

    public ConfirmationDialogViewModel DebugResetConfirm { get; }

    public ConfirmationDialogViewModel SettingsResetConfirm { get; }

    public ConfirmationDialogViewModel SetupWizardExitConfirm { get; }

    /// <summary>Presents the wizard exit confirmation with a message matching the consent state.</summary>
    [RelayCommand]
    private void ShowSetupWizardExitConfirm()
    {
        SetupWizardExitConfirm.Show(ResolveSetupWizardExitMessage());
    }

    private string ResolveSetupWizardExitMessage()
    {
        var key = SetupWizard.IsAgreementAccepted && !SetupWizard.IsConsentUpdateMode
            ? LocalizationKeys.SetupWizardExitMessage
            : LocalizationKeys.SetupWizardConsentExitMessage;
        return localizer.T(key);
    }

    // ── Setup wizard ─────────────────────────────────────────────────────

    public SetupWizardViewModel SetupWizard { get; }

    /// <summary>调试用设计画廊；归属对话框族以便经 ModalHost 栈管理（ADR-015）。</summary>
    public DesignGalleryViewModel Gallery { get; }

    [ObservableProperty]
    private bool isSetupWizardVisible;

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    /// <summary>向导显示前的落色机会；首启与协议复核都不执行完整初始化。</summary>
    public event Action? SetupWizardOpening;

    public void ShowSetupWizard()
    {
        // UI 线程上不能阻塞等待日志写入；TryLogAsync 自吞异常，丢弃 Task 是安全的。
        _ = diagnostics.MessageAsync("SetupWizardShow", "Setup wizard visibility requested.");
        SetupWizardOpening?.Invoke();
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

    // ── Update ────────────────────────────────────────────────────────

    /// <summary>更新横幅与形态判定；检查结果的转译留给 <see cref="LauncherUpdatePresenter"/>。</summary>
    public LauncherUpdateDialogViewModel LauncherUpdate { get; }

    // ── Notice ─────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool isNoticeDialogVisible;

    [ObservableProperty]
    private string noticeDialogContent = "";

    [ObservableProperty]
    private string noticeDialogConfirmText = "";

    public event Action? CloseRequested;

    // ── Legal documents ────────────────────────────────────────────────

    /// <summary>关于页的「开源许可」与「第三方声明」共用一个只读文档对话框。</summary>
    [ObservableProperty]
    private bool isLegalDocumentVisible;

    [ObservableProperty]
    private string legalDocumentTitle = "";

    [ObservableProperty]
    private string legalDocumentMarkdown = "";

    [RelayCommand]
    private void ShowOpenSourceLicense() =>
        ShowLegalDocument(LocalizationKeys.AboutOpenSourceLicense, AgreementDocuments.License);

    [RelayCommand]
    private void ShowThirdPartyNotices() =>
        ShowLegalDocument(LocalizationKeys.AboutThirdPartyNotices, AgreementDocuments.ThirdPartyNotices);

    [RelayCommand]
    private void CloseLegalDocument() => IsLegalDocumentVisible = false;

    private void ShowLegalDocument(LocalizationKeys titleKey, AgreementDocuments.Content content)
    {
        LegalDocumentTitle = localizer.T(titleKey);
        LegalDocumentMarkdown = content.IsAvailable
            ? content.Markdown
            : localizer.T(LocalizationKeys.AgreementUnavailable);
        IsLegalDocumentVisible = true;
    }

    /// <summary>Creates the application dialog family and its confirmation children.</summary>
    public DialogsViewModel(
        LauncherProductProfile productProfile,
        LocalizationService localizer,
        INoticeStateStore noticeStateService,
        SetupWizardViewModel setupWizard,
        ILauncherDiagnostics diagnostics)
        : this(
            productProfile,
            localizer,
            noticeStateService,
            setupWizard,
            diagnostics,
            async action => await Dispatcher.UIThread.InvokeAsync(action))
    {
    }

    /// <summary>Creates the dialog family with an injectable UI dispatcher for deterministic tests.</summary>
    internal DialogsViewModel(
        LauncherProductProfile productProfile,
        LocalizationService localizer,
        INoticeStateStore noticeStateService,
        SetupWizardViewModel setupWizard,
        ILauncherDiagnostics diagnostics,
        Func<Action, Task> invokeOnUiAsync)
    {
        this.localizer = localizer;
        noticeTarget = new NoticeTarget(null, noticeStateService);
        this.invokeOnUiAsync = invokeOnUiAsync;
        this.diagnostics = diagnostics;
        LanguageOptions = LocalizationService.GetLanguageOptions(localizer);
        SetupWizard = setupWizard;
        Gallery = new DesignGalleryViewModel(localizer.T);
        LauncherUpdate = new LauncherUpdateDialogViewModel(productProfile, localizer);
        StopConfirm = new ConfirmationDialogViewModel(diagnostics, "Stop");
        DownloadRunningCloseConfirm = new ConfirmationDialogViewModel(diagnostics, "CloseWhileDownloading");
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

        if (SetupWizardExitConfirm.IsVisible)
        {
            SetupWizardExitConfirm.Message = ResolveSetupWizardExitMessage();
        }

        LauncherUpdate.RefreshLocalizedText();
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

    /// <summary>窗口会话结束时取消更新横幅仍在加载的说明。</summary>
    public void Dispose() => LauncherUpdate.Dispose();

    [RelayCommand]
    private void DismissNotice()
    {
        IsNoticeDialogVisible = false;
        if (closeOnNoticeDismiss)
        {
            CloseRequested?.Invoke();
        }
    }

    /// <summary>绑定明确游戏的已读公告存储，并使上一目标的迟到读取或排队显示结果失效。</summary>
    public void SetNoticeTarget(LauncherGameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SetNoticeTarget(context.GameId, context.NoticeStateStore);
    }

    /// <summary>测试与明确存储装配接缝；身份始终验证为内置游戏，不据公告内容推断目标。</summary>
    internal void SetNoticeTarget(string gameId, INoticeStateStore store)
    {
        Cafe.Launcher.Core.Constants.LauncherProfiles.GetSupportedGame(gameId);
        ArgumentNullException.ThrowIfNull(store);
        var previous = Volatile.Read(ref noticeTarget);
        if (string.Equals(previous.GameId, gameId, StringComparison.Ordinal) && ReferenceEquals(previous.Store, store))
        {
            return;
        }

        Interlocked.Exchange(ref noticeTarget, new NoticeTarget(gameId, store));
        IsNoticeDialogVisible = false;
        NoticeDialogContent = "";
        closeOnNoticeDismiss = false;
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
            var target = Volatile.Read(ref noticeTarget);
            var noticeHash = ComputeNoticeHash(baseConfig.NoticeContent);
            var shownNotices = await target.Store.ReadShownNoticesAsync(cancellationToken);
            if (!ReferenceEquals(target, Volatile.Read(ref noticeTarget)) || shownNotices.Contains(noticeHash))
            {
                return;
            }

            var displayed = false;
            await invokeOnUiAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested || !ReferenceEquals(target, Volatile.Read(ref noticeTarget)))
                {
                    return;
                }

                NoticeDialogContent = baseConfig.NoticeContent;
                NoticeDialogConfirmText = baseConfig.ExitLauncherOpen
                    ? localizer.T(LocalizationKeys.NoticeExit)
                    : localizer.T(LocalizationKeys.NoticeConfirm);
                closeOnNoticeDismiss = baseConfig.ExitLauncherOpen;
                IsNoticeDialogVisible = true;
                displayed = true;
            });
            // Persist only a notice actually displayed, and always to its captured game store.
            if (displayed) await target.Store.SaveShownNoticeAsync(noticeHash, CancellationToken.None);
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

    private sealed record NoticeTarget(string? GameId, INoticeStateStore Store);

    private static string ComputeNoticeHash(string input)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }
}
