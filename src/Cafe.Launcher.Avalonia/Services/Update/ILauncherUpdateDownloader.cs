using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// Fetches launcher release assets: small text assets (the SHA256SUMS manifest) and
/// the update package itself, the latter hashed while it streams and checked against
/// the expected digest before it is promoted to its final path.
/// </summary>
public interface ILauncherUpdateDownloader
{
    /// <summary>
    /// Reads a small text asset. Returns null on any recoverable failure (network,
    /// rejected URL, oversized body, or a timeout that is not caller cancellation);
    /// caller cancellation propagates.
    /// </summary>
    Task<string?> ReadTextAsync(ReleaseFile file, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads <paramref name="file"/> to <paramref name="destinationPath"/> and
    /// verifies its SHA-256 against <paramref name="expectedSha256"/>. The file is
    /// written to a sibling <c>.part</c> path and only moved into place on a match.
    /// </summary>
    Task<LauncherUpdateDownloadResult> DownloadAndVerifyAsync(
        ReleaseFile file,
        string expectedSha256,
        string destinationPath,
        IProgress<LauncherUpdateProgress>? progress,
        CancellationToken cancellationToken);
}
