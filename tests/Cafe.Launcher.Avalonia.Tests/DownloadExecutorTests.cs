using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DownloadExecutorTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public DownloadExecutorTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public async Task InstallDownloadedFilesAsync_WhenTargetFileIsReadOnly_ReplacesInstalledFile()
    {
        var targetPath = Path.Combine(tempDir, "data.bin");
        var tempPath = DownloadExecutor.GetTempName(targetPath);
        var payload = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(tempPath, payload);
        await File.WriteAllTextAsync(targetPath, "stale");
        File.SetAttributes(targetPath, FileAttributes.ReadOnly);

        var hash = await new Crc64Service().ComputeFileAsync(tempPath);
        var manifestFile = new ManifestFile
        {
            Path = "data.bin",
            Size = payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Hash = hash
        };
        var failed = await CreateExecutor().InstallDownloadedFilesAsync(
            tempDir,
            [manifestFile],
            [manifestFile],
            _ => { },
            CancellationToken.None);

        Assert.Empty(failed);
        Assert.Equal(payload, await File.ReadAllBytesAsync(targetPath));
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public async Task InstallDownloadedFilesAsync_WhenCrcMismatch_DeletesTempFileAndReportsFailure()
    {
        var targetPath = Path.Combine(tempDir, "mismatch.bin");
        var tempPath = DownloadExecutor.GetTempName(targetPath);
        await File.WriteAllBytesAsync(tempPath, [9, 9, 9]);
        var manifestFile = new ManifestFile
        {
            Path = "mismatch.bin",
            Size = "3",
            Hash = "deadbeef"
        };

        var failed = await CreateExecutor().InstallDownloadedFilesAsync(
            tempDir,
            [manifestFile],
            [manifestFile],
            _ => { },
            CancellationToken.None);

        _ = Assert.Single(failed);
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void RemoveFiles_WhenFileIsReadOnly_DeletesFile()
    {
        var filePath = Path.Combine(tempDir, "removed.bin");
        File.WriteAllText(filePath, "data");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        DownloadExecutor.RemoveFiles(
            tempDir,
            [new ManifestFile { Path = "removed.bin" }],
            progress: null);

        Assert.False(File.Exists(filePath));
    }

    private DownloadExecutor CreateExecutor() =>
        new(
            new NoOpFileDownloadService(),
            new Crc64Service(),
            new HttpClientFactory(new ProxySettingsService()),
            new LocalDiagnostics(),
            () => Task.CompletedTask,
            () => false);

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class NoOpFileDownloadService : IFileDownloadService
    {
        public Task DownloadAsync(
            FileDownloadRequest request,
            FileDownloadOperationControl operationControl,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
