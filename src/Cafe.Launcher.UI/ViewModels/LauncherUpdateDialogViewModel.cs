using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Update;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.UI.ViewModels;

/// <summary>
/// 启动器更新横幅：呈现远端可见版本、声明本机能否应用内更新，并承载
/// 「下载 → 校验完成 → 待重启」三段状态机与发布说明的延迟加载。
/// 更新检查与文件信任判定不在其中——检查结果由 <c>LauncherUpdatePresenter</c>
/// 转译后调用 <see cref="Show"/>。
/// </summary>
internal sealed partial class LauncherUpdateDialogViewModel
    : ObservableObject, IModalContentViewModel, ILanguageAwarePresentation, IDisposable
{
    private readonly LauncherProductProfile productProfile;
    private readonly LocalizationService localizer;
    private readonly LatestRefresh releaseNotesRefresh = new();

    public LauncherUpdateDialogViewModel(
        LauncherProductProfile productProfile,
        LocalizationService localizer)
    {
        this.productProfile = productProfile;
        this.localizer = localizer;
    }

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private string version = "";

    [ObservableProperty]
    private string summaryText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReleaseNotes))]
    private string releaseNotes = "";

    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);

    [ObservableProperty]
    private bool isReleaseNotesLoading;

    [ObservableProperty]
    private bool hasReleaseNotesFailed;

    partial void OnIsVisibleChanged(bool value)
    {
        if (!value) releaseNotesRefresh.Cancel();
    }

    /// <summary>Release assets of the offered version; consumed by the in-app self-update flow.</summary>
    private IReadOnlyList<ReleaseFile> files = [];

    /// <summary>True when this host can download and apply the update in-app.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    private bool supportsInAppApply;

    /// <summary>
    /// Why the dialog offers the release page instead of an in-app update, or null when that is
    /// not the question: either in-app apply is offered, or an attempt was made and failed (that
    /// failure reports itself through its toast, and a failure is not a missing capability).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInAppUnavailable), nameof(InAppUnavailableNoticeText))]
    private LauncherUpdateInAppAvailability? unavailableReason;

    /// <summary>True when the dialog explains a capability verdict before the download starts.</summary>
    public bool IsInAppUnavailable => UnavailableReason is not null;

    /// <summary>
    /// Explains why the primary action is <see cref="LocalizationKeys.LauncherUpdateOpenReleasePage"/>,
    /// naming the cause the verdict actually reported: the platform, this installation, or the release.
    /// </summary>
    public string InAppUnavailableNoticeText => UnavailableReason switch
    {
        LauncherUpdateInAppAvailability.PlatformUnsupported =>
            localizer.T(LocalizationKeys.LauncherUpdateInAppUnavailablePlatform),
        LauncherUpdateInAppAvailability.HelperMissing =>
            localizer.T(LocalizationKeys.LauncherUpdateInAppUnavailableHelper),
        LauncherUpdateInAppAvailability.PackageUnverifiable =>
            localizer.T(LocalizationKeys.LauncherUpdateInAppUnavailablePackage),
        _ => ""
    };

    /// <summary>True while the in-app download/verify/ready flow owns the dialog.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm), nameof(PrimaryActionText))]
    private bool isApplying;

    /// <summary>True while the update package is being transferred.</summary>
    [ObservableProperty]
    private bool isDownloading;

    /// <summary>True once the package is verified and the helper can be launched.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm), nameof(PrimaryActionText))]
    private bool isReadyToRestart;

    /// <summary>Transfer completion percentage (0-100).</summary>
    [ObservableProperty]
    private double progress;

    /// <summary>Human-readable transfer rate while the update package is downloading.</summary>
    [ObservableProperty]
    private string downloadSpeedText = "";

    private LocalizationKeys? statusKey;

    /// <summary>Localized status line shown while the in-app update flow is active.</summary>
    public string StatusText => statusKey is { } key ? localizer.T(key) : "";

    /// <summary>
    /// Label of the dialog's primary action: restart-to-apply once the package is verified,
    /// the in-app download while this host can apply updates, otherwise the browser hand-off
    /// to the release page.
    /// </summary>
    public string PrimaryActionText =>
        IsReadyToRestart ? localizer.T(LocalizationKeys.LauncherUpdateRestart)
        : SupportsInAppApply ? localizer.T(LocalizationKeys.LauncherUpdateDownload)
        : localizer.T(LocalizationKeys.LauncherUpdateOpenReleasePage);

    /// <summary>Whether the dialog's primary action can run right now.</summary>
    public bool CanConfirm => IsReadyToRestart || !IsApplying;

    /// <summary>Raised when the user asks for the release page instead of an in-app update.</summary>
    public event Action<string>? OpenReleasePageRequested;

    /// <summary>Raised when the user confirms an in-app update download.</summary>
    public event Action<string, IReadOnlyList<ReleaseFile>>? SelfUpdateStartRequested;

    /// <summary>Raised when the user confirms restarting into the verified update.</summary>
    public event Action? ApplyRequested;

    /// <summary>Raised when the user cancels an in-progress in-app update.</summary>
    public event Action? CancelRequested;

    /// <summary>
    /// Shows the offered release and sets the primary action from the in-app update verdict.
    /// </summary>
    /// <param name="version">The offered release version.</param>
    /// <param name="files">The release assets used by the in-app update flow.</param>
    /// <param name="availability">Whether this installation can apply the release in-app, or why it cannot.</param>
    /// <param name="releaseNotes">The release notes displayed in the dialog.</param>
    public void Show(
        string version,
        IReadOnlyList<ReleaseFile> files,
        LauncherUpdateInAppAvailability availability,
        string releaseNotes = "")
    {
        releaseNotesRefresh.Cancel();
        Version = version;
        SummaryText = localizer.F(LocalizationKeys.LauncherUpdateAvailableMessage, version);
        ReleaseNotes = string.IsNullOrWhiteSpace(releaseNotes) ? "" : releaseNotes;
        IsReleaseNotesLoading = false;
        HasReleaseNotesFailed = !HasReleaseNotes;
        this.files = files ?? [];

        SupportsInAppApply = availability == LauncherUpdateInAppAvailability.Available;
        UnavailableReason =
            availability == LauncherUpdateInAppAvailability.Available ? null : availability;
        ResetApply();
        IsVisible = true;
    }

    /// <summary>横幅先呈现版本和动作，再加载缺失的正文；关闭或切换版本使旧结果失效。</summary>
    public Task LoadReleaseNotesAsync(ILauncherUpdateService service)
    {
        if (HasReleaseNotes || !IsVisible) return Task.CompletedTask;

        var requestedVersion = Version;
        IsReleaseNotesLoading = true;
        HasReleaseNotesFailed = false;
        releaseNotesRefresh.Run(null, async token =>
        {
            try
            {
                // 调用方在 UI 线程发起；不脱离开 awaiter 上下文，结果直接落在同一线程。
                var notes = await service.LoadReleaseNotesAsync(requestedVersion, token);
                if (token.IsCancellationRequested) return;
                ReleaseNotes = string.IsNullOrWhiteSpace(notes) ? "" : notes;
                IsReleaseNotesLoading = false;
                HasReleaseNotesFailed = !HasReleaseNotes;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Closing/replacing the dialog is not a load failure.
            }
        });
        return releaseNotesRefresh.Pending;
    }

    [RelayCommand]
    private void OpenReleasePage() =>
        OpenReleasePageRequested?.Invoke(productProfile.GitHubReleasesPageUrl);

    /// <inheritdoc cref="ILanguageAwarePresentation.RefreshLocalizedText"/>
    public void RefreshLocalizedText()
    {
        if (!IsVisible) return;

        SummaryText = localizer.F(LocalizationKeys.LauncherUpdateAvailableMessage, Version);
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(InAppUnavailableNoticeText));
    }

    /// <summary>窗口会话结束时取消仍在加载的更新说明。</summary>
    public void Dispose() => releaseNotesRefresh.Cancel();

    /// <summary>Switches the dialog into the in-app download state.</summary>
    public void BeginApply()
    {
        IsApplying = true;
        IsDownloading = true;
        IsReadyToRestart = false;
        Progress = 0;
        DownloadSpeedText = "";
        statusKey = LocalizationKeys.LauncherUpdateDownloading;
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>Reports transfer progress as a completion fraction in [0, 1] and bytes per second.</summary>
    public void ReportProgress(double fraction, long bytesPerSecond = 0)
    {
        Progress = Math.Clamp(fraction, 0d, 1d) * 100d;
        DownloadSpeedText = bytesPerSecond > 0
            ? $"{FileSizeFormatter.Format(bytesPerSecond)}/s"
            : "";
    }

    /// <summary>Shows that a verified update is ready and a restart will apply it.</summary>
    public void MarkReady()
    {
        IsDownloading = false;
        IsReadyToRestart = true;
        Progress = 100;
        DownloadSpeedText = "";
        statusKey = LocalizationKeys.LauncherUpdateReadyToRestart;
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>Returns from a failed in-app update and offers the release page as recovery.</summary>
    public void MarkFailed()
    {
        ResetApply();
        SupportsInAppApply = false;
        // 失败恢复不是能力判定：说明由失败 toast 承担，对话框不能改口归因给平台或安装。
        UnavailableReason = null;
    }

    /// <summary>Clears the in-app apply state and returns the dialog to its neutral form.</summary>
    public void ResetApply()
    {
        IsApplying = false;
        IsDownloading = false;
        IsReadyToRestart = false;
        Progress = 0;
        DownloadSpeedText = "";
        statusKey = null;
        OnPropertyChanged(nameof(StatusText));
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsApplying && !IsReadyToRestart)
        {
            CancelRequested?.Invoke();
        }

        IsVisible = false;
        // MotionVisibility keeps the surface on screen during its exit animation.
        // Preserve the preview until it disappears; Show replaces it next time.
        ResetApply();
    }

    [RelayCommand]
    private void Confirm()
    {
        if (IsReadyToRestart)
        {
            ApplyRequested?.Invoke();
            return;
        }

        if (SupportsInAppApply)
        {
            SelfUpdateStartRequested?.Invoke(Version, files);
            return;
        }

        // 本机不支持应用内更新：跳转版本发布页，由用户手动获取安装包。
        IsVisible = false;
        OpenReleasePageRequested?.Invoke(productProfile.GitHubReleasesPageUrl);
    }
}
