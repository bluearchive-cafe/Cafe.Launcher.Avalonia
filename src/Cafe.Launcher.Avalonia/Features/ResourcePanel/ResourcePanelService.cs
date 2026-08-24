using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Features.ResourcePanel;

/// <summary>
/// Deep module that owns the resource panel workflow:
/// UID resolution, parallel remote reads, version &amp; mode mapping, save serialization.
/// The ViewModel only keeps observable state, commands, and localization.
/// </summary>
public sealed class ResourcePanelService
{
    private readonly ResourcePanelUidService uidService;
    private readonly ResourcePanelApiClient apiClient;
    private readonly LocalDiagnostics diagnostics;

    public ResourcePanelService(
        ResourcePanelUidService uidService,
        ResourcePanelApiClient apiClient,
        LocalDiagnostics diagnostics)
    {
        this.uidService = uidService;
        this.apiClient = apiClient;
        this.diagnostics = diagnostics;
    }

    /// <summary>Path to the cookie library file for localized error messages.</summary>
    public string CookieLibraryPath => uidService.CookieLibraryPath;

    /// <summary>Resolve effective UID (cookie precedence, then settings fallback).</summary>
    public async Task<string> ResolveUidAsync(CancellationToken cancellationToken = default)
    {
        return await uidService.ResolveUidAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolve effective UID with explicit source preference and fallback.</summary>
    public async Task<string> ResolveUidWithSourceAsync(
        string uidSource,
        CancellationToken cancellationToken = default)
    {
        return await uidService.ResolveUidWithSourceAsync(uidSource, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Read the persisted UID source preference.</summary>
    public async Task<string> GetUidSourceAsync(CancellationToken cancellationToken = default)
    {
        return await uidService.GetUidSourceAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Persist UID source preference to settings.</summary>
    public async Task SaveUidSourceAsync(string uidSource, CancellationToken cancellationToken = default)
    {
        var settings = await uidService.ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        settings.ResourcePanelUidSource = uidSource;
        await uidService.SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Persist a manually-entered UID to settings.</summary>
    public async Task SaveManualUidAsync(string uid, CancellationToken cancellationToken = default)
    {
        await uidService.SaveManualUidAsync(uid, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetch status + config in parallel, map API responses to ViewModel-friendly item data.
    /// Caller should resolve UID first via <see cref="ResolveUidAsync"/>.
    /// </summary>
    public async Task<ResourcePanelLoadResult> LoadDataAsync(
        string uid,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        var statusTask = apiClient.GetStatusAsync(proxyMode, cancellationToken);
        var configTask = apiClient.GetConfigAsync(uid, proxyMode, cancellationToken);

        await Task.WhenAll(statusTask, configTask).ConfigureAwait(false);

        var status = await statusTask.ConfigureAwait(false);
        var config = await configTask.ConfigureAwait(false);

        return new ResourcePanelLoadResult
        {
            Text = MapItem(status.Text, config.Text),
            Voice = MapItem(status.Voice, config.Voice),
            Media = MapItem(status.Media, config.Media),
        };
    }

    /// <summary>
    /// Save resource panel config with mode serialization (bool → cn/jp).
    /// </summary>
    public async Task SaveConfigAsync(
        string uid,
        bool textEnabled,
        bool voiceEnabled,
        bool mediaEnabled,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        await apiClient.SaveConfigAsync(
            uid,
            ToModeString(textEnabled),
            ToModeString(voiceEnabled),
            ToModeString(mediaEnabled),
            proxyMode,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Log a non-fatal resource panel error.</summary>
    public async Task LogErrorAsync(string message, Exception exception)
    {
        await diagnostics.ErrorAsync(message, exception, CancellationToken.None).ConfigureAwait(false);
    }

    // ── Private mapping helpers ──────────────────────────────────────────

    private static ResourcePanelItemData MapItem(
        ResourcePanelStatusGroup statusGroup,
        string? configMode)
    {
        var officialVersion = statusGroup.Official?.Version;
        var localizedVersion = statusGroup.Localized?.Version;
        var officialDisplay = string.IsNullOrWhiteSpace(officialVersion) ? "--" : officialVersion;
        var localizedDisplay = string.IsNullOrWhiteSpace(localizedVersion) ? "--" : localizedVersion;

        return new ResourcePanelItemData
        {
            OfficialVersion = officialDisplay,
            LocalizedVersion = localizedDisplay,
            IsEnabled = configMode == ResourcePanelResourceModes.Chinese,
            IsReady = string.Equals(officialDisplay, localizedDisplay, StringComparison.Ordinal),
        };
    }

    private static string ToModeString(bool enabled)
    {
        return enabled ? ResourcePanelResourceModes.Chinese : ResourcePanelResourceModes.Japanese;
    }
}

/// <summary>Structured result from <see cref="ResourcePanelService.LoadDataAsync"/>.</summary>
public sealed class ResourcePanelLoadResult
{
    public ResourcePanelItemData Text { get; init; } = new();
    public ResourcePanelItemData Voice { get; init; } = new();
    public ResourcePanelItemData Media { get; init; } = new();
}

/// <summary>View-friendly projection of one resource-panel resource type.</summary>
public sealed class ResourcePanelItemData
{
    public string OfficialVersion { get; init; } = "--";
    public string LocalizedVersion { get; init; } = "--";
    public bool IsEnabled { get; init; }
    public bool IsReady { get; init; }
}
