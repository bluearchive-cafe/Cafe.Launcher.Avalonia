using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Models;

public enum ResourcePanelItemStatus
{
    Loading,
    Ready,
    Waiting,
    Failed
}

public static class ResourcePanelResourceCodes
{
    public const string Text = "text";
    public const string Voice = "voice";
    public const string Media = "media";
}

public static class ResourcePanelResourceModes
{
    public const string Chinese = "cn";
    public const string Japanese = "jp";
}

public sealed class ResourcePanelStatusResponse
{
    [JsonPropertyName("text")]
    public ResourcePanelStatusGroup Text { get; set; } = new();

    [JsonPropertyName("voice")]
    public ResourcePanelStatusGroup Voice { get; set; } = new();

    [JsonPropertyName("media")]
    public ResourcePanelStatusGroup Media { get; set; } = new();
}

public sealed class ResourcePanelStatusGroup
{
    [JsonPropertyName("official")]
    public ResourcePanelVersionInfo Official { get; set; } = new();

    [JsonPropertyName("localized")]
    public ResourcePanelVersionInfo Localized { get; set; } = new();
}

public sealed class ResourcePanelVersionInfo
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public sealed class ResourcePanelConfigResponse
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("voice")]
    public string? Voice { get; set; }

    [JsonPropertyName("media")]
    public string? Media { get; set; }
}

public sealed partial class ResourcePanelItem : ObservableObject
{
    public ResourcePanelItem(string code)
    {
        Code = code;
    }

    public string Code { get; }

    [ObservableProperty]
    private string displayName = "";

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private string officialVersion = "--";

    [ObservableProperty]
    private string localizedVersion = "--";

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private ResourcePanelItemStatus status;

    public bool IsOperable => Status is ResourcePanelItemStatus.Ready or ResourcePanelItemStatus.Waiting;

    /// <summary>
    /// Gets the four per-status presentation flags the view maps to status-chip style classes
    /// (ADR-041). They are view-facing derivatives of <see cref="Status"/> — the state machine
    /// itself keeps writing the enum only.
    /// </summary>
    public bool IsStatusLoading => Status == ResourcePanelItemStatus.Loading;

    public bool IsStatusReady => Status == ResourcePanelItemStatus.Ready;

    public bool IsStatusWaiting => Status == ResourcePanelItemStatus.Waiting;

    public bool IsStatusFailed => Status == ResourcePanelItemStatus.Failed;

    partial void OnStatusChanged(ResourcePanelItemStatus value)
    {
        OnPropertyChanged(nameof(IsOperable));
        OnPropertyChanged(nameof(IsStatusLoading));
        OnPropertyChanged(nameof(IsStatusReady));
        OnPropertyChanged(nameof(IsStatusWaiting));
        OnPropertyChanged(nameof(IsStatusFailed));
    }

    [ObservableProperty]
    private string statusIconKind = "";

    /// <summary>
    /// Gets whether official and localized versions carry the same meaningful value,
    /// letting the item collapse the two-column version comparison into one row.
    /// </summary>
    public bool IsVersionAligned =>
        !string.IsNullOrWhiteSpace(OfficialVersion)
        && OfficialVersion != "--"
        && string.Equals(OfficialVersion, LocalizedVersion, StringComparison.Ordinal);

    partial void OnOfficialVersionChanged(string value) => OnPropertyChanged(nameof(IsVersionAligned));

    partial void OnLocalizedVersionChanged(string value) => OnPropertyChanged(nameof(IsVersionAligned));
}
