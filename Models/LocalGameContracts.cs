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
    public string Path { get => _path ??= ""; set => _path = value ?? ""; }
    private string? _path = "";

    [JsonPropertyName("size")]
    public string Size { get => _size ??= "0"; set => _size = value ?? "0"; }
    private string? _size = "0";

    [JsonPropertyName("hash")]
    public string Hash { get => _hash ??= ""; set => _hash = value ?? ""; }
    private string? _hash = "";

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
    public string[] Params { get => _params ??= []; set => _params = value ?? []; }
    private string[] _params = [];

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("vc")]
    public string? Vc { get; set; }
}
