using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Auth;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameDownloadServiceTests
{
    static GameDownloadServiceTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void GetSafePath_WhenPathIsRelative_ReturnsPathInsideGameDirectory()
    {
        var gamePath = Path.Combine(Path.GetTempPath(), "YostarGames", "BlueArchive_JP");

        var result = GamePathValidator.GetSafePath(gamePath, "data/file.bin");

        Assert.Equal(Path.Combine(Path.GetFullPath(gamePath), "data", "file.bin"), result);
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("..\\outside.bin")]
    [InlineData("data/../../outside.bin")]
    public void GetSafePath_WhenPathEscapesGameDirectory_Throws(string relativePath)
    {
        var gamePath = Path.Combine(Path.GetTempPath(), "YostarGames", "BlueArchive_JP");

        Assert.Throws<InvalidOperationException>(() => GamePathValidator.GetSafePath(gamePath, relativePath));
    }

    [Fact]
    public void Dispose_WhenCalledTwice_DoesNotThrow()
    {
        using var apiClient = new LauncherApiClient(new HttpClientHandler(), new AuthorizationHeaderFactory(), new PatchUrlGroupService());
        var service = CreateService(apiClient);

        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public void Dispose_AfterStop_DoesNotThrow()
    {
        using var apiClient = new LauncherApiClient(new HttpClientHandler(), new AuthorizationHeaderFactory(), new PatchUrlGroupService());
        var service = CreateService(apiClient);

        service.Stop();
        service.Dispose();
    }

    [Fact]
    public void RetryDomainOrder_ReturnsExpectedSequence()
    {
        Assert.Equal([1, 1, 1, 1, 0, 0, 0, 1, 1, 1], FileDownloadService.RetryDomainOrder);
    }

    [Fact]
    public void ResolveRetryDomain_WhenRetryTypeIsOne_UsesPrimaryCdn()
    {
        var cdnConfig = new CdnConfigResponse
        {
            PrimaryCdn = "https://primary.example.invalid",
            BackUpCdn = "https://backup.example.invalid"
        };

        var result = FileDownloadService.ResolveRetryDomain(cdnConfig, FileDownloadService.RetryDomainOrder[0]);

        Assert.Equal("https://primary.example.invalid", result);
    }

    [Fact]
    public void ResolveRetryDomain_WhenRetryTypeIsZero_UsesBackupCdn()
    {
        var cdnConfig = new CdnConfigResponse
        {
            PrimaryCdn = "https://primary.example.invalid",
            BackUpCdn = "https://backup.example.invalid"
        };

        var result = FileDownloadService.ResolveRetryDomain(cdnConfig, 0);

        Assert.Equal("https://backup.example.invalid", result);
    }

    [Fact]
    public void BuildDownloadUrl_WhenCafeGroupCdnConfigIsUsed_UsesCafePackageHost()
    {
        var patchUrlGroupService = new PatchUrlGroupService();
        var cdnConfig = patchUrlGroupService.RewriteCdnConfig(
            new CdnConfigResponse
            {
                PrimaryCdn = "https://launcher-pkg-ba-jp.yo-star.com",
                BackUpCdn = "https://launcher-pkg-ba-jp.yo-star.com"
            },
            PatchUrlGroups.Cafe);

        var url = FileDownloadService.BuildDownloadUrl(
            cdnConfig.PrimaryCdn,
            "/source/root",
            "/data/file name.bin");

        Assert.Equal("https://launcher-pkg-ba-jp.bluearchive.cafe/source/root/data/file%20name.bin", url);
    }

    [Fact]
    public void BuildDownloadUrl_WhenDomainIsBlank_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => FileDownloadService.BuildDownloadUrl(" ", "/source/root", "/data/file.bin"));
    }

    [Fact]
    public async Task DownloadFileAsync_WhenTemporaryFileAlreadyMatchesExpectedSize_SkipsHttpRequest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var targetPath = Path.Combine(tempDir, "file.bin.tmp");
            var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
            await File.WriteAllBytesAsync(targetPath, expectedBytes);
            var hashPath = Path.Combine(tempDir, "hash-source.bin");
            await File.WriteAllBytesAsync(hashPath, expectedBytes);
            var expectedHash = await new Crc64Service().ComputeFileAsync(hashPath);
            var handler = new CountingHandler(expectedBytes);
            using var client = new HttpClient(handler);
            var downloader = new FileDownloadService(
                new Crc64Service(),
                new LocalDiagnostics(),
                RemoteHttpUrlValidator.CreateForTesting());

            await downloader.DownloadAsync(
                targetPath,
                new CdnConfigResponse
                {
                    PrimaryCdn = "https://primary.example.invalid",
                    BackUpCdn = "https://backup.example.invalid"
                },
                "source",
                expectedBytes.Length,
                expectedHash,
                "file.bin",
                client,
                () => Task.CompletedTask,
                (_, _) => Task.CompletedTask,
                false,
                CancellationToken.None);

            Assert.Equal(0, handler.RequestCount);
            Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadFileAsync_WhenTemporaryFileIsLargerThanExpected_DownloadsFreshCopyWithoutRangeHeader()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var targetPath = Path.Combine(tempDir, "file.bin.tmp");
            var expectedBytes = Encoding.UTF8.GetBytes("fresh-content");
            await File.WriteAllBytesAsync(targetPath, Encoding.UTF8.GetBytes("this-content-is-longer-than-expected"));
            var hashPath = Path.Combine(tempDir, "hash-source.bin");
            await File.WriteAllBytesAsync(hashPath, expectedBytes);
            var expectedHash = await new Crc64Service().ComputeFileAsync(hashPath);
            var handler = new RangeIgnoredHandler(expectedBytes);
            using var client = new HttpClient(handler);
            var downloader = new FileDownloadService(
                new Crc64Service(),
                new LocalDiagnostics(),
                RemoteHttpUrlValidator.CreateForTesting());

            await downloader.DownloadAsync(
                targetPath,
                new CdnConfigResponse
                {
                    PrimaryCdn = "https://primary.example.invalid",
                    BackUpCdn = "https://backup.example.invalid"
                },
                "source",
                expectedBytes.Length,
                expectedHash,
                "file.bin",
                client,
                () => Task.CompletedTask,
                (_, _) => Task.CompletedTask,
                false,
                CancellationToken.None);

            Assert.False(handler.RangeWasRequested);
            Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadFileAsync_WhenDownloadedHashMismatches_TriesNextRetryDomain()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        var expectedBytes = Encoding.UTF8.GetBytes("correct-content");
        var hashPath = Path.Combine(tempDir, "hash-source.bin");
        await File.WriteAllBytesAsync(hashPath, expectedBytes);
        var expectedHash = await new Crc64Service().ComputeFileAsync(hashPath);
        using var apiClient = new LauncherApiClient(new HttpClientHandler(), new AuthorizationHeaderFactory(), new PatchUrlGroupService());
        using var service = CreateService(apiClient);
        var handler = new RetryContentHandler(expectedBytes);
        using var client = new HttpClient(handler);
        var file = new ManifestFile
        {
            Path = "data/file.bin",
            Size = expectedBytes.Length.ToString(CultureInfo.InvariantCulture),
            Hash = expectedHash
        };
        var cdnConfig = new CdnConfigResponse
        {
            PrimaryCdn = "https://primary.example.invalid",
            BackUpCdn = "https://backup.example.invalid"
        };

        await InvokeDownloadFileAsync(service, gamePath, cdnConfig, "/source", file, client);

        Assert.Equal(
            [
                "primary.example.invalid",
                "primary.example.invalid",
                "primary.example.invalid",
                "primary.example.invalid",
                "backup.example.invalid"
            ],
            handler.RequestHosts);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(Path.Combine(gamePath, "data", "file.bin.tmp")));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task DownloadFileAsync_WhenEveryHashMismatches_ThrowsAndRemovesTemporaryFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        var expectedBytes = Encoding.UTF8.GetBytes("correct-content");
        var hashPath = Path.Combine(tempDir, "hash-source.bin");
        await File.WriteAllBytesAsync(hashPath, expectedBytes);
        var expectedHash = await new Crc64Service().ComputeFileAsync(hashPath);
        using var apiClient = new LauncherApiClient(new HttpClientHandler(), new AuthorizationHeaderFactory(), new PatchUrlGroupService());
        using var service = CreateService(apiClient);
        var handler = new AlwaysWrongContentHandler();
        using var client = new HttpClient(handler);
        var file = new ManifestFile
        {
            Path = "data/file.bin",
            Size = expectedBytes.Length.ToString(CultureInfo.InvariantCulture),
            Hash = expectedHash
        };
        var cdnConfig = new CdnConfigResponse
        {
            PrimaryCdn = "https://primary.example.invalid",
            BackUpCdn = "https://backup.example.invalid"
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => InvokeDownloadFileAsync(service, gamePath, cdnConfig, "/source", file, client));

        Assert.Equal(FileDownloadService.RetryDomainOrder.Length, handler.RequestCount);
        Assert.False(File.Exists(Path.Combine(gamePath, "data", "file.bin.tmp")));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task DownloadFileAsync_WhenRangeIsIgnored_ReplacesTemporaryFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        await File.WriteAllBytesAsync(targetPath, expectedBytes[..4]);
        var hashPath = Path.Combine(tempDir, "hash-source.bin");
        await File.WriteAllBytesAsync(hashPath, expectedBytes);
        var expectedHash = await new Crc64Service().ComputeFileAsync(hashPath);
        var handler = new RangeIgnoredHandler(expectedBytes);
        using var client = new HttpClient(handler);
        var downloader = new FileDownloadService(
            new Crc64Service(),
            new LocalDiagnostics(),
            RemoteHttpUrlValidator.CreateForTesting());

        await downloader.DownloadAsync(
            targetPath,
            new CdnConfigResponse
            {
                PrimaryCdn = "https://primary.example.invalid",
                BackUpCdn = "https://backup.example.invalid"
            },
            "source",
            expectedBytes.Length,
            expectedHash,
            "file.bin",
            client,
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            false,
            CancellationToken.None);

        Assert.True(handler.RangeWasRequested);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task DownloadFileAsync_WhenContentRangeStartsAtWrongOffset_RetriesWithoutCorruptingFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        await File.WriteAllBytesAsync(targetPath, expectedBytes[..4]);
        var hashPath = Path.Combine(tempDir, "hash-source.bin");
        await File.WriteAllBytesAsync(hashPath, expectedBytes);
        var expectedHash = await new Crc64Service().ComputeFileAsync(hashPath);
        var handler = new InvalidRangeThenCompleteHandler(expectedBytes);
        using var client = new HttpClient(handler);
        var downloader = new FileDownloadService(
            new Crc64Service(),
            new LocalDiagnostics(),
            RemoteHttpUrlValidator.CreateForTesting());

        await downloader.DownloadAsync(
            targetPath,
            new CdnConfigResponse
            {
                PrimaryCdn = "https://primary.example.invalid",
                BackUpCdn = "https://backup.example.invalid"
            },
            "source",
            expectedBytes.Length,
            expectedHash,
            "file.bin",
            client,
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            false,
            CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task DownloadFileAsync_WhenContentRangeTotalLengthMismatches_RetriesWithoutCorruptingFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var targetPath = Path.Combine(tempDir, "file.bin.tmp");
            var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
            await File.WriteAllBytesAsync(targetPath, expectedBytes[..4]);
            var hashPath = Path.Combine(tempDir, "hash-source.bin");
            await File.WriteAllBytesAsync(hashPath, expectedBytes);
            var expectedHash = await new Crc64Service().ComputeFileAsync(hashPath);
            var handler = new InvalidContentLengthThenCompleteHandler(expectedBytes);
            using var client = new HttpClient(handler);
            var downloader = new FileDownloadService(
                new Crc64Service(),
                new LocalDiagnostics(),
                RemoteHttpUrlValidator.CreateForTesting());

            await downloader.DownloadAsync(
                targetPath,
                new CdnConfigResponse
                {
                    PrimaryCdn = "https://primary.example.invalid",
                    BackUpCdn = "https://backup.example.invalid"
                },
                "source",
                expectedBytes.Length,
                expectedHash,
                "file.bin",
                client,
                () => Task.CompletedTask,
                (_, _) => Task.CompletedTask,
                false,
                CancellationToken.None);

            Assert.Equal(2, handler.RequestCount);
            Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadFileAsync_WhenTransferFails_ResumesFromWrittenBytes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var targetPath = Path.Combine(tempDir, "file.bin.tmp");
            var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
            var hashPath = Path.Combine(tempDir, "hash-source.bin");
            await File.WriteAllBytesAsync(hashPath, expectedBytes);
            var expectedHash = await new Crc64Service().ComputeFileAsync(hashPath);
            var handler = new InterruptedTransferHandler(expectedBytes);
            using var client = new HttpClient(handler);
            var downloader = new FileDownloadService(
                new Crc64Service(),
                new LocalDiagnostics(),
                RemoteHttpUrlValidator.CreateForTesting());

            await downloader.DownloadAsync(
                targetPath,
                new CdnConfigResponse
                {
                    PrimaryCdn = "https://primary.example.invalid",
                    BackUpCdn = "https://backup.example.invalid"
                },
                "source",
                expectedBytes.Length,
                expectedHash,
                "file.bin",
                client,
                () => Task.CompletedTask,
                (_, _) => Task.CompletedTask,
                false,
                CancellationToken.None);

            Assert.Equal(4, handler.SecondRequestRangeStart);
            Assert.Equal(2, handler.RequestCount);
            Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenNoFilesNeedChanges_ClearsDownloadState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        var settingsPath = Path.Combine(tempDir, "settings.json");
        var statePath = Path.Combine(tempDir, "download_state.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        await WriteLocalGameFilesAsync(gamePath);
        using var apiClient = CreateManifestApiClient();
        var service = CreateService(apiClient, settingsService, statePath);

        var result = await service.InstallOrUpdateAsync(CreateSnapshot(gamePath), _ => { });

        Assert.True(result.Success);
        Assert.False(File.Exists(statePath));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenFileIsRequired_InstallsFileAndCommitsInstallationState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        var statePath = Path.Combine(tempDir, "download_state.json");
        var fileBytes = Encoding.UTF8.GetBytes("installed-content");
        var hashPath = Path.Combine(tempDir, "hash-source.bin");
        Directory.CreateDirectory(tempDir);
        await File.WriteAllBytesAsync(hashPath, fileBytes);
        var expectedHash = await new Crc64Service().ComputeFileAsync(hashPath);
        using var apiClient = CreateManifestApiClient(
            new ManifestFile
            {
                Path = "data/file.bin",
                Size = fileBytes.Length.ToString(CultureInfo.InvariantCulture),
                Hash = expectedHash
            });
        var downloader = new WritingFileDownloadService(fileBytes);
        using var service = CreateService(apiClient, settingsService, statePath, downloader);
        var progress = new List<GameOperationProgress>();
        var snapshot = CreateSnapshot(gamePath);
        snapshot.RuntimeState = LauncherRuntimeState.NotInstalled;

        var result = await service.InstallOrUpdateAsync(snapshot, progress.Add);
        var installationState = await new LocalInstallationStateStore().ReadAsync(gamePath);

        Assert.True(result.Success);
        Assert.Equal(1, result.AffectedFileCount);
        Assert.Equal(fileBytes, await File.ReadAllBytesAsync(Path.Combine(gamePath, "data", "file.bin")));
        Assert.Equal(LocalInstallationStateKind.Valid, installationState.Kind);
        Assert.Equal("1.0.0", installationState.Manifest?.Version);
        Assert.Contains(
            installationState.Manifest?.Files ?? [],
            file => file.Path == "data/file.bin" && file.Hash == expectedHash);
        Assert.Contains(progress, item =>
            item.Stage == GameOperationStage.DownloadCompleted && item.Progress == 100);
        Assert.False(File.Exists(statePath));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenPaused_WaitsUntilResumeBeforeCompleting()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        var fileBytes = Encoding.UTF8.GetBytes("pause-resume-content");
        var manifestFile = await CreateManifestFileAsync(tempDir, "data/file.bin", fileBytes);
        using var apiClient = CreateManifestApiClient(manifestFile);
        var downloader = new ControlledFileDownloadService(fileBytes);
        using var service = CreateService(
            apiClient,
            settingsService,
            Path.Combine(tempDir, "download_state.json"),
            downloader);
        var snapshot = CreateSnapshot(gamePath);
        snapshot.RuntimeState = LauncherRuntimeState.NotInstalled;

        var operation = service.InstallOrUpdateAsync(snapshot, _ => { });
        await downloader.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.Pause();
        downloader.AllowPauseCheck.TrySetResult();
        await downloader.PauseCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(service.IsPaused);
        Assert.False(operation.IsCompleted);

        service.Resume();
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.Success);
        Assert.False(service.IsPaused);
        Assert.Equal(fileBytes, await File.ReadAllBytesAsync(Path.Combine(gamePath, "data", "file.bin")));
        Directory.Delete(tempDir, recursive: true);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Stop_WhenOperationIsPaused_StopsOperationAndAppliesPersistedStateChoice(
        bool clearPersistedState,
        bool expectedStateFileExists)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        var statePath = Path.Combine(tempDir, "download_state.json");
        var fileBytes = Encoding.UTF8.GetBytes("stopped-content");
        var manifestFile = await CreateManifestFileAsync(tempDir, "data/file.bin", fileBytes);
        using var apiClient = CreateManifestApiClient(manifestFile);
        var downloader = new ControlledFileDownloadService(fileBytes);
        using var service = CreateService(apiClient, settingsService, statePath, downloader);
        var progress = new List<GameOperationProgress>();
        var snapshot = CreateSnapshot(gamePath);
        snapshot.RuntimeState = LauncherRuntimeState.NotInstalled;

        var operation = service.InstallOrUpdateAsync(snapshot, progress.Add);
        await downloader.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.Pause();
        downloader.AllowPauseCheck.TrySetResult();
        await downloader.PauseCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        service.Stop(clearPersistedState);
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(result.Success);
        Assert.Equal(GameOperationErrorCode.Stopped, result.ErrorCode);
        Assert.False(service.IsRunning);
        Assert.False(service.IsPaused);
        Assert.Contains(progress, item => item.Stage == GameOperationStage.Stopped);
        Assert.Equal(expectedStateFileExists, File.Exists(statePath));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenDiskSpaceIsInsufficient_DoesNotStartDownloads()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        var manifestFile = new ManifestFile
        {
            Path = "data/huge.bin",
            Size = long.MaxValue.ToString(CultureInfo.InvariantCulture),
            Hash = "0"
        };
        using var apiClient = CreateManifestApiClient(manifestFile);
        var downloader = new RecordingFileDownloadService();
        using var service = CreateService(
            apiClient,
            settingsService,
            Path.Combine(tempDir, "download_state.json"),
            downloader);
        var snapshot = CreateSnapshot(gamePath);
        snapshot.RuntimeState = LauncherRuntimeState.NotInstalled;

        var result = await service.InstallOrUpdateAsync(snapshot, _ => { });

        Assert.False(result.Success);
        Assert.Equal(GameOperationErrorCode.InsufficientDiskSpace, result.ErrorCode);
        Assert.Equal(1, result.AffectedFileCount);
        Assert.Equal(0, downloader.InvocationCount);
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenDiskSpaceIsInsufficient_ClearsDownloadState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        var manifestFile = new ManifestFile
        {
            Path = "data/huge.bin",
            Size = long.MaxValue.ToString(CultureInfo.InvariantCulture),
            Hash = "0"
        };
        using var apiClient = CreateManifestApiClient(manifestFile);
        var statePath = Path.Combine(tempDir, "download_state.json");
        using var service = CreateService(
            apiClient,
            settingsService,
            statePath);
        var snapshot = CreateSnapshot(gamePath);
        snapshot.RuntimeState = LauncherRuntimeState.NotInstalled;

        var result = await service.InstallOrUpdateAsync(snapshot, _ => { });

        Assert.False(result.Success);
        Assert.False(File.Exists(statePath));
        Directory.Delete(tempDir, recursive: true);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(9L, false)]
    [InlineData(10L, true)]
    public async Task InstallOrUpdateAsync_ReadsAvailableDiskSpaceOnceAndReportsDiskCheck(
        long? availableBytes,
        bool expectedSuccess)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        var fileBytes = new byte[10];
        var manifestFile = await CreateManifestFileAsync(tempDir, "data/file.bin", fileBytes);
        using var apiClient = CreateManifestApiClient(manifestFile);
        var readCount = 0;
        var diskSpaceService = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ =>
            {
                readCount++;
                return availableBytes;
            }
        };
        using var service = CreateService(
            apiClient,
            settingsService,
            Path.Combine(tempDir, "download_state.json"),
            new WritingFileDownloadService(fileBytes),
            diskSpaceService);
        var progress = new List<GameOperationProgress>();
        var snapshot = CreateSnapshot(gamePath);
        snapshot.RuntimeState = LauncherRuntimeState.NotInstalled;

        var result = await service.InstallOrUpdateAsync(snapshot, progress.Add);

        Assert.Equal(expectedSuccess, result.Success);
        Assert.Equal(1, readCount);
        Assert.Contains(progress, item =>
            item.Stage == GameOperationStage.DiskCheck
            && item.RequiredDiskBytes == 10
            && item.AvailableDiskBytes == availableBytes);
        if (!expectedSuccess)
        {
            Assert.Contains(FileSizeFormatter.Format(10), result.Message, StringComparison.Ordinal);
            Assert.Contains(
                availableBytes.HasValue ? FileSizeFormatter.Format(availableBytes.Value) : "--",
                result.Message,
                StringComparison.Ordinal);
        }
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenMoreThanTenFilesAreRequired_LimitsParallelDownloadsToTen()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        var fileBytes = Encoding.UTF8.GetBytes("parallel-content");
        var hashFile = await CreateManifestFileAsync(tempDir, "unused.bin", fileBytes);
        var manifestFiles = Enumerable.Range(0, 12)
            .Select(index => new ManifestFile
            {
                Path = $"data/file-{index}.bin",
                Size = fileBytes.Length.ToString(CultureInfo.InvariantCulture),
                Hash = hashFile.Hash
            })
            .ToArray();
        using var apiClient = CreateManifestApiClient(manifestFiles);
        var downloader = new ParallelTrackingFileDownloadService(fileBytes, expectedBlockedCount: 10);
        using var service = CreateService(
            apiClient,
            settingsService,
            Path.Combine(tempDir, "download_state.json"),
            downloader);
        var snapshot = CreateSnapshot(gamePath);
        snapshot.RuntimeState = LauncherRuntimeState.NotInstalled;

        var operation = service.InstallOrUpdateAsync(snapshot, _ => { });
        await downloader.ExpectedBlockedCountReached.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(10, downloader.StartedCount);
        Assert.Equal(10, downloader.MaximumConcurrency);

        downloader.Release.TrySetResult();
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.Success);
        Assert.Equal(12, downloader.StartedCount);
        Assert.Equal(10, downloader.MaximumConcurrency);
        Assert.All(
            manifestFiles,
            file => Assert.True(File.Exists(Path.Combine(gamePath, file.Path.Replace('/', Path.DirectorySeparatorChar)))));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenInstallVerificationFails_RedownloadsFailedFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        var expectedBytes = Encoding.UTF8.GetBytes("verified-content");
        var manifestFile = await CreateManifestFileAsync(tempDir, "data/file.bin", expectedBytes);
        using var apiClient = CreateManifestApiClient(manifestFile);
        var downloader = new VerificationRetryFileDownloadService(
            Encoding.UTF8.GetBytes("invalid-content"),
            expectedBytes);
        using var service = CreateService(
            apiClient,
            settingsService,
            Path.Combine(tempDir, "download_state.json"),
            downloader);
        var snapshot = CreateSnapshot(gamePath);
        snapshot.RuntimeState = LauncherRuntimeState.NotInstalled;

        var result = await service.InstallOrUpdateAsync(snapshot, _ => { });

        Assert.True(result.Success);
        Assert.Equal(2, downloader.InvocationCount);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(Path.Combine(gamePath, "data", "file.bin")));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenSpeedLimitIsOneMegabytePerSecond_ThrottlesReportedBytes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings
        {
            GamePath = gamePath,
            DownloadSpeedLimit = DownloadSpeedLimits.Speed1MBs
        });
        var fileBytes = new byte[1024 * 1024];
        Random.Shared.NextBytes(fileBytes);
        var manifestFile = await CreateManifestFileAsync(tempDir, "data/file.bin", fileBytes);
        using var apiClient = CreateManifestApiClient(manifestFile);
        using var service = CreateService(
            apiClient,
            settingsService,
            Path.Combine(tempDir, "download_state.json"),
            new WritingFileDownloadService(fileBytes));
        var snapshot = CreateSnapshot(gamePath);
        snapshot.RuntimeState = LauncherRuntimeState.NotInstalled;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = await service.InstallOrUpdateAsync(snapshot, _ => { });
        watch.Stop();

        Assert.True(result.Success);
        Assert.True(
            watch.Elapsed >= TimeSpan.FromMilliseconds(800),
            $"Expected throttled install to take at least 800 ms, actual: {watch.Elapsed}.");
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenInstallVerificationAlwaysFails_StopsAfterThreeRetries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        var expectedBytes = Encoding.UTF8.GetBytes("expected-content");
        var manifestFile = await CreateManifestFileAsync(tempDir, "data/file.bin", expectedBytes);
        using var apiClient = CreateManifestApiClient(manifestFile);
        var downloader = new VerificationRetryFileDownloadService(
            Encoding.UTF8.GetBytes("invalid-content"),
            Encoding.UTF8.GetBytes("invalid-content"));
        using var service = CreateService(
            apiClient,
            settingsService,
            Path.Combine(tempDir, "download_state.json"),
            downloader);
        var snapshot = CreateSnapshot(gamePath);
        snapshot.RuntimeState = LauncherRuntimeState.NotInstalled;

        var progress = new List<GameOperationProgress>();
        var result = await service.InstallOrUpdateAsync(snapshot, progress.Add);

        Assert.False(result.Success);
        Assert.Equal(GameOperationErrorCode.Network, result.ErrorCode);
        Assert.Equal(1, result.FailedFileCount);
        Assert.Equal(4, downloader.InvocationCount);
        Assert.Equal(
            [(1, 3, 1), (2, 3, 1), (3, 3, 1)],
            progress
                .Where(item => item.Stage == GameOperationStage.VerificationRetry)
                .Select(item => (item.RetryAttempt, item.RetryLimit, item.FailedFileCount))
                .ToArray());
        Assert.Contains(progress, item =>
            item.Stage == GameOperationStage.VerificationFailed
            && item.FailedFileCount == 1);
        Assert.False(File.Exists(Path.Combine(gamePath, "data", "file.bin.tmp")));
        Assert.False(File.Exists(Path.Combine(gamePath, "data", "file.bin")));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task ResumePersistedAsync_WhenStateDoesNotMatchCurrentVersion_ClearsDownloadState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var statePath = Path.Combine(tempDir, "download_state.json");
        await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new DownloadTaskState
        {
            Version = "0.9.0",
            Basis = "manifest.json",
            GamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP")
        }));
        using var apiClient = CreateManifestApiClient();
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        var service = CreateService(apiClient, settingsService, statePath);

        var result = await service.ResumePersistedAsync(CreateSnapshot(Path.Combine(tempDir, "YostarGames", "BlueArchive_JP")), _ => { });

        Assert.Null(result);
        Assert.False(File.Exists(statePath));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task ResumePersistedAsync_WhenStateUsesDifferentPatchUrlGroup_ClearsDownloadState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var statePath = Path.Combine(tempDir, "download_state.json");
        await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new DownloadTaskState
        {
            Version = "1.0.0",
            Basis = "manifest.json",
            GamePath = gamePath,
            PatchUrlGroup = PatchUrlGroups.Official
        }));
        using var apiClient = CreateManifestApiClient();
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        var service = CreateService(apiClient, settingsService, statePath);
        var snapshot = CreateSnapshot(gamePath);
        snapshot.Settings.PatchUrlGroup = PatchUrlGroups.Cafe;

        var result = await service.ResumePersistedAsync(snapshot, _ => { });

        Assert.Null(result);
        Assert.False(File.Exists(statePath));
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task ResumePersistedAsync_WhenOperationIsRunning_DoesNotReplaceActiveOperation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        await WriteLocalGameFilesAsync(gamePath);
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        var statePath = Path.Combine(tempDir, "download_state.json");
        var handler = new BlockingManifestHandler();
        using var apiClient = new LauncherApiClient(
            handler,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());
        using var service = CreateService(apiClient, settingsService, statePath);
        var snapshot = CreateSnapshot(gamePath);
        snapshot.RuntimeState = LauncherRuntimeState.Corrupted;
        var repairTask = service.RepairAsync(snapshot, _ => { });

        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var resumeTask = service.ResumePersistedAsync(snapshot, _ => { });

        try
        {
            var completed = await Task.WhenAny(resumeTask, Task.Delay(TimeSpan.FromMilliseconds(250)));
            Assert.Same(resumeTask, completed);
            Assert.Null(await resumeTask);
            Assert.False(repairTask.IsCompleted);
        }
        finally
        {
            handler.Release.TrySetResult();
            service.Stop();
            await repairTask;
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RepairAsync_WhenInstallationStateIsCorrupted_UsesOnlyLatestManifestAndKeepsUnknownFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        await File.WriteAllTextAsync(Path.Combine(gamePath, "manifest.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(gamePath, "unknown.bin"), "keep");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
        var handler = new LatestManifestOnlyHandler();
        using var apiClient = new LauncherApiClient(
            handler,
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());
        using var service = CreateService(
            apiClient,
            settingsService,
            Path.Combine(tempDir, "download_state.json"));
        var snapshot = CreateSnapshot(gamePath);
        snapshot.RuntimeState = LauncherRuntimeState.Corrupted;

        var result = await service.RepairAsync(snapshot, _ => { });
        var state = await new LocalInstallationStateStore().ReadAsync(gamePath);

        Assert.True(result.Success);
        Assert.Equal(1, handler.ManifestUrlRequestCount);
        Assert.Equal(LocalInstallationStateKind.Valid, state.Kind);
        Assert.Equal("1.0.0", state.Manifest?.Version);
        Assert.True(File.Exists(Path.Combine(gamePath, "unknown.bin")));
        Directory.Delete(tempDir, recursive: true);
    }

    private static GameDownloadService CreateService(LauncherApiClient apiClient)
    {
        return CreateService(
            apiClient,
            new LauncherSettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json")),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "download_state.json"));
    }

    private static GameDownloadService CreateService(
        LauncherApiClient apiClient,
        LauncherSettingsService settingsService,
        string downloadStateFilePath,
        IFileDownloadService? fileDownloadService = null,
        DiskSpaceService? diskSpaceService = null)
    {
        var localInstallationStateStore = new LocalInstallationStateStore();
        var diagnostics = new LocalDiagnostics();
        var remoteManifestService = new RemoteManifestService(apiClient);
        fileDownloadService ??= new FileDownloadService(
            new Crc64Service(),
            diagnostics,
            RemoteHttpUrlValidator.CreateForTesting());
        return new GameDownloadService(
            new GameDownloadService.Dependencies(
                apiClient,
                remoteManifestService,
                fileDownloadService,
                localInstallationStateStore,
                settingsService,
                new ProxySettingsService(),
                new Crc64Service(),
                diskSpaceService ?? new DiskSpaceService(),
                diagnostics,
                new LocalizationService(),
                new GameInstallationPath()),
            downloadStateFilePath);
    }

    private static LauncherStatusSnapshot CreateSnapshot(string gamePath)
    {
        return new LauncherStatusSnapshot
        {
            Settings = new LauncherSettings { GamePath = gamePath },
            LocalGame = new LocalInstallationState { GamePath = gamePath },
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse
                {
                    GameLatestVersion = "1.0.0",
                    GameLatestFilePath = "manifest.json",
                    GameStartExeName = "BlueArchive"
                },
                CdnConfig = new CdnConfigResponse
                {
                    PrimaryCdn = "https://cdn.example.invalid",
                    BackUpCdn = "https://backup.example.invalid"
                }
            }
        };
    }

    private static async Task WriteLocalGameFilesAsync(string gamePath)
    {
        var gameConfig = new GameLauncherConfig
        {
            Tag = "BlueArchive_JP",
            Name = "CafeLauncherAvaloniaTestGame",
            Version = "1.0.0"
        };
        var manifest = new LocalManifest
        {
            Name = "BlueArchive_JP",
            Version = "1.0.0",
            Basis = "manifest.json",
            Files = []
        };
        await File.WriteAllTextAsync(Path.Combine(gamePath, "game-launcher-config.json"), JsonSerializer.Serialize(gameConfig));
        await File.WriteAllTextAsync(Path.Combine(gamePath, "manifest.json"), JsonSerializer.Serialize(manifest));
    }

    private static async Task<ManifestFile> CreateManifestFileAsync(
        string tempDir,
        string path,
        byte[] content)
    {
        Directory.CreateDirectory(tempDir);
        var hashPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(hashPath, content);
        return new ManifestFile
        {
            Path = path,
            Size = content.Length.ToString(CultureInfo.InvariantCulture),
            Hash = await new Crc64Service().ComputeFileAsync(hashPath)
        };
    }

    private static LauncherApiClient CreateManifestApiClient()
    {
        return new LauncherApiClient(
            new ManifestHandler(),
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());
    }

    private static LauncherApiClient CreateManifestApiClient(params ManifestFile[] files)
    {
        return new LauncherApiClient(
            new ManifestHandler(files),
            new AuthorizationHeaderFactory(),
            new PatchUrlGroupService());
    }

    private static async Task InvokeDownloadFileAsync(
        GameDownloadService service,
        string gamePath,
        CdnConfigResponse cdnConfig,
        string source,
        ManifestFile file,
        HttpClient client)
    {
        var targetPath = Path.Combine(gamePath, GetTempNameInternal(file.Path));
        var crc64Service = new Crc64Service();
        var diagnostics = new LocalDiagnostics();
        var downloader = new FileDownloadService(
            crc64Service,
            diagnostics,
            RemoteHttpUrlValidator.CreateForTesting());
        await downloader.DownloadAsync(
            targetPath,
            cdnConfig,
            source,
            long.Parse(file.Size, CultureInfo.InvariantCulture),
            file.Hash,
            file.Path,
            client,
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            false,
            CancellationToken.None);
    }

    private static string GetTempNameInternal(string name)
    {
        return $"{name}.tmp";
    }

    private sealed class ManifestHandler(params ManifestFile[] files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri?.ToString() ?? "";
            var json = requestUri.Contains("/api/launcher/game/config/json", StringComparison.Ordinal)
                ? "{\"code\":200,\"data\":{\"url\":\"https://manifest.example.invalid/manifest.json\"}}"
                : JsonSerializer.Serialize(new RemoteManifest
                {
                    Source = "source",
                    File = files.ToList()
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class WritingFileDownloadService(byte[] content) : IFileDownloadService
    {
        public async Task DownloadAsync(
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
            CancellationToken cancellationToken)
        {
            await pauseAwaiter();
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(targetTempPath)!);
            await File.WriteAllBytesAsync(targetTempPath, content, cancellationToken);
            await onProgressAsync(content.Length, cancellationToken);
        }
    }

    private sealed class ControlledFileDownloadService(byte[] content) : IFileDownloadService
    {
        public TaskCompletionSource DownloadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowPauseCheck { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PauseCheckStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DownloadAsync(
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
            CancellationToken cancellationToken)
        {
            DownloadStarted.TrySetResult();
            await AllowPauseCheck.Task.WaitAsync(cancellationToken);
            PauseCheckStarted.TrySetResult();
            await pauseAwaiter();
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(targetTempPath)!);
            await File.WriteAllBytesAsync(targetTempPath, content, cancellationToken);
            await onProgressAsync(content.Length, cancellationToken);
        }
    }

    private sealed class RecordingFileDownloadService : IFileDownloadService
    {
        public int InvocationCount { get; private set; }

        public Task DownloadAsync(
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
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ParallelTrackingFileDownloadService(
        byte[] content,
        int expectedBlockedCount) : IFileDownloadService
    {
        private int startedCount;
        private int currentConcurrency;
        private int maximumConcurrency;

        public int StartedCount => Volatile.Read(ref startedCount);

        public int MaximumConcurrency => Volatile.Read(ref maximumConcurrency);

        public TaskCompletionSource ExpectedBlockedCountReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DownloadAsync(
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
            CancellationToken cancellationToken)
        {
            var started = Interlocked.Increment(ref startedCount);
            var current = Interlocked.Increment(ref currentConcurrency);
            UpdateMaximum(current);
            if (started == expectedBlockedCount)
            {
                ExpectedBlockedCountReached.TrySetResult();
            }

            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                await pauseAwaiter();
                Directory.CreateDirectory(Path.GetDirectoryName(targetTempPath)!);
                await File.WriteAllBytesAsync(targetTempPath, content, cancellationToken);
                await onProgressAsync(content.Length, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref currentConcurrency);
            }
        }

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var currentMaximum = Volatile.Read(ref maximumConcurrency);
                if (currentMaximum >= value
                    || Interlocked.CompareExchange(ref maximumConcurrency, value, currentMaximum) == currentMaximum)
                {
                    return;
                }
            }
        }
    }

    private sealed class VerificationRetryFileDownloadService(
        byte[] firstContent,
        byte[] retryContent) : IFileDownloadService
    {
        public int InvocationCount { get; private set; }

        public async Task DownloadAsync(
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
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            var content = InvocationCount == 1 ? firstContent : retryContent;
            Directory.CreateDirectory(Path.GetDirectoryName(targetTempPath)!);
            await File.WriteAllBytesAsync(targetTempPath, content, cancellationToken);
            await onProgressAsync(content.Length, cancellationToken);
        }
    }

    private sealed class BlockingManifestHandler : HttpMessageHandler
    {
        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            var requestUri = request.RequestUri?.ToString() ?? "";
            var json = requestUri.Contains("/api/launcher/game/config/json", StringComparison.Ordinal)
                ? "{\"code\":200,\"data\":{\"url\":\"https://manifest.example.invalid/manifest.json\"}}"
                : "{\"source\":\"\",\"file\":[]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class LatestManifestOnlyHandler : HttpMessageHandler
    {
        public int ManifestUrlRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri?.ToString() ?? "";
            string json;
            if (requestUri.Contains("/api/launcher/game/config/json", StringComparison.Ordinal))
            {
                ManifestUrlRequestCount++;
                json = "{\"code\":200,\"data\":{\"url\":\"https://manifest.example.invalid/manifest.json\"}}";
            }
            else
            {
                json = "{\"source\":\"\",\"file\":[]}";
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RetryContentHandler : HttpMessageHandler
    {
        private readonly byte[] expectedBytes;

        public RetryContentHandler(byte[] expectedBytes)
        {
            this.expectedBytes = expectedBytes;
        }

        public List<string> RequestHosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri?.Host ?? "";
            RequestHosts.Add(host);
            var content = host == "primary.example.invalid"
                ? Encoding.UTF8.GetBytes("wrong-content")
                : expectedBytes;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class CountingHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class AlwaysWrongContentHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("wrong-content"))
            });
        }
    }

    private sealed class RangeIgnoredHandler(byte[] content) : HttpMessageHandler
    {
        public bool RangeWasRequested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RangeWasRequested = request.Headers.Range is not null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class InvalidRangeThenCompleteHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                var partialContent = new ByteArrayContent(content[4..]);
                partialContent.Headers.ContentRange =
                    new System.Net.Http.Headers.ContentRangeHeaderValue(3, content.Length - 1, content.Length);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = partialContent
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class InvalidContentLengthThenCompleteHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                var partialContent = new ByteArrayContent(content[4..]);
                partialContent.Headers.ContentRange =
                    new System.Net.Http.Headers.ContentRangeHeaderValue(4, content.Length - 1, content.Length + 1);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = partialContent
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class InterruptedTransferHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public long? SecondRequestRangeStart { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new InterruptedReadStream(content, bytesBeforeFailure: 4))
                });
            }

            if (RequestCount == 2)
            {
                SecondRequestRangeStart = request.Headers.Range?.Ranges.Single().From;
                var partialContent = new ByteArrayContent(content[4..]);
                partialContent.Headers.ContentRange =
                    new System.Net.Http.Headers.ContentRangeHeaderValue(4, content.Length - 1, content.Length);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = partialContent
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class InterruptedReadStream(byte[] content, int bytesBeforeFailure) : Stream
    {
        private int position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (position >= bytesBeforeFailure)
            {
                throw new IOException("Simulated interrupted transfer.");
            }

            var bytesToCopy = Math.Min(count, bytesBeforeFailure - position);
            Array.Copy(content, position, buffer, offset, bytesToCopy);
            position += bytesToCopy;
            return bytesToCopy;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position >= bytesBeforeFailure)
            {
                return ValueTask.FromException<int>(new IOException("Simulated interrupted transfer."));
            }

            var bytesToCopy = Math.Min(buffer.Length, bytesBeforeFailure - position);
            content.AsMemory(position, bytesToCopy).CopyTo(buffer);
            position += bytesToCopy;
            return ValueTask.FromResult(bytesToCopy);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
