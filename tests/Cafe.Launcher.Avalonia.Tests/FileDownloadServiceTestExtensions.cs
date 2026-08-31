using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Test-only convenience for focused transfer tests. Wraps the public
/// <see cref="IFileDownloadService.DownloadAsync(FileDownloadRequest, FileDownloadOperationControl, CancellationToken)"/>
/// so the production interface remains the only compiled surface.
/// </summary>
internal static class FileDownloadServiceTestExtensions
{
    internal static Task DownloadAsync(
        this FileDownloadService service,
        string targetTempPath,
        CdnConfigResponse cdnConfig,
        string source,
        long expectedSize,
        string expectedHash,
        string filePath,
        HttpClient httpClient,
        Func<Task> pauseAwaiter,
        Func<long, CancellationToken, Task> onProgressAsync,
        bool connectionUsesProxy,
        CancellationToken cancellationToken) =>
        service.DownloadAsync(
            new FileDownloadRequest(
                targetTempPath,
                cdnConfig,
                source,
                expectedSize,
                expectedHash,
                filePath),
            new FileDownloadOperationControl(
                httpClient,
                pauseAwaiter,
                onProgressAsync,
                ct => onProgressAsync(0, ct),
                connectionUsesProxy),
            cancellationToken);
}
