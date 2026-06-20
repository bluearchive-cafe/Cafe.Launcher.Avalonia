using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Models;

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

    [JsonPropertyName("sha512")]
    public string Sha512 { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonIgnore]
    public string DisplaySize => FileSizeFormatter.Format(Size);
}
