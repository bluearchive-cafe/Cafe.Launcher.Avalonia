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
    /// <summary>本次执行实际采用的删除范围；在确认页冻结时捕获，结果页据此回报。</summary>
    private bool includedManagedCompatibility;

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
    /// <summary>预计删除大小的数字部分（大字）；单位与「估算」由 <see cref="TotalSizeUnitText"/> 承接。</summary>
    public string TotalSizeText => footprint is { } value
        ? FileSizeFormatter.FormatParts(value.InstallDirectoryBytes + (IsCompatibilitySelected ? value.PrefixBytes : 0)).Value
        : localizer.T(LocalizationKeys.UninstallCalculating);
    /// <summary>单位 + 修饰（<c>GiB · 估算</c>）：小字呈现，修饰不该和数字抢同样的分量。</summary>
    public string TotalSizeUnitText => footprint is { } value
        ? localizer.F(LocalizationKeys.UninstallEstimatedUnit, FileSizeFormatter.FormatParts(value.InstallDirectoryBytes + (IsCompatibilitySelected ? value.PrefixBytes : 0)).Unit)
        : localizer.T(LocalizationKeys.UninstallCalculating);
    /// <summary>受管兼容环境的占用：0 是一个有意义的答案（未使用），不需要大写强调。</summary>
    public string CompatibilitySizeText => footprint is { } value
        ? FileSizeFormatter.Format(value.PrefixBytes) : localizer.T(LocalizationKeys.UninstallCalculating);
    /// <summary>实测删除文件数（大字）；量词由 <see cref="RemovedFilesUnitText"/> 承接。</summary>
    public string RemovedFilesText => localizer.F(LocalizationKeys.UninstallRemovedFilesValue, result?.AffectedFileCount ?? 0);
    public string RemovedFilesUnitText => localizer.T(LocalizationKeys.UninstallRemovedFilesUnit);
    /// <summary>实测删除字节数（大字）；单位由 <see cref="RemovedSizeUnitText"/> 承接。</summary>
    public string RemovedSizeText => FileSizeFormatter.FormatParts(result?.AffectedBytes ?? 0).Value;
    public string RemovedSizeUnitText => FileSizeFormatter.FormatParts(result?.AffectedBytes ?? 0).Unit;
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
    /// <summary>
    /// 确认页删除范围的第一句（「整个游戏目录将被删除。」）：资源串以换行切成两段，
    /// 首段加粗单独成行——破坏性操作的结论不该混在细节句子里被读过去。
    /// </summary>
    public string ScopeStatementText => ScopeDescriptionLines()[0];
    /// <summary>删除范围的细节段；资源串没有换行时为空，此时整串都归首段。</summary>
    public string ScopeDetailText => ScopeDescriptionLines() is [_, var detail, ..] ? detail : "";
    public bool HasScopeDetail => ScopeDetailText.Length > 0;
    /// <summary>结果页的「本次删除范围」：说清受管兼容环境这次是否在删除范围内。</summary>
    public string ResultScopeText => localizer.T(includedManagedCompatibility
        ? LocalizationKeys.UninstallResultScopeWithCompatibility
        : LocalizationKeys.UninstallResultScopeGameDirectory);

    /// <summary>删除范围资源串按换行切分；翻译漏了换行时整串作为首段，不丢文案。</summary>
    private string[] ScopeDescriptionLines() =>
        localizer.T(LocalizationKeys.UninstallScopeDescription)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public string DoneText => localizer.T(IsFailed ? LocalizationKeys.Close : LocalizationKeys.UninstallDone);
    // 执行中关闭钮只被禁用会让用户以为是坏掉的控件；提示说明「为什么现在不能关」。
    public string CloseToolTip => localizer.T(IsExecuting
        ? LocalizationKeys.UninstallCloseBusy
        : LocalizationKeys.Close);
    // 标题说的是「正在做什么」，不是步骤名：删除阶段用进行时串（"Deleting files"），
    // 否则大标题会退化成步骤标签（"Delete files"）。
    public string ProgressTitle => localizer.T(State switch
    {
        UninstallDialogState.Scanning => LocalizationKeys.UninstallScanStep,
        UninstallDialogState.Cleanup => LocalizationKeys.UninstallCleanupStep,
        _ => LocalizationKeys.UninstallDeletingStep
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
    // 「已完成」必须后于本步：删除阶段的扫描项已完成，而扫描阶段的清理项还没开始。
    public bool IsScanDone => IsExecuting && State > UninstallDialogState.Scanning;
    public bool IsDeleteDone => IsExecuting && State > UninstallDialogState.Deleting;

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
        includedManagedCompatibility = false;
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
        // 结果页要如实回报这次删了什么：范围在此刻冻结，后续勾选变化不得改写它。
        includedManagedCompatibility = IsCompatibilitySelected;
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
            nameof(KeptPrefixPath), nameof(HasKeptPrefix), nameof(TotalSizeText), nameof(TotalSizeUnitText),
            nameof(CompatibilitySizeText),
            nameof(RemovedFilesText), nameof(RemovedFilesUnitText), nameof(RemovedSizeText), nameof(RemovedSizeUnitText),
            nameof(Title), nameof(IconKind), nameof(Status),
            nameof(ResultMessage), nameof(LeftoversSummary), nameof(ResultScopeText), nameof(DoneText), nameof(CloseToolTip),
            nameof(ScopeStatementText), nameof(ScopeDetailText), nameof(HasScopeDetail),
            nameof(ProgressTitle), nameof(ProgressDetail),
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
