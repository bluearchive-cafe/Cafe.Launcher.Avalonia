using System;
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

    /// <summary>
    /// Hashes the planning pass computed for files it found healthy, each witnessed by the size and
    /// last-write time the file had at that moment. The install phase reuses an entry only while the
    /// file still matches its witness, which is what lets a repair session avoid reading the
    /// untouched part of an install twice without weakening the content check. Empty for the
    /// install/update plans, whose diff is size-based and therefore has no hashes to hand over.
    /// </summary>
    public Dictionary<string, PlannedFileHash> PlannedHashes { get; set; } =
        new(StringComparer.Ordinal);
}
