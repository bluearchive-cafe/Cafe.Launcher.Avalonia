using System;
using System.Collections.Generic;
using System.Threading;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Update;

namespace Cafe.Launcher.UI.Features.Shell;

/// <summary>
/// 自更新的表现层终点：拥有 <see cref="ShellSelfUpdateCoordinator"/>，把它的启动、进度与
/// 终局映射到更新对话框、toast、错误管道，并在 helper 接管后请求正常关窗。
/// 协调器只描述状态机，这里才决定用户看到什么。
/// </summary>
internal sealed class ShellSelfUpdateFlow : IDisposable
{
    private readonly ShellSelfUpdateCoordinator coordinator;
    private readonly IWindowsLauncherUpdateApplier updateApplier;
    private readonly LauncherUpdateDialogViewModel updateDialog;
    private readonly ToastService toastService;
    private readonly LocalizationService localizer;
    private readonly IErrorHandlingService errorHandling;
    private readonly WindowChromeViewModel windowChrome;

    public ShellSelfUpdateFlow(
        ILauncherSelfUpdateService selfUpdateService,
        IWindowsLauncherUpdateApplier updateApplier,
        LauncherUpdateDialogViewModel updateDialog,
        ToastService toastService,
        LocalizationService localizer,
        IErrorHandlingService errorHandling,
        WindowChromeViewModel windowChrome,
        CancellationToken lifetimeToken)
    {
        this.updateApplier = updateApplier;
        this.updateDialog = updateDialog;
        this.toastService = toastService;
        this.localizer = localizer;
        this.errorHandling = errorHandling;
        this.windowChrome = windowChrome;
        coordinator = new ShellSelfUpdateCoordinator(selfUpdateService, updateApplier, lifetimeToken);
        coordinator.PreparationStarted += OnPreparationStarted;
        coordinator.ProgressReported += OnProgressReported;
        coordinator.Finished += OnFinished;
    }

    /// <summary>Starts the download-and-verify run for the release the user confirmed.</summary>
    public void Begin(string version, IReadOnlyList<ReleaseFile> files) =>
        coordinator.Begin(version, files);

    /// <summary>Hands the verified package to the helper, then exits through the normal close path.</summary>
    public void Apply()
    {
        // 与下载失败不同：没有就绪包时的“应用”是无人可应的按钮事件，保持沉默；
        // helper 拒收才是用户可见的失败。
        switch (coordinator.TryApplyPending())
        {
            case ShellSelfUpdateApplyResult.NotReady:
                return;
            case ShellSelfUpdateApplyResult.StartFailed:
                updateDialog.MarkFailed();
                toastService.ShowError(localizer.T(LocalizationKeys.LauncherUpdateApplyFailed));
                return;
        }

        // 主进程必须退出，helper 才能替换文件；走正常关窗路径保存窗口与设置。
        windowChrome.RequestShutdown();
    }

    /// <summary>Cancels the in-flight preparation; the run reports its cancellation.</summary>
    public void Cancel() => coordinator.CancelPreparation();

    /// <summary>Clears helper copies left by a previous session before the shell starts.</summary>
    public void CleanupAbandonedHelpers() => updateApplier.CleanupAbandonedHelperDirectories();

    /// <summary>Cancels the in-flight run and releases the coordinator's lifetime resources.</summary>
    public void Dispose() => coordinator.Dispose();

    private void OnPreparationStarted() => updateDialog.BeginApply();

    private void OnProgressReported(LauncherUpdateProgress progress) =>
        updateDialog.ReportProgress(progress.Fraction, progress.BytesPerSecond);

    /// <summary>Maps one coordinator outcome onto the update dialog, toast, and error pipeline.</summary>
    private void OnFinished(ShellSelfUpdateOutcome outcome, Exception? exception)
    {
        switch (outcome)
        {
            case ShellSelfUpdateOutcome.Ready:
                updateDialog.MarkReady();
                break;
            case ShellSelfUpdateOutcome.DownloadFailed:
                updateDialog.MarkFailed();
                toastService.ShowError(localizer.T(LocalizationKeys.LauncherUpdateDownloadFailed));
                break;
            case ShellSelfUpdateOutcome.DownloadBlocked:
                updateDialog.MarkFailed();
                toastService.ShowError(localizer.T(LocalizationKeys.LauncherUpdateDownloadBlocked));
                break;
            case ShellSelfUpdateOutcome.Cancelled:
                updateDialog.ResetApply();
                break;
            case ShellSelfUpdateOutcome.UnexpectedError:
                updateDialog.MarkFailed();
                _ = errorHandling.HandleErrorAsync("Launcher self-update failed.", exception!,
                    new ErrorHandlingOptions { ToastMessage = localizer.T(LocalizationKeys.LauncherUpdateDownloadFailed) });
                break;
        }
    }
}
