using System.Collections.Generic;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// The set of files to download, delete, and the manifest they are based on.
/// Shared between manifest diff computation and download execution.
/// </summary>
internal sealed class DownloadPlan
{
    public string Source { get; set; } = "";

    public List<ManifestFile> NeedDownload { get; set; } = [];

    public List<ManifestFile> NeedDelete { get; set; } = [];

    public List<ManifestFile> ManifestFiles { get; set; } = [];
}
