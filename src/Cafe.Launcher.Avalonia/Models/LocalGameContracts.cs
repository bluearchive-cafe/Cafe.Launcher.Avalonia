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
    // Property declaration order defines the serialized JSON key order, which MUST match the
    // official manifest (path, hash, size, vc). The vc integrity hash is computed over the
    // values in this exact order (see OfficialHashService.GetManifestFileHash); keeping the
    // serialization order identical lets the official and rewritten launchers read each
    // other's manifest.json without flagging it corrupted.
    [JsonPropertyName("path")]
    public string Path { get => path ??= ""; set => path = value ?? ""; }
    private string? path = "";

    [JsonPropertyName("hash")]
    public string Hash { get => hash ??= ""; set => hash = value ?? ""; }
    private string? hash = "";

    [JsonPropertyName("size")]
    public string Size { get => size ??= "0"; set => size = value ?? "0"; }
    private string? size = "0";

    /// <summary>
    /// Size as a parsed long value. Returns 0 for non-parseable input.
    /// Centralises the <see cref="Helpers.FileSizeFormatter.ParseSize"/> call
    /// so consumers don't need to parse <see cref="Size"/> individually.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public long SizeBytes => long.TryParse(Size, out var s) ? s : 0;

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
