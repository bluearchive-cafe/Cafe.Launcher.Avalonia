using System.Text.Json.Serialization;

namespace Cafe.Launcher.Avalonia.Models;

/// <summary>
/// Serializable state of an in-progress game download for resume after restart.
/// Mirrors the original Electron launcher's localStorage "download-task" key.
/// </summary>
public sealed class DownloadTaskState
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("basis")]
    public string Basis { get; set; } = "";

    [JsonPropertyName("path")]
    public string GamePath { get; set; } = "";

    [JsonPropertyName("repair")]
    public bool IsRepair { get; set; }

    [JsonPropertyName("patchUrlGroup")]
    public string PatchUrlGroup { get; set; } = PatchUrlGroups.Official;

    [JsonPropertyName("startedAt")]
    public string StartedAt { get; set; } = "";
}
