using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Controls;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.UI.Features.GameOperations;

internal partial class GameOperationsViewModel : IModalContentViewModel
{
    private GameOperationResult? taskResult;
    private GameOperationKind taskKind;
    private string taskPath = "";
    private long taskVersion;
    private CancellationTokenSource? installSizeCancellation;
    private long? installDownloadBytes;
    private bool taskCreateDesktopShortcut;

    /// <summary>Asks the shell to reveal the non-modal task, honoring existing draft and confirmation guards.</summary>
    public event Action? TaskSurfaceRequested;

    [ObservableProperty] private bool createInstallDesktopShortcut = true;

    public string InstallDownloadSizeText => localizer.F(LocalizationKeys.TaskInstallDownloadSize, FormatInstallSize(installDownloadBytes));
    public string InstallRequiredSizeText
    {
        get
        {
            long? required = FileSizeFormatter.TryParseHumanReadable(currentSnapshot?.Remote.GameConfig?.DecompressionSize ?? "", out var bytes)
                ? bytes : null;
            if (installDownloadBytes is { } download) required = Math.Max(required ?? 0, download);
            return localizer.F(LocalizationKeys.TaskInstallRequiredSize, FormatInstallSize(required));
        }
    }

    private static string FormatInstallSize(long? bytes) => bytes is { } value ? $"{value / (1024d * 1024 * 1024):0.00} GB" : "—";

    [ObservableProperty] private bool isTaskDialogVisible;
    [ObservableProperty] private bool isInstallPreparing;
    [ObservableProperty] private bool isTaskExecuting;
    [ObservableProperty] private bool isTaskStopping;
    [ObservableProperty] private bool isInstallPathBusy;
    [ObservableProperty] private string installPathDraft = "";

    /// <summary>Shell wires the picker without coupling game operations to settings presentation.</summary>
    public Func<string, Task<string?>>? PickInstallPathAsync { get; set; }
    /// <summary>Commits an explicitly submitted installation path through the saved-settings owner.</summary>
    public Func<string, Task<bool>>? CommitInstallPathAsync { get; set; }

    public bool HasTaskResult => taskResult is not null;
    public bool HasTask => IsTaskExecuting || HasTaskResult;
    public bool IsTaskResultVisible => HasTaskResult && !IsInstallPreparing;
    public bool IsTaskFailed => taskResult is { Success: false, ErrorCode: not GameOperationErrorCode.Stopped };
    public bool CanStopTask => IsTaskExecuting && !IsTaskStopping;
    public bool CanManageGame => !shell.IsBusy && !IsTaskExecuting && !IsUninstallExecuting;
    public bool CanRepairGame => CanManageGame && currentSnapshot is { } snapshot
        && GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.Repair, snapshot).Decision == GameOperationDecision.Allowed;
    public bool CanUninstallGame => CanManageGame && currentSnapshot is { } snapshot
        && GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.Uninstall, snapshot).Decision == GameOperationDecision.Allowed;
    public bool CanUsePrimaryAction => HasTask || CanManageGame;
    public bool CanLaunchTaskResult => IsTaskResultVisible && taskResult?.Success == true
        && currentSnapshot?.RuntimeState == LauncherRuntimeState.Ready && CanManageGame;
    public bool CanRetryTask => IsTaskResultVisible && (IsTaskFailed || taskResult?.ErrorCode == GameOperationErrorCode.Stopped) && CanManageGame
        && taskResult!.ErrorCode is not (GameOperationErrorCode.PathMissing or GameOperationErrorCode.CdnConfiguration
            or GameOperationErrorCode.RemoteConfiguration or GameOperationErrorCode.GameRunning
            or GameOperationErrorCode.InsufficientDiskSpace or GameOperationErrorCode.InvalidState);
    public bool CanStartInstall => CanCommitInstall && (!shell.IsInstallBlockedByDiskSpace
        || !GamePathValidator.PathComparer.Equals(InstallPathDraft, currentSnapshot?.LocalGame.GamePath));
    public bool CanViewTaskLog => HasTask;
    public bool CanChooseInstallPath => IsInstallPreparing && !IsInstallPathBusy && CanManageGame;
    public bool CanCommitInstall => CanChooseInstallPath && !string.IsNullOrWhiteSpace(InstallPathDraft);
    public string TaskTitle => IsInstallPreparing ? localizer.T(LocalizationKeys.InstallGame)
        : IsTaskStopping ? localizer.T(LocalizationKeys.TaskStopping)
        : taskResult is { Success: true } ? localizer.T(taskKind == GameOperationKind.Repair ? LocalizationKeys.RepairCompleted
            : taskIsInstall ? LocalizationKeys.TaskInstallCompleted : LocalizationKeys.TaskUpdateCompleted)
        : taskResult?.ErrorCode == GameOperationErrorCode.Stopped ? localizer.T(taskKind == GameOperationKind.Repair ? LocalizationKeys.TaskRepairStopped
            : taskIsInstall ? LocalizationKeys.TaskInstallStopped : LocalizationKeys.TaskUpdateStopped)
        : IsTaskFailed ? localizer.T(taskKind == GameOperationKind.Repair ? LocalizationKeys.TaskRepairFailed
            : taskIsInstall ? LocalizationKeys.TaskInstallFailed : LocalizationKeys.TaskUpdateFailed) : ProgressTitle;
    public string TaskIcon => IsInstallPreparing ? "Download" : IsTaskStopping ? "Stop" : taskResult?.Success == true ? "Check"
        : IsTaskFailed ? "AlertCircleOutline" : taskResult?.ErrorCode == GameOperationErrorCode.Stopped ? "Stop"
        : IsPaused ? "Pause" : IsTaskStalled ? "ClockOutline"
        : taskProgress?.IsCheckingDownload == true || displayedProgressStage is GameOperationStage.UpdateCheck or GameOperationStage.RepairCheck or GameOperationStage.FileCheck ? "Magnify"
        : displayedProgressStage == GameOperationStage.VerificationRetry ? "Refresh" : ProgressIconKind;
    public string TaskMessage => taskResult?.Message ?? "";
    public string TaskPath => IsInstallPreparing ? InstallPathDraft : taskPath;
    public string TaskCloseText => localizer.T(IsTaskExecuting ? LocalizationKeys.TaskHide : LocalizationKeys.Close);
    public DialogSurfaceStatus TaskStatus => IsInstallPreparing ? DialogSurfaceStatus.None
        : IsTaskFailed ? DialogSurfaceStatus.Danger
        : taskResult?.ErrorCode == GameOperationErrorCode.Stopped ? DialogSurfaceStatus.Warning : DialogSurfaceStatus.None;
    public string PrimaryActionText => IsTaskExecuting ? TaskActionText
        : CanLaunchTaskResult ? localizer.T(LocalizationKeys.StartGame)
        : HasTaskResult ? TaskTitle
        : currentSnapshot?.RuntimeState == LauncherRuntimeState.Ready ? localizer.T(LocalizationKeys.StartGame) : InstallButtonText;
    public string PrimaryActionIcon => IsTaskExecuting ? TaskIcon
        : CanLaunchTaskResult ? "PlayCircle" : HasTaskResult ? TaskIcon
        : currentSnapshot?.RuntimeState == LauncherRuntimeState.Ready ? "PlayCircle" : InstallButtonIconKind;
    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        if (CanLaunchTaskResult) { await LaunchTaskResultAsync(); return; }
        if (HasTask) { ShowTask(); return; }
        if (!CanManageGame || currentSnapshot is null) return;
        switch (currentSnapshot.RuntimeState)
        {
            case LauncherRuntimeState.Ready: await StartGameAsync(); break;
            case LauncherRuntimeState.NotInstalled: await OpenInstallPreparationAsync(); break;
            default: await InstallOrUpdateAsync(); break;
        }
    }

    /// <summary>Opening preparation leaves the previous result intact until execution is admitted.</summary>
    [RelayCommand]
    private async Task OpenInstallPreparationAsync()
    {
        if (!CanManageGame) return;
        installSizeCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        installSizeCancellation = cancellation;
        InstallPathDraft = currentSnapshot?.LocalGame.GamePath ?? "";
        CreateInstallDesktopShortcut = true;
        installDownloadBytes = null;
        IsInstallPreparing = true;
        IsTaskDialogVisible = true;
        RefreshTaskPresentation();
        try
        {
            if (currentSnapshot is not { } snapshot) return;
            var size = await executor.GetInstallDownloadSizeAsync(snapshot, cancellation.Token);
            if (cancellation.IsCancellationRequested || disposed) return;
            installDownloadBytes = size;
            RefreshTaskPresentation();
        }
        catch (OperationCanceledException) { /* Closing preparation also cancels its read-only preview. */ }
        catch (Exception) { /* Preview failure leaves an unknown size; installation performs its own checks. */ }
        finally
        {
            if (ReferenceEquals(installSizeCancellation, cancellation)) installSizeCancellation = null;
        }
    }

    [RelayCommand]
    private void ShowTask()
    {
        if (!HasTask || IsInstallPreparing || IsInstallPathBusy) return;
        IsInstallPreparing = false;
        TaskSurfaceRequested?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanCloseTask))]
    private void CloseTask()
    {
        if (!CanCloseTask()) return;
        var wasPreparing = IsInstallPreparing;
        IsTaskDialogVisible = false;
        IsInstallPreparing = false;
        if (!wasPreparing && !IsTaskExecuting) taskResult = null;
        if (!wasPreparing && !IsTaskExecuting) pendingTaskNotification = false;
        RefreshTaskPresentation();
    }

    private bool CanCloseTask() => !IsInstallPathBusy;

    [RelayCommand]
    private async Task ChooseInstallPathAsync()
    {
        if (!IsInstallPreparing || IsInstallPathBusy || !CanManageGame || PickInstallPathAsync is null) return;
        IsInstallPathBusy = true;
        try
        {
            var picked = await PickInstallPathAsync(InstallPathDraft);
            if (!string.IsNullOrWhiteSpace(picked)) InstallPathDraft = picked;
        }
        finally { IsInstallPathBusy = false; }
    }

    [RelayCommand]
    private async Task ConfirmInstallAsync() => await CommitInstallationAsync(install: true);

    [RelayCommand]
    private async Task IdentifyInstalledGameAsync() => await CommitInstallationAsync(install: false);

    private async Task CommitInstallationAsync(bool install)
    {
        if (!CanChooseInstallPath || (install && !CanStartInstall) || CommitInstallPathAsync is null) return;
        IsInstallPathBusy = true;
        try
        {
            if (!install)
            {
                if (PickInstallPathAsync is not { } picker) return;
                var picked = await picker(InstallPathDraft);
                if (string.IsNullOrWhiteSpace(picked)) return;
                InstallPathDraft = picked;
            }
            if (!await CommitInstallPathAsync(InstallPathDraft)) return;
            if (currentSnapshot is null) return;
            IsInstallPathBusy = false;
            if (install)
            {
                if (shell.IsInstallBlockedByDiskSpace) return;
                taskCreateDesktopShortcut = CreateInstallDesktopShortcut;
                await journey.InstallOrUpdateAsync(currentSnapshot, taskCreateDesktopShortcut);
            }
            else
            {
                IsTaskDialogVisible = false;
                IsInstallPreparing = false;
            }
        }
        finally { IsInstallPathBusy = false; }
    }

    [RelayCommand]
    private async Task RetryTaskAsync()
    {
        if (!CanRetryTask || currentSnapshot is null) return;
        if (!GamePathValidator.PathComparer.Equals(Path.TrimEndingDirectorySeparator(taskPath),
                Path.TrimEndingDirectorySeparator(currentSnapshot.LocalGame.GamePath)))
        {
            toastService.ShowWarning(localizer.T(LocalizationKeys.OperationUnavailableForCurrentState));
            return;
        }
        if (taskKind == GameOperationKind.Repair) await RepairAsync();
        else await journey.InstallOrUpdateAsync(currentSnapshot, taskCreateDesktopShortcut);
    }

    [RelayCommand]
    private async Task LaunchTaskResultAsync()
    {
        if (!CanLaunchTaskResult) return;
        CloseTask();
        await StartGameAsync();
    }

    [RelayCommand]
    private async Task ViewTaskLogAsync()
    {
        await AsyncEvent.InvokeSequentiallyAsync(OpenLogViewerRequested);
    }

    private void BeginTask(GameOperationKind kind, bool automatic)
    {
        taskVersion++;
        taskResult = null;
        taskKind = kind;
        taskIsInstall = kind == GameOperationKind.Download
            && (currentSnapshot is null || currentSnapshot.RuntimeState == LauncherRuntimeState.NotInstalled);
        taskPath = currentSnapshot?.LocalGame.GamePath ?? "";
        IsTaskStopping = false;
        IsInstallPreparing = false;
        IsTaskDialogVisible = false;
        pendingTaskNotification = false;
        IsTaskExecuting = true;
        StartTaskActivity();
        RefreshTaskPresentation();
    }

    private void CompleteTask(GameOperationResult result)
    {
        if (disposed || journey.IsShuttingDown) return;
        if (dialogs.StopConfirm.IsVisible) dialogs.StopConfirm.CancelCommand.Execute(null);
        taskResult = result;
        IsTaskExecuting = false;
        IsTaskStopping = false;
        CanPauseOperation = false;
        taskActivityTimer?.Stop();
        RefreshTaskPresentation();
        if (isTaskWindowPresenting && isTaskSurfaceUnobscured) return;
        if (!isTaskWindowPresenting) { pendingTaskNotification = true; return; }
        ShowTaskResultNotification();
    }

    private void ShowTaskResultNotification()
    {
        if (taskResult is not { } result) return;
        var version = taskVersion;
        toastService.Show(new ToastOptions
        {
            Title = TaskTitle,
            Message = result.Message,
            Severity = result.Success ? ToastSeverity.Success
                : result.ErrorCode == GameOperationErrorCode.Stopped ? ToastSeverity.Warning : ToastSeverity.Error,
            PrimaryAction = new ToastAction(localizer.T(LocalizationKeys.TaskViewResult), _ =>
            {
                if (version == taskVersion && HasTaskResult) ShowTask();
                return Task.FromResult(ToastActionResult.Success());
            })
        });
    }

    private void OnShellTaskStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.IsBusy) or nameof(ShellViewModel.IsInstallBlockedByDiskSpace)) RefreshTaskPresentation();
    }

    partial void OnIsTaskExecutingChanged(bool value) => RefreshTaskPresentation();
    partial void OnIsTaskStoppingChanged(bool value) => RefreshTaskPresentation();
    partial void OnIsInstallPreparingChanged(bool value)
    {
        if (!value) installSizeCancellation?.Cancel();
        RefreshTaskPresentation();
    }
    partial void OnInstallPathDraftChanged(string value) => RefreshTaskPresentation();
    partial void OnIsInstallPathBusyChanged(bool value)
    {
        CloseTaskCommand.NotifyCanExecuteChanged();
        RefreshTaskPresentation();
    }

    private void RefreshTaskProgressPresentation()
    {
        OnPropertyChanged(nameof(TaskTitle));
        OnPropertyChanged(nameof(TaskIcon));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(PrimaryActionIcon));
        RefreshFloatingTaskPresentation();
    }

    private void RefreshTaskPresentation()
    {
        RefreshFloatingTaskPresentation();
        foreach (var name in new[] { nameof(HasTask), nameof(HasTaskResult), nameof(IsTaskResultVisible), nameof(IsTaskFailed),
            nameof(CanRepairGame), nameof(CanUninstallGame), nameof(CanStopTask), nameof(CanManageGame), nameof(CanUsePrimaryAction), nameof(CanLaunchTaskResult),
            nameof(CanRetryTask), nameof(CanStartInstall), nameof(CanChooseInstallPath), nameof(CanViewTaskLog), nameof(CanCommitInstall), nameof(TaskTitle), nameof(TaskIcon), nameof(TaskMessage), nameof(TaskPath),
            nameof(InstallDownloadSizeText), nameof(InstallRequiredSizeText), nameof(TaskStatus), nameof(TaskCloseText), nameof(PrimaryActionText), nameof(PrimaryActionIcon) })
            OnPropertyChanged(name);
    }
}
