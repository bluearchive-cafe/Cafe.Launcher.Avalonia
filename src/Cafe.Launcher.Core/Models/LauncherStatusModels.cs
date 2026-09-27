using System;
using System.Collections.Generic;

namespace Cafe.Launcher.Core.Models;

/// <summary>
/// 清单校验结果：只描述「文件完整性有没有问题」，不含任何表现层类型。
/// </summary>
public sealed class ManifestValidationResult
{
    public bool Success { get; set; }

    public int DamagedFileCount { get; set; }

    public int MissingFileCount { get; set; }

    public int SizeMismatchFileCount { get; set; }

    /// <summary>
    /// Whether this result reports file-integrity damage rather than a state, path,
    /// or configuration failure. Only the manifest file scan produces a non-zero
    /// count; every other failure reports <see cref="Success"/> false with all
    /// counts left at zero.
    /// </summary>
    public bool HasDamagedFiles => DamagedFileCount > 0;

    public string Message { get; set; } = "";
}

/// <summary>
/// 一次启动尝试的结果（成功、失败原因、选中的 runner 与会话看护需要的进程家族名）。
/// </summary>
public sealed class GameLaunchResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    /// <summary>Technical details for local diagnostics, kept separate from the user-facing message.</summary>
    public string DiagnosticMessage { get; set; } = "";

    /// <summary>Original launch exception retained for local diagnostic logging only.</summary>
    public Exception? DiagnosticException { get; set; }

    public ManifestValidationResult Validation { get; set; } = new();

    /// <summary>Runner that won selection; non-null only on a successful launch that spawned a host process.</summary>
    public string? RunnerId { get; set; }

    /// <summary>
    /// 游戏进程家族名（不含扩展名）：启动成功后会话看护据此辨认「游戏真的起来了」（ADR-035）。
    /// 空表示无从辨认。
    /// </summary>
    public IReadOnlyList<string> KnownExeNames { get; set; } = [];
}

/// <summary>官方远程配置的一次读取结果。</summary>
public sealed class LauncherRemoteState
{
    public GameConfigResponse? GameConfig { get; set; }

    public BaseConfigResponse? BaseConfig { get; set; }

    public CdnConfigResponse? CdnConfig { get; set; }

    public OperationsResourceResponse? OperationsResource { get; set; }

    public SocialMediaResourceResponse? SocialMediaResource { get; set; }

    public InstallationConfigResponse? InstallationConfig { get; set; }
}

public enum LauncherRuntimeState
{
    NotInstalled,
    Corrupted,
    IoFailure,
    RemoteUnavailable,
    BelowLowestVersion,
    UpdateAvailable,
    Ready
}

/// <summary>
/// 一次状态刷新的快照：设置、本机安装状态、远程配置与推导出的运行状态。
/// 由 Core 组装、由表现层渲染，因此不含任何表现层类型。
/// </summary>
public sealed class LauncherStatusSnapshot
{
    public LauncherSettings Settings { get; set; } = new();

    public LocalInstallationState LocalGame { get; set; } = new();

    public LauncherRemoteState Remote { get; set; } = new();

    public LauncherRuntimeState RuntimeState { get; set; }

    public DateTimeOffset CheckedAt { get; set; }
}
