using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the resource panel. Owns only observable state, commands, and localization.
/// The resource panel workflow (UID resolution, parallel API reads, mode mapping, save
/// serialization) is delegated to <see cref="ResourcePanelService"/>.
/// </summary>
public partial class ResourcePanelViewModel : ViewModelBase, IDisposable
{
    private readonly ResourcePanelService resourcePanelService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly CancellationTokenSource lifetimeCts = new();
    private bool disposed;
    private string proxyMode = ProxyModes.Direct;
    private string patchUrlGroup = PatchUrlGroups.Official;

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
    }

    [ObservableProperty]
    private bool isResourcePanelVisible;

    [ObservableProperty]
    private bool isResourcePanelBusy;

    [ObservableProperty]
    private bool isResourcePanelUidMissing;

    [ObservableProperty]
    private string resourcePanelUid = "";

    [ObservableProperty]
    private string resourcePanelUidText = "";

    [ObservableProperty]
    private string manualResourcePanelUid = "";

    [ObservableProperty]
    private string resourcePanelMessage = "";

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

        IsResourcePanelBusy = true;
        try
        {
            await resourcePanelService.SaveManualUidAsync(uid, lifetimeCts.Token);
            ResourcePanelUid = uid;
            ResourcePanelUidText = localizer.F("resourcePanelCurrentUid", uid);
            IsResourcePanelUidMissing = false;
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

    // ── Internal helpers ──────────────────────────────────────────────────

    private async Task LoadResourcePanelAsync(CancellationToken cancellationToken)
    {
        IsResourcePanelBusy = true;
        ResourcePanelMessage = localizer.T("resourcePanelLoading");
        SetResourcePanelStatusText(localizer.T("resourcePanelLoading"));
        try
        {
            var uid = await resourcePanelService.ResolveUidAsync(cancellationToken);
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
            ResourcePanelMessage = localizer.F("resourcePanelLoadFailed", exception.Message);
            SetResourcePanelStatusText(localizer.T("resourcePanelFailed"));
            await resourcePanelService.LogErrorAsync("Resource panel load failed.", exception);
        }
        finally
        {
            IsResourcePanelBusy = false;
        }
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
        item.StatusText = data.IsReady
            ? localizer.T("resourcePanelReady")
            : localizer.T("resourcePanelWaiting");
    }

    private void SetResourcePanelStatusText(string statusText)
    {
        foreach (var item in ResourcePanelItems)
        {
            item.StatusText = statusText;
            item.OfficialVersion = "--";
            item.LocalizedVersion = "--";
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
