using System.Collections.Generic;

namespace Cafe.Launcher.Avalonia.Models;

public enum LocalInstallationStateKind
{
    NotInstalled,
    Valid,
    Corrupted,
    IoFailure
}

public sealed class LocalInstallationState
{
    public LocalInstallationStateKind Kind { get; init; }

    public string GamePath { get; init; } = "";

    public string ConfigPath { get; init; } = "";

    public string ManifestPath { get; init; } = "";

    public GameLauncherConfig? GameConfig { get; init; }

    public LocalManifest? Manifest { get; init; }

    public string? Error { get; init; }
}

public sealed record LocalInstallationFile(string Path, long Size, ulong Crc64);

public sealed record LocalInstallationStateCommit(
    string Version,
    string ManifestBasis,
    string ExecutableName,
    IReadOnlyList<string> LaunchParameters,
    IReadOnlyList<LocalInstallationFile> Files);
