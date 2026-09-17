using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.ResourcePanel;

/// <summary>
/// ViewModel for the resource panel. Owns only observable state, commands, and localization.
/// The resource panel workflow (UID resolution, parallel API reads, mode mapping, save
/// serialization) is delegated to <see cref="ResourcePanelService"/>.
/// </summary>
public partial class ResourcePanelViewModel : ViewModelBase, IDisposable, IModalContentViewModel, ILanguageAwarePresentation
{
    private readonly ResourcePanelService resourcePanelService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly IErrorHandlingService errorHandling;
    private readonly CancellationTokenSource lifetimeCts = new();
    private bool disposed;
    private string patchUrlGroup = PatchUrlGroups.Official;
    private bool isLoadingSource;
    private bool isSettingUidSource;
    private string? lastLoadedUid;
    private IReadOnlyList<bool> savedResourceBaseline = [false, false, false];

    /// <summary>Gets whether any resource switch differs from the last saved/loaded baseline.</summary>
    /// <remarks>基线与条目表按位对齐，比较塌成一次序列比较（D11）。</remarks>
    private bool HasUnsavedResourceChanges =>
        !savedResourceBaseline.SequenceEqual(ResourcePanelItems.Select(item => item.IsEnabled));

    /// <summary>Fired when the user tries to open the panel from a non-Cafe download source.</summary>
    public event Action? ResourcePanelSourceConfirmRequested;

    public ResourcePanelViewModel(
        ResourcePanelService resourcePanelService,
        LocalizationService localizer,
        ToastService toastService,
        IErrorHandlingService errorHandling)
    {
        this.resourcePanelService = resourcePanelService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.errorHandling = errorHandling;
        foreach (var item in ResourcePanelItems)
        {
            item.PropertyChanged += OnResourcePanelItemPropertyChanged;
        }

        PopulateUidSourceOptions();
        UpdateUidPresent();
    }

    [ObservableProperty]
    private bool isResourcePanelVisible;

    [ObservableProperty]
    private bool isResourcePanelBusy;

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
        IsResourcePanelVisible = true;
        await LoadResourcePanelAsync(lifetimeCts.Token);
    }

    [RelayCommand]
    private void CloseResourcePanel()
    {
        IsResourcePanelVisible = false;
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

    [RelayCommand]
    private async Task SetUidSourceAsync(string source)
    {
        IsResourcePanelBusy = true;
        try
        {
            await resourcePanelService.SaveUidSourceAsync(source, lifetimeCts.Token);
            isSettingUidSource = true;
            SelectedResourcePanelUidSource = source;
            isSettingUidSource = false;
            await LoadResourcePanelAsync(lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetResourcePanelMessage(localizer.F(LocalizationKeys.ResourcePanelLoadFailed, exception.Message), isError: true);
            await resourcePanelService.LogErrorAsync("Resource panel source switch failed.", exception);
        }
        finally
        {
            IsResourcePanelBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshResourcePanelAsync()
    {
        await LoadResourcePanelAsync(lifetimeCts.Token);
    }

    [RelayCommand]
    private async Task SaveManualResourcePanelUidAsync()
    {
        var uid = ManualResourcePanelUid.Trim();
        if (string.IsNullOrWhiteSpace(uid))
        {
            SetResourcePanelMessage(localizer.T(LocalizationKeys.ResourcePanelUidEmpty), isError: true);
            return;
        }

        if (!ResourcePanelUidService.IsValidUid(uid))
        {
            SetResourcePanelMessage(localizer.T(LocalizationKeys.ResourcePanelUidInvalidFormat), isError: true);
            return;
        }

        IsResourcePanelBusy = true;
        try
        {
            await resourcePanelService.SaveManualUidAsync(uid, lifetimeCts.Token);
            await resourcePanelService.SaveUidSourceAsync(ResourcePanelUidSources.Custom, lifetimeCts.Token);
            isSettingUidSource = true;
            SelectedResourcePanelUidSource = ResourcePanelUidSources.Custom;
            isSettingUidSource = false;
            ResourcePanelUid = uid;
            ResourcePanelUidText = localizer.F(LocalizationKeys.ResourcePanelCurrentUid, uid);
            IsResourcePanelUidMissing = false;
            IsResourcePanelUidEditing = false;
            SetResourcePanelMessage(localizer.T(LocalizationKeys.ResourcePanelUidSaved));
            await LoadResourcePanelDataAsync(uid, lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetResourcePanelMessage(localizer.F(LocalizationKeys.ResourcePanelLoadFailed, exception.Message), isError: true);
            await errorHandling.HandleErrorAsync("Resource panel manual UID save failed.", exception,
                new ErrorHandlingOptions { ShowToast = false });
        }
        finally
        {
            IsResourcePanelBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveResourcePanelAsync()
    {
        if (string.IsNullOrWhiteSpace(ResourcePanelUid))
        {
            IsResourcePanelUidMissing = true;
            SetResourcePanelMessage(localizer.F(LocalizationKeys.ResourcePanelUidMissing, resourcePanelService.CookieLibraryPath));
            return;
        }

        IsResourcePanelBusy = true;
        try
        {
            await resourcePanelService.SaveConfigAsync(
                ResourcePanelUid,
                ResourcePanelItems[0].IsEnabled,
                ResourcePanelItems[1].IsEnabled,
                ResourcePanelItems[2].IsEnabled,
                lifetimeCts.Token);
            SetResourcePanelMessage(localizer.T(LocalizationKeys.ResourcePanelSaved));
            CaptureSavedResourceBaseline();
            RefreshSaveEnabled();
            toastService.ShowSuccess(localizer.T(LocalizationKeys.ResourcePanelSaved));
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
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
            IsResourcePanelBusy = false;
        }
    }

    // ── Computed property notifications ───────────────────────────────────

    partial void OnIsResourcePanelUidMissingChanged(bool value) => UpdateUidPresent();
    partial void OnIsResourcePanelUidEditingChanged(bool value) => UpdateUidPresent();
    partial void OnSelectedResourcePanelUidSourceChanged(string value)
    {
        OnPropertyChanged(nameof(IsResourcePanelUidSourceCustom));
        if (!isLoadingSource && !isSettingUidSource)
        {
            SetUidSourceCommand.Execute(value);
        }
    }

    private void UpdateUidPresent()
    {
        IsResourcePanelUidPresent = !IsResourcePanelUidMissing && !IsResourcePanelUidEditing;
    }

    // ── Internal helpers ──────────────────────────────────────────────────

    private async Task LoadResourcePanelAsync(CancellationToken cancellationToken)
    {
        IsResourcePanelBusy = true;
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
            IsResourcePanelBusy = false;
            SetResourcePanelMessage(localizer.F(LocalizationKeys.ResourcePanelLoadFailed, exception.Message), isError: true);
            MarkItemsFailed();
            await resourcePanelService.LogErrorAsync("Resource panel load failed.", exception);
        }
        finally
        {
            if (IsResourcePanelBusy)
                IsResourcePanelBusy = false;
            RefreshSaveEnabled();
        }
    }

    private void RefreshSaveEnabled()
    {
        IsResourcePanelSaveEnabled =
            !IsResourcePanelBusy &&
            !IsResourcePanelUidMissing &&
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
    private void CaptureSavedResourceBaseline() =>
        savedResourceBaseline = ResourcePanelItems.Select(item => item.IsEnabled).ToArray();

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

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetimeCts.Cancel();
        lifetimeCts.Dispose();
    }
}
