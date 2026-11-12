using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.Diagnostics;

namespace Cafe.Launcher.Core.Services.Update;

/// <summary>
/// Orchestrates a Windows launcher self-update up to the point of a verified,
/// staged package: select the asset for this host, fetch and parse the release's
/// SHA256SUMS, download the package and verify it, and report where it landed.
/// Applying (spawning the helper) is a separate seam the caller owns.
/// </summary>
internal sealed class LauncherSelfUpdateService : ILauncherSelfUpdateService
{
    private readonly ILauncherUpdateDownloader downloader;
    private readonly ILauncherUpdateHostInfoProvider hostInfoProvider;
    private readonly IWindowsLauncherUpdateApplier updateApplier;
    private readonly LauncherDataRoot dataRoot;
    private readonly ILauncherDiagnostics diagnostics;

    /// <summary>Creates the coordinator that checks, verifies, and stages launcher update packages.</summary>
    /// <param name="downloader">Reads the checksum manifest and downloads update packages.</param>
    /// <param name="hostInfoProvider">Describes the current platform and launcher installation.</param>
    /// <param name="updateApplier">Reports whether the local helper is available for applying a verified package.</param>
    /// <param name="dataRoot">Provides the application-owned directory for staged update files.</param>
    /// <param name="diagnostics">Records recoverable update download failures.</param>
    public LauncherSelfUpdateService(
        ILauncherUpdateDownloader downloader,
        ILauncherUpdateHostInfoProvider hostInfoProvider,
        IWindowsLauncherUpdateApplier updateApplier,
        LauncherDataRoot dataRoot,
        ILauncherDiagnostics diagnostics)
    {
        this.downloader = downloader;
        this.hostInfoProvider = hostInfoProvider;
        this.updateApplier = updateApplier;
        this.dataRoot = dataRoot;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// The single availability verdict for the given release: the release must offer a verifiable
    /// package for this host <em>and</em> this installation must carry the helper that applies it.
    /// The dialog's pre-check and the download gate ask the same question here, so both agree, and
    /// a negative answer carries the cause the user is told about.
    /// </summary>
    public LauncherUpdateInAppAvailability ResolveInAppAvailability(IReadOnlyList<ReleaseFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return AvailabilityOf(LauncherUpdatePackageSelector.Select(hostInfoProvider.GetHostInfo(), files));
    }

    /// <summary>
    /// Prepares the update described by <paramref name="files"/>. The result is
    /// <see cref="LauncherSelfUpdatePreparationStatus.ExternalDownload"/> when this
    /// host has no in-app path (non-Windows, non-x64, a release missing a package
    /// or its checksum manifest, or a missing update helper), and
    /// <see cref="LauncherSelfUpdatePreparationStatus.Failed"/>
    /// when an in-app path existed but the package could not be staged safely or
    /// verification did not complete.
    /// </summary>
    public async Task<LauncherSelfUpdatePreparation> PrepareAsync(
        IReadOnlyList<ReleaseFile> files,
        string version,
        IProgress<LauncherUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        var selection = LauncherUpdatePackageSelector.Select(hostInfoProvider.GetHostInfo(), files);
        if (!CanStartDownload(selection))
        {
            return LauncherSelfUpdatePreparation.External();
        }

        // 纵深防御：元数据接受处已拒绝越界包名，但本服务也可以被直接调用，而包名最终就是
        // 数据根下的落点文件名。名字不合规就没有可下载的对象——先于任何网络请求失败。
        if (!LauncherUpdatePackageSelector.IsSingleFileNameSegment(selection.Package!.Name))
        {
            return FailPreparation(
                $"The release package name is not a single file name: {selection.Package!.Name}",
                LauncherSelfUpdateFailureKind.Verification);
        }

        // 版本段已被收窄为单个目录名、包名刚被限制为单段；拼出的绝对路径仍要断言落在
        // UpdateDirectory 之内，这样今后任一段被放开也不会让落点越出数据根。
        var destinationPath = TryResolveStagingPath(
            dataRoot.UpdateDirectory,
            version,
            selection.Package!.Name);
        if (destinationPath is null)
        {
            return FailPreparation(
                $"The update package would be staged outside {dataRoot.UpdateDirectory}: "
                + selection.Package!.Name,
                LauncherSelfUpdateFailureKind.Verification);
        }

        string? manifestText;
        try
        {
            manifestText = await downloader
                .ReadTextAsync(selection.ChecksumManifest!, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsManifestReadFailure(exception))
        {
            // 清单抓取失败以前会静默变成「发布包缺少校验值」，日志里不留任何痕迹。
            // 现在按传输层异常分流：被拒地址（hosts/DNS 指向本机）与一般网络失败分开，
            // 两者都落 WARN，表现层才能给出针对性指引。
            var kind = RemoteHttpRequestService.IsBlockedAddressRejection(exception)
                ? LauncherSelfUpdateFailureKind.UrlRejected
                : LauncherSelfUpdateFailureKind.Network;
            return FailPreparation(
                $"The release checksum manifest could not be fetched: {exception.Message}",
                kind);
        }

        if (manifestText is null)
        {
            return FailPreparation(
                $"The release checksum manifest is too large to read for {selection.Package!.Name}.",
                LauncherSelfUpdateFailureKind.Verification);
        }

        if (!LauncherUpdateChecksumManifest.TryParse(manifestText, out var hashes))
        {
            return FailPreparation(
                "The release checksum manifest is not a valid checksum list.",
                LauncherSelfUpdateFailureKind.Verification);
        }

        if (!LauncherUpdateChecksumManifest.TryGetHash(hashes, selection.Package!.Name, out var expectedSha256))
        {
            return FailPreparation(
                $"The release checksum manifest does not cover {selection.Package!.Name}.",
                LauncherSelfUpdateFailureKind.Verification);
        }

        var download = await downloader
            .DownloadAndVerifyAsync(selection.Package!, expectedSha256, destinationPath, progress, cancellationToken)
            .ConfigureAwait(false);
        if (!download.IsSuccess)
        {
            diagnostics.LogMessage(
                LogEntrySeverity.Warn,
                "LauncherUpdateDownload",
                download.FailureMessage);
            return LauncherSelfUpdatePreparation.Failed(
                download.FailureMessage,
                FailureKindOf(download.Status));
        }

        return LauncherSelfUpdatePreparation.Ready(selection.Target, download.FilePath!, expectedSha256);
    }

    /// <summary>
    /// Completes the selector's answer with the fact only this installation can answer: whether
    /// the helper that applies a verified package is actually here. A release-side reason is
    /// reported as-is — there is no point blaming the installation for an unusable release.
    /// </summary>
    private LauncherUpdateInAppAvailability AvailabilityOf(LauncherUpdateSelection selection) =>
        selection.Availability != LauncherUpdateInAppAvailability.Available
            ? selection.Availability
            : updateApplier.IsAvailable
                ? LauncherUpdateInAppAvailability.Available
                : LauncherUpdateInAppAvailability.HelperMissing;

    /// <summary>
    /// The download gate: only an available verdict may start a transfer. A host without a helper
    /// never downloads a package it could not apply.
    /// </summary>
    private bool CanStartDownload(LauncherUpdateSelection selection) =>
        AvailabilityOf(selection) == LauncherUpdateInAppAvailability.Available;

    private LauncherSelfUpdatePreparation FailPreparation(
        string message,
        LauncherSelfUpdateFailureKind kind)
    {
        diagnostics.LogMessage(LogEntrySeverity.Warn, "LauncherUpdateDownload", message);
        return LauncherSelfUpdatePreparation.Failed(message, kind);
    }

    /// <summary>
    /// The staged file for one package, or <c>null</c> when the assembled path would leave
    /// <paramref name="updateDirectory"/>. The package name has already been checked as a single
    /// segment and the version segment is sanitized; this assertion covers both together, so a
    /// future relaxation of either step cannot turn staging into a write outside the data root.
    /// </summary>
    /// <remarks>
    /// Containment is <see cref="DirectoryTreeDeleter.IsUnder"/> — the same guard every other
    /// destructive path in Core uses, so the trailing-separator and platform-comparison rules
    /// live in one place.
    /// </remarks>
    private static string? TryResolveStagingPath(
        string updateDirectory,
        string version,
        string packageName)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(updateDirectory));
        var candidate = Path.GetFullPath(
            Path.Combine(directory, SanitizeVersionSegment(version), packageName));
        return DirectoryTreeDeleter.IsUnder(candidate, directory) ? candidate : null;
    }

    private static bool IsManifestReadFailure(Exception exception) =>
        exception is HttpRequestException or InvalidOperationException or IOException
        || exception is OperationCanceledException;

    private static LauncherSelfUpdateFailureKind FailureKindOf(LauncherUpdateDownloadStatus status) =>
        status switch
        {
            LauncherUpdateDownloadStatus.UrlRejected => LauncherSelfUpdateFailureKind.UrlRejected,
            LauncherUpdateDownloadStatus.ChecksumMismatch => LauncherSelfUpdateFailureKind.Verification,
            LauncherUpdateDownloadStatus.IoFailure => LauncherSelfUpdateFailureKind.Io,
            _ => LauncherSelfUpdateFailureKind.Network
        };

    /// <summary>Keeps the version segment inside one directory name on every platform.</summary>
    private static string SanitizeVersionSegment(string version)
    {
        // 点号是合法文件名字符，"." / ".." 会通过逐字符过滤却指向父目录：显式改写。
        if (string.IsNullOrWhiteSpace(version) || version is "." or "..")
        {
            return "unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(version.Length);
        foreach (var character in version)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        // Windows 会静默丢弃目录名末尾的点与空格，落盘名与这里的字符串随即不同
        // （".._.." 会变成 ".._"，随后的打开按原字符串找不到同一目录）。先自行裁掉。
        var sanitized = builder.ToString().TrimEnd(' ', '.');
        return sanitized.Length == 0 || sanitized is "." or ".." ? "unknown" : sanitized;
    }
}
