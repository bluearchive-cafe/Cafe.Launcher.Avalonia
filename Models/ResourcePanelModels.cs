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

    [ObservableProperty]
    private string statusIconKind = "";
}
