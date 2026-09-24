using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// Orchestrates a Windows launcher self-update up to the point of a verified,
/// staged package: select the asset for this host, fetch and parse the release's
/// SHA256SUMS, download the package and verify it, and report where it landed.
/// Applying (spawning the helper) is a separate seam the caller owns.
/// </summary>
public sealed class LauncherSelfUpdateService
{
    private readonly ILauncherUpdateDownloader downloader;
    private readonly ILauncherUpdateHostInfoProvider hostInfoProvider;
    private readonly LauncherDataRoot dataRoot;
    private readonly LocalDiagnostics diagnostics;

    public LauncherSelfUpdateService(
        ILauncherUpdateDownloader downloader,
        ILauncherUpdateHostInfoProvider hostInfoProvider,
        LauncherDataRoot dataRoot,
        LocalDiagnostics diagnostics)
    {
        this.downloader = downloader;
        this.hostInfoProvider = hostInfoProvider;
        this.dataRoot = dataRoot;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// True when this host has an in-app apply path for the given release: Windows x64
    /// with the expected package and the SHA256SUMS manifest both present.
    /// </summary>
    public bool CanApplyInApp(IReadOnlyList<ReleaseFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return LauncherUpdatePackageSelector.Select(hostInfoProvider.GetHostInfo(), files).CanApplyInApp;
    }

    /// <summary>
    /// Prepares the update described by <paramref name="files"/>. The result is
    /// <see cref="LauncherSelfUpdatePreparationStatus.ExternalDownload"/> when this
    /// host has no in-app path (non-Windows, non-x64, or a release missing a package
    /// or its checksum manifest), and <see cref="LauncherSelfUpdatePreparationStatus.Failed"/>
    /// when an in-app path existed but verification did not complete.
    /// </summary>
    public async Task<LauncherSelfUpdatePreparation> PrepareAsync(
        IReadOnlyList<ReleaseFile> files,
        string version,
        IProgress<LauncherUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        var selection = LauncherUpdatePackageSelector.Select(hostInfoProvider.GetHostInfo(), files);
        if (!selection.CanApplyInApp)
        {
            return LauncherSelfUpdatePreparation.External();
        }

        var manifestText = await downloader
            .ReadTextAsync(selection.ChecksumManifest!, cancellationToken)
            .ConfigureAwait(false);
        if (!LauncherUpdateChecksumManifest.TryParse(manifestText, out var hashes)
            || !LauncherUpdateChecksumManifest.TryGetHash(hashes, selection.Package!.Name, out var expectedSha256))
        {
            return LauncherSelfUpdatePreparation.Failed(
                $"The release checksum manifest does not cover {selection.Package!.Name}.");
        }

        var destinationPath = Path.Combine(
            dataRoot.UpdateDirectory,
            SanitizeVersionSegment(version),
            selection.Package!.Name);
        var download = await downloader
            .DownloadAndVerifyAsync(selection.Package!, expectedSha256, destinationPath, progress, cancellationToken)
            .ConfigureAwait(false);
        if (!download.IsSuccess)
        {
            await diagnostics
                .WarningAsync("LauncherUpdateDownload", download.FailureMessage, CancellationToken.None)
                .ConfigureAwait(false);
            return LauncherSelfUpdatePreparation.Failed(download.FailureMessage);
        }

        return LauncherSelfUpdatePreparation.Ready(selection.Target, download.FilePath!, expectedSha256);
    }

    /// <summary>Keeps the version segment inside one directory name on every platform.</summary>
    private static string SanitizeVersionSegment(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(version.Length);
        foreach (var character in version)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        return builder.ToString();
    }
}
