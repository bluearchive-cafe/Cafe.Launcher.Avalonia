using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>Describes one manifest file transfer without transport implementation details.</summary>
public sealed record FileDownloadRequest(
    string TargetTempPath,
    CdnConfigResponse CdnConfig,
    string Source,
    long ExpectedSize,
    string ExpectedHash,
    string FilePath);
