using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the resource panel. Owns only observable state, commands, and localization.
/// The resource panel workflow (UID resolution, parallel API reads, mode mapping, save
/// serialization) is delegated to <see cref="ResourcePanelService"/>.
/// </summary>
public partial class ResourcePanelViewModel : ViewModelBase, IDisposable, IModalContentViewModel
{
    private readonly ResourcePanelService resourcePanelService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly CancellationTokenSource lifetimeCts = new();
    private bool disposed;
    private string proxyMode = ProxyModes.Auto;
    private string patchUrlGroup = PatchUrlGroups.Official;
    private bool isLoadingSource;
    private bool isSettingUidSource;

    /// <summary>Fired when the user tries to open the panel from a non-Cafe download source.</summary>
    public event Action? ResourcePanelSourceConfirmRequested;

    public ResourcePanelViewModel(
        ResourcePanelService resourcePanelService,
        LocalizationService localizer,
        ToastService toastService)
    {
        this.resourcePanelService = resourcePanelService;
        this.localizer = localizer;
        this.toastService = toastService;
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

    /// <summary>Called by parent ApplyLanguage to refresh display names.</summary>
    public void RefreshDisplayNames()
    {
        GetResourcePanelItem(ResourcePanelResourceCodes.Text).DisplayName = localizer.T("resourcePanelGameText");
        GetResourcePanelItem(ResourcePanelResourceCodes.Voice).DisplayName = localizer.T("resourcePanelMainVoice");
        GetResourcePanelItem(ResourcePanelResourceCodes.Media).DisplayName = localizer.T("resourcePanelMedia");
        if (ResourcePanelItems.All(item => string.IsNullOrWhiteSpace(item.StatusText)))
        {
            SetResourcePanelStatusText(localizer.T("resourcePanelLoading"));
        }

        PopulateUidSourceOptions();
    }

    private void PopulateUidSourceOptions()
    {
        var autoDisplay = localizer.T("resourcePanelUidSourceAuto");
        var customDisplay = localizer.T("resourcePanelUidSourceCustom");
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
        proxyMode = settings.ProxyMode;
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

        await OpenPanelDirectly();
    }

    /// <summary>Open the panel directly without Cafe-source check. Called by parent after switching source.</summary>
    public async Task OpenPanelDirectly()
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
            ResourcePanelMessage = localizer.F("resourcePanelLoadFailed", exception.Message);
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
            ResourcePanelMessage = localizer.T("resourcePanelUidEmpty");
            return;
        }

        if (!ResourcePanelUidService.IsValidUid(uid))
        {
            ResourcePanelMessage = localizer.T("resourcePanelUidInvalidFormat");
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
            ResourcePanelUidText = localizer.F("resourcePanelCurrentUid", uid);
            IsResourcePanelUidMissing = false;
            IsResourcePanelUidEditing = false;
            ResourcePanelMessage = localizer.T("resourcePanelUidSaved");
            await LoadResourcePanelDataAsync(uid, lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ResourcePanelMessage = localizer.F("resourcePanelLoadFailed", exception.Message);
            await resourcePanelService.LogErrorAsync("Resource panel manual UID save failed.", exception);
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
            ResourcePanelMessage = localizer.F("resourcePanelUidMissing", resourcePanelService.CookieLibraryPath);
            return;
        }

        IsResourcePanelBusy = true;
        try
        {
            await resourcePanelService.SaveConfigAsync(
                ResourcePanelUid,
                GetResourcePanelItem(ResourcePanelResourceCodes.Text).IsEnabled,
                GetResourcePanelItem(ResourcePanelResourceCodes.Voice).IsEnabled,
                GetResourcePanelItem(ResourcePanelResourceCodes.Media).IsEnabled,
                proxyMode,
                lifetimeCts.Token);
            ResourcePanelMessage = localizer.T("resourcePanelSaved");
            toastService.ShowSuccess(localizer.T("resourcePanelSaved"));
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            var message = localizer.F("resourcePanelSaveFailed", exception.Message);
            ResourcePanelMessage = message;
            toastService.ShowError(message);
            await resourcePanelService.LogErrorAsync("Resource panel save failed.", exception);
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
        ResourcePanelMessage = localizer.T("resourcePanelLoading");
        SetResourcePanelStatusText(localizer.T("resourcePanelLoading"));
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
                : localizer.F("resourcePanelCurrentUid", uid);
            ManualResourcePanelUid = uid;
            if (string.IsNullOrWhiteSpace(uid))
            {
                IsResourcePanelUidMissing = true;
                ResourcePanelMessage = localizer.F("resourcePanelUidMissing", resourcePanelService.CookieLibraryPath);
                SetResourcePanelStatusText(localizer.T("resourcePanelFailed"));
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
            ResourcePanelMessage = localizer.F("resourcePanelLoadFailed", exception.Message);
            SetResourcePanelStatusText(localizer.T("resourcePanelFailed"));
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
            ResourcePanelItems.Count > 0 &&
            ResourcePanelItems.All(i => i is { Status: ResourcePanelItemStatus.Ready or ResourcePanelItemStatus.Waiting });
    }

    private async Task LoadResourcePanelDataAsync(string uid, CancellationToken cancellationToken)
    {
        ResourcePanelMessage = localizer.T("resourcePanelLoading");
        SetResourcePanelStatusText(localizer.T("resourcePanelLoading"));
        var result = await resourcePanelService.LoadDataAsync(uid, proxyMode, cancellationToken);
        ApplyResult(result);
        ResourcePanelMessage = localizer.T("statusNetworkLoaded");
    }

    private void ApplyResult(ResourcePanelLoadResult result)
    {
        ApplyItem(GetResourcePanelItem(ResourcePanelResourceCodes.Text), result.Text);
        ApplyItem(GetResourcePanelItem(ResourcePanelResourceCodes.Voice), result.Voice);
        ApplyItem(GetResourcePanelItem(ResourcePanelResourceCodes.Media), result.Media);
    }

    private void ApplyItem(ResourcePanelItem item, ResourcePanelItemData data)
    {
        item.OfficialVersion = data.OfficialVersion;
        item.LocalizedVersion = data.LocalizedVersion;
        item.IsEnabled = data.IsEnabled;
        if (data.IsReady)
        {
            item.Status = ResourcePanelItemStatus.Ready;
            item.StatusIconKind = "CheckCircle";
            item.StatusText = localizer.T("resourcePanelReady");
        }
        else
        {
            item.Status = ResourcePanelItemStatus.Waiting;
            item.StatusIconKind = "ClockOutline";
            item.StatusText = localizer.T("resourcePanelWaiting");
        }
    }

    private void SetResourcePanelStatusText(string statusText)
    {
        foreach (var item in ResourcePanelItems)
        {
            item.StatusText = statusText;
            item.OfficialVersion = "--";
            item.LocalizedVersion = "--";
            item.Status = IsResourcePanelBusy
                ? ResourcePanelItemStatus.Loading
                : ResourcePanelItemStatus.Failed;
            item.StatusIconKind = IsResourcePanelBusy ? "Sync" : "AlertCircle";
        }
    }

    private ResourcePanelItem GetResourcePanelItem(string code)
    {
        return ResourcePanelItems.FirstOrDefault(item => item.Code == code)
            ?? throw new InvalidOperationException($"Resource panel item not found: {code}");
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
