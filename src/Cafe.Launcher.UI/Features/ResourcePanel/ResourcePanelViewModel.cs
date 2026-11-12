using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Features.ResourcePanel;

/// <summary>
/// ViewModel for the resource panel. Owns only observable state, commands, and localization.
/// The resource panel workflow (UID resolution, parallel API reads, mode mapping, save
/// serialization) is delegated to <see cref="ResourcePanelService"/>.
/// </summary>
internal partial class ResourcePanelViewModel : ViewModelBase, IDisposable, IModalContentViewModel, ILanguageAwarePresentation
{
    private readonly ResourcePanelService resourcePanelService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly IErrorHandlingService errorHandling;
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly CancellationToken lifetimeToken;
    private bool disposed;
    private CancellationTokenSource? operationCts;
    private CancellationToken operationToken;
    private Task operationTask = Task.CompletedTask;
    private ResourcePanelOperationKind operationKind;
    private Func<Task>? pendingDiscardAction;
    private string patchUrlGroup = PatchUrlGroups.Official;
    private bool isLoadingSource;
    private bool isSettingUidSource;
    private string? lastLoadedUid;
    private IReadOnlyList<bool> savedResourceBaseline = [false, false, false];

    /// <summary>Gets whether any resource switch differs from the last saved/loaded baseline.</summary>
    /// <remarks>基线与条目表按位对齐，比较塌成一次序列比较（D11）。</remarks>
    private bool HasUnsavedResourceChanges =>
        !savedResourceBaseline.SequenceEqual(ResourcePanelItems.Select(item => item.IsEnabled));

    /// <summary>Gets whether resource preferences or an edited UID have not been saved.</summary>
    public bool HasUnsavedChanges => HasUnsavedResourceChanges
        || ((IsResourcePanelUidEditing || IsResourcePanelUidMissing) && ManualResourcePanelUid.Trim() != ResourcePanelUid);

    /// <summary>Protects pending edits before closing, reloading, or changing the UID.</summary>
    public ConfirmationDialogViewModel DiscardChangesConfirm { get; }

    /// <summary>Fired when the user tries to open the panel from a non-Cafe download source.</summary>
    public event Action? ResourcePanelSourceConfirmRequested;

    public ResourcePanelViewModel(
        ResourcePanelService resourcePanelService,
        LocalizationService localizer,
        ToastService toastService,
        IErrorHandlingService errorHandling,
        ILauncherDiagnostics diagnostics)
    {
        this.resourcePanelService = resourcePanelService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.errorHandling = errorHandling;
        DiscardChangesConfirm = new ConfirmationDialogViewModel(diagnostics, "Resource panel discard");
        DiscardChangesConfirm.Confirmed += ConfirmDiscardChangesAsync;
        // 在任何 await 之前取 Token：Dispose 回收源之后，迟到的命令入口仍能
        // 读到已取消的 Token，而不是抛 ObjectDisposedException。
        lifetimeToken = lifetimeCts.Token;
        foreach (var item in ResourcePanelItems)
        {
            item.PropertyChanged += OnResourcePanelItemPropertyChanged;
        }

        PopulateUidSourceOptions();
        UpdateUidPresent();
    }

    [ObservableProperty]
    private bool isResourcePanelVisible;

    /// <summary>
    /// Gets whether a load or save is running. Busy blocks the commands that would
    /// start another operation and freezes the switch card, but never blocks closing
    /// the panel: close stays the universal cancel entry.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshResourcePanelCommand), nameof(SetUidSourceCommand), nameof(SaveManualResourcePanelUidCommand))]
    private bool isResourcePanelBusy;

    private bool CanChangeResourcePanel() => !IsResourcePanelBusy;

    [ObservableProperty]
    private bool isResourcePanelSaveEnabled;

    [ObservableProperty]
    private bool isResourcePanelUidMissing;

    [ObservableProperty]
    private bool isResourcePanelUidEditing;

    private bool isResourcePanelUidPresent;
    public bool IsResourcePanelUidPresent
    {
        get => isResourcePanelUidPresent;
        private set => SetProperty(ref isResourcePanelUidPresent, value);
    }

    [ObservableProperty]
    private string resourcePanelUid = "";

    [ObservableProperty]
    private string resourcePanelUidText = "";

    [ObservableProperty]
    private string manualResourcePanelUid = "";

    [ObservableProperty]
    private string resourcePanelMessage = "";

    /// <summary>Gets whether the current inline message carries error severity (drives the strip's danger styling).</summary>
    [ObservableProperty]
    private bool isResourcePanelMessageError;

    [ObservableProperty]
    private string selectedResourcePanelUidSource = ResourcePanelUidSources.Auto;

    public bool IsResourcePanelUidSourceCustom =>
        SelectedResourcePanelUidSource == ResourcePanelUidSources.Custom;

    public ObservableCollection<SettingOption> ResourcePanelUidSourceOptions { get; } = [];

    public ObservableCollection<ResourcePanelItem> ResourcePanelItems { get; } =
    [
        new ResourcePanelItem(ResourcePanelResourceCodes.Text),
        new ResourcePanelItem(ResourcePanelResourceCodes.Voice),
        new ResourcePanelItem(ResourcePanelResourceCodes.Media)
    ];

    // ── Public API for parent VM ──────────────────────────────────────────

    /// <inheritdoc cref="ILanguageAwarePresentation.RefreshLocalizedText"/>
    /// <remarks>
    /// UID 展示文案的刷新也在这里（此前由 ShellViewModel.ApplyLanguage 代拉）：
    /// UID 是本 VM 自己的状态，它的本地化包装不该由壳层代笔。
    /// </remarks>
    public void RefreshLocalizedText()
    {
        RefreshDisplayNames();
        if (!string.IsNullOrWhiteSpace(ResourcePanelUid))
        {
            ResourcePanelUidText = localizer.F(LocalizationKeys.ResourcePanelCurrentUid, ResourcePanelUid);
        }
    }

    /// <summary>Called by parent ApplyLanguage to refresh display names.</summary>
    public void RefreshDisplayNames()
    {
        var displayNames = new[]
        {
            localizer.T(LocalizationKeys.ResourcePanelGameText),
            localizer.T(LocalizationKeys.ResourcePanelMainVoice),
            localizer.T(LocalizationKeys.ResourcePanelMedia)
        };
        for (var i = 0; i < ResourcePanelItems.Count; i++)
        {
            ResourcePanelItems[i].DisplayName = displayNames[i];
        }
        if (ResourcePanelItems.All(item => string.IsNullOrWhiteSpace(item.StatusText)))
        {
            MarkItemsLoading(preserveVersions: true);
        }

        PopulateUidSourceOptions();
    }

    private void PopulateUidSourceOptions()
    {
        var autoDisplay = localizer.T(LocalizationKeys.ResourcePanelUidSourceAuto);
        var customDisplay = localizer.T(LocalizationKeys.ResourcePanelUidSourceCustom);
        if (ResourcePanelUidSourceOptions.Count == 0)
        {
            ResourcePanelUidSourceOptions.Add(new SettingOption { Code = ResourcePanelUidSources.Auto, DisplayName = autoDisplay });
            ResourcePanelUidSourceOptions.Add(new SettingOption { Code = ResourcePanelUidSources.Custom, DisplayName = customDisplay });
        }
        else
        {
            ResourcePanelUidSourceOptions[0].DisplayName = autoDisplay;
            ResourcePanelUidSourceOptions[1].DisplayName = customDisplay;
        }
    }

    public void ApplySettings(LauncherSettings settings)
    {
        patchUrlGroup = settings.PatchUrlGroup;
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenResourcePanelAsync()
    {
        if (!string.Equals(patchUrlGroup, PatchUrlGroups.Cafe, StringComparison.Ordinal))
        {
            ResourcePanelSourceConfirmRequested?.Invoke();
            return;
        }

        await OpenPanelDirectlyAsync();
    }

    /// <summary>Open the panel directly without Cafe-source check. Called by parent after switching source.</summary>
    public async Task OpenPanelDirectlyAsync()
    {
        if (disposed)
        {
            return;
        }

        IsResourcePanelVisible = true;
        await LoadResourcePanelAsync();
    }

    /// <summary>
    /// Closes the panel at any time, including while a load or save is still running:
    /// the in-flight request is cancelled and awaited, so no leftover continuation
    /// writes state after the panel is gone. Closing during a save (or with unsaved
    /// input) keeps the existing discard confirmation — the user never loses typed
    /// input without saying so.
    /// </summary>
    [RelayCommand]
    private Task CloseResourcePanelAsync()
    {
        if (disposed)
        {
            return Task.CompletedTask;
        }

        // 保存中的关闭按「放弃保存」处理：先确认再取消，不静默丢弃用户输入。
        if (operationKind == ResourcePanelOperationKind.Save || HasUnsavedChanges)
        {
            pendingDiscardAction = ClosePanelCoreAsync;
            DiscardChangesConfirm.Show(localizer.T(LocalizationKeys.ResourcePanelUnsavedChangesMessage));
            return Task.CompletedTask;
        }

        return ClosePanelCoreAsync();
    }

    private async Task ClosePanelCoreAsync()
    {
        await CancelOperationAsync();
        if (disposed)
        {
            return;
        }

        IsResourcePanelVisible = false;
        if (operationCts is null)
        {
            // 取消后不残留「加载中/保存失败」提示：迟到的完成回调已被取消令牌挡住。
            SetResourcePanelMessage("");
        }
    }

    [RelayCommand]
    private void CancelDiscardChanges()
    {
        pendingDiscardAction = null;
        DiscardChangesConfirm.CancelCommand.Execute(null);
    }

    private Task RunAfterDiscardAsync(Func<Task> action, bool includeUid = true)
    {
        if (IsResourcePanelBusy || disposed)
            return Task.CompletedTask;

        if (HasUnsavedResourceChanges || (includeUid && HasUnsavedChanges))
        {
            pendingDiscardAction = action;
            DiscardChangesConfirm.Show(localizer.T(LocalizationKeys.ResourcePanelUnsavedChangesMessage));
            return Task.CompletedTask;
        }

        return action();
    }

    private async Task ConfirmDiscardChangesAsync()
    {
        var action = pendingDiscardAction;
        pendingDiscardAction = null;
        // 忙碌不再是拒绝理由：保存中的关闭正是靠这条路径先确认再取消。
        if (action is null || disposed)
            return;

        for (var i = 0; i < ResourcePanelItems.Count; i++)
            ResourcePanelItems[i].IsEnabled = savedResourceBaseline[i];
        ManualResourcePanelUid = ResourcePanelUid;
        IsResourcePanelUidEditing = false;
        await action();
    }

    [RelayCommand]
    private void BeginEditResourcePanelUid()
    {
        ManualResourcePanelUid = ResourcePanelUid;
        IsResourcePanelUidEditing = true;
    }

    [RelayCommand]
    private void CancelEditResourcePanelUid()
    {
        IsResourcePanelUidEditing = false;
    }

    [RelayCommand(CanExecute = nameof(CanChangeResourcePanel))]
    private Task SetUidSourceAsync(string source) =>
        RunAfterDiscardAsync(() => RunOperationAsync(
            ResourcePanelOperationKind.Load,
            cancellationToken => SetUidSourceCoreAsync(source, cancellationToken)));

    private async Task SetUidSourceCoreAsync(string source, CancellationToken cancellationToken)
    {
        try
        {
            await resourcePanelService.SaveUidSourceAsync(source, cancellationToken);
            isSettingUidSource = true;
            SelectedResourcePanelUidSource = source;
            isSettingUidSource = false;
            await LoadResourcePanelAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetResourcePanelMessage(localizer.F(LocalizationKeys.ResourcePanelLoadFailed, exception.Message), isError: true);
            await resourcePanelService.LogErrorAsync("Resource panel source switch failed.", exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeResourcePanel))]
    private Task RefreshResourcePanelAsync() =>
        RunAfterDiscardAsync(LoadResourcePanelAsync);

    [RelayCommand(CanExecute = nameof(CanChangeResourcePanel))]
    private Task SaveManualResourcePanelUidAsync()
    {
        var uid = ManualResourcePanelUid.Trim();
        if (string.IsNullOrWhiteSpace(uid))
        {
            SetResourcePanelMessage(localizer.T(LocalizationKeys.ResourcePanelUidEmpty), isError: true);
            return Task.CompletedTask;
        }

        if (!ResourcePanelUidService.IsValidUid(uid))
        {
            SetResourcePanelMessage(localizer.T(LocalizationKeys.ResourcePanelUidInvalidFormat), isError: true);
            return Task.CompletedTask;
        }

        return RunAfterDiscardAsync(
            () => RunOperationAsync(
                ResourcePanelOperationKind.Save,
                cancellationToken => SaveManualUidCoreAsync(uid, cancellationToken)),
            includeUid: false);
    }

    private async Task SaveManualUidCoreAsync(string uid, CancellationToken cancellationToken)
    {
        ManualResourcePanelUid = uid;
        IsResourcePanelUidEditing = true;
        try
        {
            await resourcePanelService.SaveManualUidAsync(uid, cancellationToken);
            await resourcePanelService.SaveUidSourceAsync(ResourcePanelUidSources.Custom, cancellationToken);
            isSettingUidSource = true;
            SelectedResourcePanelUidSource = ResourcePanelUidSources.Custom;
            isSettingUidSource = false;
            ResourcePanelUid = uid;
            ResourcePanelUidText = localizer.F(LocalizationKeys.ResourcePanelCurrentUid, uid);
            IsResourcePanelUidMissing = false;
            IsResourcePanelUidEditing = false;
            SetResourcePanelMessage(localizer.T(LocalizationKeys.ResourcePanelUidSaved));
            await LoadResourcePanelDataAsync(uid, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetResourcePanelMessage(localizer.F(LocalizationKeys.ResourcePanelLoadFailed, exception.Message), isError: true);
            await errorHandling.HandleErrorAsync("Resource panel manual UID save failed.", exception,
                new ErrorHandlingOptions { ShowToast = false });
        }
    }

    [RelayCommand]
    private Task SaveResourcePanelAsync()
    {
        if (string.IsNullOrWhiteSpace(ResourcePanelUid))
        {
            IsResourcePanelUidMissing = true;
            SetResourcePanelMessage(localizer.F(LocalizationKeys.ResourcePanelUidMissing, resourcePanelService.CookieLibraryPath));
            return Task.CompletedTask;
        }

        return RunOperationAsync(ResourcePanelOperationKind.Save, SaveConfigCoreAsync);
    }

    private async Task SaveConfigCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await resourcePanelService.SaveConfigAsync(
                ResourcePanelUid,
                ResourcePanelItems[0].IsEnabled,
                ResourcePanelItems[1].IsEnabled,
                ResourcePanelItems[2].IsEnabled,
                cancellationToken);
            SetResourcePanelMessage(localizer.T(LocalizationKeys.ResourcePanelSaved));
            CaptureSavedResourceBaseline();
            RefreshSaveEnabled();
            toastService.ShowSuccess(localizer.T(LocalizationKeys.ResourcePanelSaved));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 关闭放弃保存：基线保持原值，用户确认放弃后已恢复草稿。
        }
        catch (Exception exception)
        {
            var message = localizer.F(LocalizationKeys.ResourcePanelSaveFailed, exception.Message);
            SetResourcePanelMessage(message, isError: true);
            await errorHandling.HandleErrorAsync("Resource panel save failed.", exception,
                new ErrorHandlingOptions { ToastMessage = message });
        }
        finally
        {
            RefreshSaveEnabled();
        }
    }

    // ── Computed property notifications ───────────────────────────────────

    partial void OnIsResourcePanelBusyChanged(bool value) => RefreshSaveEnabled();

    partial void OnIsResourcePanelUidMissingChanged(bool value)
    {
        UpdateUidPresent();
        RefreshSaveEnabled();
    }
    partial void OnIsResourcePanelUidEditingChanged(bool value)
    {
        UpdateUidPresent();
        RefreshSaveEnabled();
    }
    partial void OnManualResourcePanelUidChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnSelectedResourcePanelUidSourceChanged(string? oldValue, string newValue)
    {
        OnPropertyChanged(nameof(IsResourcePanelUidSourceCustom));
        if (!isLoadingSource && !isSettingUidSource)
        {
            // Keep the applied source selected until discard and persistence succeed.
            isSettingUidSource = true;
            SelectedResourcePanelUidSource = oldValue ?? ResourcePanelUidSources.Auto;
            isSettingUidSource = false;
            SetUidSourceCommand.Execute(newValue);
        }
    }

    private void UpdateUidPresent()
    {
        IsResourcePanelUidPresent = !IsResourcePanelUidMissing && !IsResourcePanelUidEditing;
    }

    // ── Internal helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Runs one user-visible load/save under its own cancellation source so the close
    /// path can cancel and await it. Nested calls (a source switch or a manual-UID save
    /// that reloads) join the running operation instead of opening a second scope, so
    /// one close cancels the whole chain. The source is disposed with the operation:
    /// repeated open/close cycles never accumulate token sources.
    /// </summary>
    private Task RunOperationAsync(ResourcePanelOperationKind kind, Func<CancellationToken, Task> body)
    {
        if (disposed || lifetimeToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var running = operationCts;
        if (running is { IsCancellationRequested: false })
        {
            return body(operationToken);
        }

        var cts = new CancellationTokenSource();
        operationCts = cts;
        operationToken = cts.Token;
        operationKind = kind;
        IsResourcePanelBusy = true;
        var task = CompleteOperationAsync(cts, body);
        operationTask = task;
        return task;
    }

    private async Task CompleteOperationAsync(CancellationTokenSource cts, Func<CancellationToken, Task> body)
    {
        try
        {
            await body(cts.Token);
        }
        finally
        {
            // 只有仍持有状态的操作才复位：关闭后立即重开时，新操作已接管 Busy。
            if (ReferenceEquals(operationCts, cts))
            {
                operationCts = null;
                operationToken = default;
                operationKind = ResourcePanelOperationKind.None;
                operationTask = Task.CompletedTask;
                IsResourcePanelBusy = false;
            }

            cts.Dispose();
        }
    }

    /// <summary>Cancels the running operation and waits for its state reset, so no continuation stays pending.</summary>
    private async Task CancelOperationAsync()
    {
        var task = operationTask;
        operationCts?.Cancel();
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task LoadResourcePanelAsync() =>
        RunOperationAsync(ResourcePanelOperationKind.Load, LoadResourcePanelCoreAsync);

    private async Task LoadResourcePanelCoreAsync(CancellationToken cancellationToken)
    {
        IsResourcePanelUidEditing = false;
        SetResourcePanelMessage(localizer.T(LocalizationKeys.ResourcePanelLoading));
        MarkItemsLoading(preserveVersions: true);
        try
        {
            try
            {
                isLoadingSource = true;
                var uidSource = await resourcePanelService.GetUidSourceAsync(cancellationToken);
                SelectedResourcePanelUidSource = uidSource;
            }
            finally
            {
                isLoadingSource = false;
            }

            var uid = await resourcePanelService.ResolveUidWithSourceAsync(
                SelectedResourcePanelUidSource, cancellationToken);
            ResourcePanelUid = uid;
            ResourcePanelUidText = string.IsNullOrWhiteSpace(uid)
                ? ""
                : localizer.F(LocalizationKeys.ResourcePanelCurrentUid, uid);
            ManualResourcePanelUid = uid;
            if (string.IsNullOrWhiteSpace(uid))
            {
                IsResourcePanelUidMissing = true;
                SetResourcePanelMessage(localizer.F(LocalizationKeys.ResourcePanelUidMissing, resourcePanelService.CookieLibraryPath));
                MarkItemsFailed();
                return;
            }

            IsResourcePanelUidMissing = false;
            await LoadResourcePanelDataAsync(uid, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetResourcePanelMessage(localizer.F(LocalizationKeys.ResourcePanelLoadFailed, exception.Message), isError: true);
            MarkItemsFailed();
            await resourcePanelService.LogErrorAsync("Resource panel load failed.", exception);
        }
        finally
        {
            RefreshSaveEnabled();
        }
    }

    private void RefreshSaveEnabled()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        IsResourcePanelSaveEnabled =
            !IsResourcePanelBusy &&
            !IsResourcePanelUidMissing &&
            !IsResourcePanelUidEditing &&
            HasUnsavedResourceChanges &&
            ResourcePanelItems.Count > 0 &&
            ResourcePanelItems.All(i => i is { Status: ResourcePanelItemStatus.Ready or ResourcePanelItemStatus.Waiting });
    }

    private void OnResourcePanelItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResourcePanelItem.IsEnabled))
        {
            RefreshSaveEnabled();
        }
    }

    /// <summary>Freezes the current switch state as the "nothing to save" baseline.</summary>
    private void CaptureSavedResourceBaseline()
    {
        savedResourceBaseline = ResourcePanelItems.Select(item => item.IsEnabled).ToArray();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private async Task LoadResourcePanelDataAsync(string uid, CancellationToken cancellationToken)
    {
        // 版本归属判定：同一 UID 的重载保留旧值防刷新闪烁；换 UID 时旧数据作废归零。
        var preserveVersions = string.Equals(lastLoadedUid, uid, StringComparison.Ordinal);
        MarkItemsLoading(preserveVersions);
        SetResourcePanelMessage(localizer.T(LocalizationKeys.ResourcePanelLoading));
        var result = await resourcePanelService.LoadDataAsync(uid, cancellationToken);
        lastLoadedUid = uid;
        ApplyResult(result);
        CaptureSavedResourceBaseline();
        SetResourcePanelMessage(localizer.T(LocalizationKeys.StatusNetworkLoaded));
    }

    private void ApplyResult(ResourcePanelLoadResult result)
    {
        // 装载结果与条目表按位对齐（D11）：不再按 code 各自查找。
        for (var i = 0; i < ResourcePanelItems.Count; i++)
        {
            ApplyItem(ResourcePanelItems[i], result[i]);
        }
    }

    private void ApplyItem(ResourcePanelItem item, ResourcePanelItemData data)
    {
        item.OfficialVersion = data.OfficialVersion;
        item.LocalizedVersion = data.LocalizedVersion;
        item.IsEnabled = data.IsEnabled;
        if (data.IsReady)
        {
            SetState(item, ResourcePanelItemStatus.Ready, "CheckCircle", localizer.T(LocalizationKeys.ResourcePanelReady));
        }
        else
        {
            SetState(item, ResourcePanelItemStatus.Waiting, "ClockOutline", localizer.T(LocalizationKeys.ResourcePanelWaiting));
        }
    }

    /// <summary>条目状态的三字段（枚举/图标/文案）由这一处统一写（D11）。</summary>
    private static void SetState(ResourcePanelItem item, ResourcePanelItemStatus status, string iconKind, string statusText)
    {
        item.Status = status;
        item.StatusIconKind = iconKind;
        item.StatusText = statusText;
    }

    /// <summary>Marks every item loading; already loaded versions stay visible when preserved to avoid refresh flicker.</summary>
    private void MarkItemsLoading(bool preserveVersions)
    {
        foreach (var item in ResourcePanelItems)
        {
            SetState(item, ResourcePanelItemStatus.Loading, "Sync", localizer.T(LocalizationKeys.ResourcePanelLoading));
            if (!preserveVersions)
            {
                item.OfficialVersion = "--";
                item.LocalizedVersion = "--";
            }
        }
    }

    /// <summary>Marks every item failed while keeping the last good data visible for diagnosis.</summary>
    private void MarkItemsFailed()
    {
        foreach (var item in ResourcePanelItems)
        {
            SetState(item, ResourcePanelItemStatus.Failed, "AlertCircle", localizer.T(LocalizationKeys.ResourcePanelFailed));
        }
    }

    /// <summary>Sets the inline message and its severity together so the strip styling never drifts from the text.</summary>
    private void SetResourcePanelMessage(string message, bool isError = false)
    {
        ResourcePanelMessage = message;
        IsResourcePanelMessageError = isError;
    }

    private enum ResourcePanelOperationKind
    {
        None,
        Load,
        Save
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        operationCts?.Cancel();
        pendingDiscardAction = null;
        DiscardChangesConfirm.Confirmed -= ConfirmDiscardChangesAsync;
        lifetimeCts.Cancel();
        foreach (var item in ResourcePanelItems)
        {
            item.PropertyChanged -= OnResourcePanelItemPropertyChanged;
        }

        lifetimeCts.Dispose();
    }
}
