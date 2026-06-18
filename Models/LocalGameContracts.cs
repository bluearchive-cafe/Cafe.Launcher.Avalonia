using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cafe.Launcher.Avalonia.Models;

public sealed class LocalManifest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("basis")]
    public string? Basis { get; set; }

    [JsonPropertyName("vc")]
    public string? Vc { get; set; }

    [JsonPropertyName("files")]
    public List<ManifestFile> Files { get; set; } = [];
}

public sealed class RemoteManifest
{
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("file")]
    public List<ManifestFile> File { get; set; } = [];
}

public sealed class ManifestFile
{
    [JsonPropertyName("path")]
    public string Path { get => path ??= ""; set => path = value ?? ""; }
    private string? path = "";

    [JsonPropertyName("size")]
    public string Size { get => size ??= "0"; set => size = value ?? "0"; }
    private string? size = "0";

    [JsonPropertyName("hash")]
    public string Hash { get => hash ??= ""; set => hash = value ?? ""; }
    private string? hash = "";

    [JsonPropertyName("vc")]
    public string? Vc { get; set; }
}

public sealed class GameLauncherConfig
{
    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("params")]
    public string[] Params { get => parameters ??= []; set => parameters = value ?? []; }
    private string[] parameters = [];

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("vc")]
    public string? Vc { get; set; }
}
