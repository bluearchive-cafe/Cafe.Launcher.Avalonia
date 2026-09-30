using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Controls;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>
/// 由游戏操作拥有的完整卸载表面。一次打开对应一个统计版本，确认后保留同一快照与模态。
/// 所有方法由 UI 线程调用；进度的线程切换在 GameOperationsViewModel 的入口完成。
/// </summary>
internal sealed partial class UninstallDialogViewModel : ViewModelBase, IModalContentViewModel, ILanguageAwarePresentation
{
    private readonly LocalizationService localizer;
    private long generation;
    private UninstallFootprint? footprint;
    private GameOperationResult? result;
    private int processedEntries;
    private int totalEntries;

    public UninstallDialogViewModel(LocalizationService localizer, Func<Task> confirm, Func<Task> showLogs)
    {
        this.localizer = localizer;
        ConfirmCommand = new AsyncRelayCommand(confirm, () => IsVisible && IsConfirmation);
        CloseCommand = new RelayCommand(Close, () => !IsExecuting);
        ViewLogsCommand = new AsyncRelayCommand(async () =>
        {
            Close();
            await showLogs();
        }, () => IsResult && (HasLeftovers || IsFailed));
    }

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private UninstallDialogState state;

    [ObservableProperty]
    private bool isCompatibilitySelected;

    [ObservableProperty]
    private int progressValue;

    public IAsyncRelayCommand ConfirmCommand { get; }
    public IRelayCommand CloseCommand { get; }
    public IAsyncRelayCommand ViewLogsCommand { get; }
    public LauncherStatusSnapshot? Snapshot { get; private set; }
    public string GamePath => Snapshot?.LocalGame.GamePath ?? "";
    public bool IsConfirmation => State == UninstallDialogState.Confirmation;
    public bool IsExecuting => State is UninstallDialogState.Scanning or UninstallDialogState.Deleting or UninstallDialogState.Cleanup;
    public bool IsResult => State is UninstallDialogState.Completed or UninstallDialogState.Failed;
    public bool IsFailed => State == UninstallDialogState.Failed;
    public bool IsIndeterminate => State == UninstallDialogState.Scanning;
    public bool HasLeftovers => Leftovers.Count > 0;
    public bool HasLogAction => IsResult && (HasLeftovers || IsFailed);
    public bool IsCleanCompletion => State == UninstallDialogState.Completed && !HasLeftovers;
    public bool HasMetrics => IsResult && result?.Success == true;
    public IReadOnlyList<string> Leftovers => result?.UninstallDetails?.Leftovers ?? [];
    public string? KeptPrefixPath => IsResult ? result?.UninstallDetails?.KeptPrefixPath
        : IsCompatibilitySelected ? footprint?.KeptPrefixWithCompatibilityPath : footprint?.KeptPrefixPath;
    public bool HasKeptPrefix => !string.IsNullOrWhiteSpace(KeptPrefixPath);
    public string TotalSizeText => footprint is { } value
        ? localizer.F(LocalizationKeys.UninstallEstimatedSizeValue,
            FileSizeFormatter.Format(value.InstallDirectoryBytes + (IsCompatibilitySelected ? value.PrefixBytes : 0)))
        : localizer.T(LocalizationKeys.UninstallCalculating);
    public string CompatibilitySizeText => footprint is { } value
        ? FileSizeFormatter.Format(value.PrefixBytes) : localizer.T(LocalizationKeys.UninstallCalculating);
    public string RemovedFilesText => localizer.F(LocalizationKeys.UninstallRemovedFilesValue, result?.AffectedFileCount ?? 0);
    public string RemovedSizeText => FileSizeFormatter.Format(result?.AffectedBytes ?? 0);
    public string Title => localizer.T(IsFailed ? LocalizationKeys.UninstallFailureTitle
        : HasLeftovers ? LocalizationKeys.UninstallLeftoversTitle
        : IsResult ? LocalizationKeys.UninstallCompletedTitle
        : IsExecuting ? LocalizationKeys.Uninstalling : LocalizationKeys.UninstallGame);
    public string IconKind => IsFailed ? "AlertCircleOutline" : HasLeftovers ? "AlertOutline"
        : IsResult ? "Check" : "DeleteOutline";
    public DialogSurfaceStatus Status => IsFailed || IsConfirmation ? DialogSurfaceStatus.Danger
        : HasLeftovers ? DialogSurfaceStatus.Warning : DialogSurfaceStatus.None;
    public string ResultMessage => IsFailed ? result?.Message ?? ""
        : localizer.T(HasLeftovers ? LocalizationKeys.UninstallLeftoversDescription : LocalizationKeys.UninstallSuccessDescription);
    public string LeftoversSummary => localizer.F(LocalizationKeys.UninstallLeftoversCount, Leftovers.Count);
    public string DoneText => localizer.T(IsFailed ? LocalizationKeys.Close : LocalizationKeys.UninstallDone);
    public string ProgressTitle => localizer.T(State switch
    {
        UninstallDialogState.Scanning => LocalizationKeys.UninstallScanStep,
        UninstallDialogState.Cleanup => LocalizationKeys.UninstallCleanupStep,
        _ => LocalizationKeys.UninstallDeleteStep
    });
    public string ProgressDetail => State switch
    {
        UninstallDialogState.Scanning => localizer.T(LocalizationKeys.UninstallScanning),
        UninstallDialogState.Cleanup => localizer.T(LocalizationKeys.UninstallCleanupDescription),
        _ => localizer.F(LocalizationKeys.UninstallProgress, processedEntries, totalEntries)
    };
    public bool IsScanActive => State == UninstallDialogState.Scanning;
    public bool IsDeleteActive => State == UninstallDialogState.Deleting;
    public bool IsCleanupActive => State == UninstallDialogState.Cleanup;
    public bool IsScanDone => IsExecuting && !IsScanActive;
    public bool IsDeleteDone => IsCleanupActive;

    /// <summary>打开确认页并使任何旧统计回调失效。</summary>
    public long Open(LauncherStatusSnapshot snapshot)
    {
        if (IsExecuting)
        {
            return generation;
        }
        generation++;
        Snapshot = snapshot;
        footprint = null;
        result = null;
        IsCompatibilitySelected = false;
        ProgressValue = 0;
        State = UninstallDialogState.Confirmation;
        IsVisible = true;
        RefreshLocalizedText();
        return generation;
    }

    /// <summary>只有当前确认页允许接受实测大小；不影响已经确认的执行范围。</summary>
    public void ApplyFootprint(long version, UninstallFootprint value)
    {
        if (generation != version || !IsVisible || !IsConfirmation)
        {
            return;
        }
        footprint = value;
        RefreshLocalizedText();
    }

    /// <summary>冻结当前确认页；重复调用不会再次发起卸载。</summary>
    public bool BeginExecution()
    {
        if (!IsVisible || !IsConfirmation || Snapshot is null)
        {
            return false;
        }
        generation++;
        State = UninstallDialogState.Scanning;
        ProgressValue = 0;
        return true;
    }

    public void ApplyProgress(GameOperationProgress progress)
    {
        if (!IsExecuting)
        {
            return;
        }
        var nextState = progress.Stage switch
        {
            GameOperationStage.UninstallScanning => UninstallDialogState.Scanning,
            GameOperationStage.UninstallCleanup => UninstallDialogState.Cleanup,
            _ => UninstallDialogState.Deleting
        };
        if (nextState < State)
        {
            return;
        }
        State = nextState;
        ProgressValue = Math.Max(ProgressValue, Math.Clamp(progress.Progress, 0, 100));
        processedEntries = progress.ProcessedEntryCount;
        totalEntries = progress.TotalEntryCount;
        OnPropertyChanged(nameof(ProgressDetail));
    }

    /// <summary>终态不改变可见性；结果由用户主动关闭。</summary>
    public void Complete(GameOperationResult value)
    {
        result = value;
        State = value.Success ? UninstallDialogState.Completed : UninstallDialogState.Failed;
        RefreshLocalizedText();
    }

    private void Close()
    {
        if (IsExecuting)
        {
            return;
        }
        generation++;
        IsVisible = false;
        Snapshot = null;
    }

    partial void OnStateChanged(UninstallDialogState value) => RefreshLocalizedText();
    partial void OnIsVisibleChanged(bool value) => ConfirmCommand.NotifyCanExecuteChanged();
    partial void OnIsCompatibilitySelectedChanged(bool value) => RefreshLocalizedText();

    public void RefreshLocalizedText()
    {
        foreach (string property in new[]
        {
            nameof(GamePath), nameof(IsConfirmation), nameof(IsExecuting), nameof(IsResult), nameof(IsFailed),
            nameof(IsIndeterminate), nameof(HasLeftovers), nameof(HasLogAction), nameof(IsCleanCompletion), nameof(HasMetrics), nameof(Leftovers),
            nameof(KeptPrefixPath), nameof(HasKeptPrefix), nameof(TotalSizeText), nameof(CompatibilitySizeText),
            nameof(RemovedFilesText), nameof(RemovedSizeText), nameof(Title), nameof(IconKind), nameof(Status),
            nameof(ResultMessage), nameof(LeftoversSummary), nameof(DoneText), nameof(ProgressTitle), nameof(ProgressDetail),
            nameof(IsScanActive), nameof(IsDeleteActive), nameof(IsCleanupActive), nameof(IsScanDone), nameof(IsDeleteDone)
        })
        {
            OnPropertyChanged(property);
        }
        ConfirmCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
        ViewLogsCommand.NotifyCanExecuteChanged();
    }
}
