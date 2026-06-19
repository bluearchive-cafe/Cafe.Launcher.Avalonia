using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class ResourcePanelViewModel : ViewModelBase, IDisposable
{
    private readonly ResourcePanelUidService resourcePanelUidService;
    private readonly ResourcePanelApiClient resourcePanelApiClient;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly LocalDiagnostics diagnostics;
    private readonly CancellationTokenSource lifetimeCts = new();
    private bool disposed;

    // Delegate for proxy mode resolution (set by parent).
    public Func<string>? GetProxyMode { get; set; }

    // Delegate for patch URL group check (set by parent).
    public Func<string>? GetPatchUrlGroup { get; set; }

    /// <summary>Fired when the user tries to open the panel from a non-Cafe download source.</summary>
    public event Action? ResourcePanelSourceConfirmRequested;

    public ResourcePanelViewModel(
        ResourcePanelUidService resourcePanelUidService,
        ResourcePanelApiClient resourcePanelApiClient,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics)
    {
        this.resourcePanelUidService = resourcePanelUidService;
        this.resourcePanelApiClient = resourcePanelApiClient;
        this.localizer = localizer;
        this.toastService = toastService;
        this.diagnostics = diagnostics;
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

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenResourcePanelAsync()
    {
        var patchUrlGroup = GetPatchUrlGroup?.Invoke();
        if (!string.Equals(patchUrlGroup, PatchUrlGroups.Cafe, StringComparison.Ordinal))
        {
            ResourcePanelSourceConfirmRequested?.Invoke();
            return;
        }

        await OpenPanelDirectly();
    }

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
            await resourcePanelUidService.SaveManualUidAsync(uid, lifetimeCts.Token);
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
            await TryLogErrorAsync("Resource panel manual UID save failed.", exception);
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
            ResourcePanelMessage = localizer.F("resourcePanelUidMissing", resourcePanelUidService.CookieLibraryPath);
            return;
        }

        IsResourcePanelBusy = true;
        try
        {
            await resourcePanelApiClient.SaveConfigAsync(
                ResourcePanelUid,
                ToResourcePanelMode(GetResourcePanelItem(ResourcePanelResourceCodes.Text).IsEnabled),
                ToResourcePanelMode(GetResourcePanelItem(ResourcePanelResourceCodes.Voice).IsEnabled),
                ToResourcePanelMode(GetResourcePanelItem(ResourcePanelResourceCodes.Media).IsEnabled),
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
            await TryLogErrorAsync("Resource panel save failed.", exception);
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
            var uid = await resourcePanelUidService.ResolveUidAsync(cancellationToken);
            ResourcePanelUid = uid;
            ResourcePanelUidText = string.IsNullOrWhiteSpace(uid)
                ? ""
                : localizer.F("resourcePanelCurrentUid", uid);
            ManualResourcePanelUid = uid;
            if (string.IsNullOrWhiteSpace(uid))
            {
                IsResourcePanelUidMissing = true;
                ResourcePanelMessage = localizer.F("resourcePanelUidMissing", resourcePanelUidService.CookieLibraryPath);
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
            await TryLogErrorAsync("Resource panel load failed.", exception);
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
        resourcePanelApiClient.ProxyMode = GetProxyMode?.Invoke() ?? ProxyModes.Direct;
        var statusTask = resourcePanelApiClient.GetStatusAsync(cancellationToken);
        var configTask = resourcePanelApiClient.GetConfigAsync(uid, cancellationToken);
        await Task.WhenAll(statusTask, configTask);
        ApplyResourcePanelStatus(await statusTask);
        ApplyResourcePanelConfig(await configTask);
        ResourcePanelMessage = localizer.T("statusNetworkLoaded");
    }

    private void ApplyResourcePanelStatus(ResourcePanelStatusResponse status)
    {
        ApplyResourcePanelStatus(
            GetResourcePanelItem(ResourcePanelResourceCodes.Text), status.Text);
        ApplyResourcePanelStatus(
            GetResourcePanelItem(ResourcePanelResourceCodes.Voice), status.Voice);
        ApplyResourcePanelStatus(
            GetResourcePanelItem(ResourcePanelResourceCodes.Media), status.Media);
    }

    private void ApplyResourcePanelStatus(ResourcePanelItem item, ResourcePanelStatusGroup status)
    {
        var officialVersion = status.Official?.Version;
        var localizedVersion = status.Localized?.Version;
        item.OfficialVersion = string.IsNullOrWhiteSpace(officialVersion)
            ? "--" : officialVersion;
        item.LocalizedVersion = string.IsNullOrWhiteSpace(localizedVersion)
            ? "--" : localizedVersion;
        item.StatusText = string.Equals(item.OfficialVersion, item.LocalizedVersion, StringComparison.Ordinal)
            ? localizer.T("resourcePanelReady") : localizer.T("resourcePanelWaiting");
    }

    private void ApplyResourcePanelConfig(ResourcePanelConfigResponse config)
    {
        GetResourcePanelItem(ResourcePanelResourceCodes.Text).IsEnabled =
            config.Text == ResourcePanelResourceModes.Chinese;
        GetResourcePanelItem(ResourcePanelResourceCodes.Voice).IsEnabled =
            config.Voice == ResourcePanelResourceModes.Chinese;
        GetResourcePanelItem(ResourcePanelResourceCodes.Media).IsEnabled =
            config.Media == ResourcePanelResourceModes.Chinese;
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

    private static string ToResourcePanelMode(bool enabled)
    {
        return enabled ? ResourcePanelResourceModes.Chinese : ResourcePanelResourceModes.Japanese;
    }

    private async Task TryLogErrorAsync(string title, Exception exception)
    {
        try
        {
            await diagnostics.ErrorAsync(title, exception);
        }
        catch
        {
            // Best-effort logging
        }
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
