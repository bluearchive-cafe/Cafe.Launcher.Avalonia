using System.Collections.Generic;

namespace Cafe.Launcher.Core.Models;

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

    /// <summary>已校验的配置或清单属于其他游戏／区服；表现层可据此解释绑定拒绝，不自行解析协议文件。</summary>
    public bool HasDifferentGameIdentity { get; init; }
}

public sealed record LocalInstallationFile(string Path, long Size, string Crc64);

public sealed record LocalInstallationStateCommit(
    string Version,
    string ManifestBasis,
    string ExecutableName,
    IReadOnlyList<string> LaunchParameters,
    IReadOnlyList<LocalInstallationFile> Files);
