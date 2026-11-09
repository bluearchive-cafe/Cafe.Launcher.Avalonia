using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.Core.Models;

/// <summary>
/// Release information returned by the launcher update server proxy.
/// </summary>
public sealed class LauncherReleaseResponse
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("files")]
    public List<ReleaseFile> Files { get; set; } = [];

    [JsonPropertyName("releaseDate")]
    public DateTime? ReleaseDate { get; set; }

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = "";
}

/// <summary>
/// A downloadable file entry within a release.
/// </summary>
public sealed class ReleaseFile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>
    /// 可选的镜像下载地址；仅在通过产品档案的基址与版本路径校验后才会被下载器优先使用。
    /// </summary>
    [JsonPropertyName("mirrorUrl")]
    public string? MirrorUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonIgnore]
    public string DisplaySize => FileSizeFormatter.Format(Size);
}
