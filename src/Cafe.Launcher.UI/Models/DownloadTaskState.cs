using System.Text.Json.Serialization;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Models;

/// <summary>
/// Serializable state of an in-progress game download for resume after restart.
/// Mirrors the original Electron launcher's localStorage "download-task" key.
/// </summary>
internal sealed class DownloadTaskState
{
    /// <summary>显式身份检查点使用版本 2；零仅表示旧文档未声明版本。</summary>
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; }

    /// <summary>创建任务时固定的稳定游戏身份；旧文档缺失时必须另行验证归属。</summary>
    [JsonPropertyName("gameId")]
    public string GameId { get; set; } = "";

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
