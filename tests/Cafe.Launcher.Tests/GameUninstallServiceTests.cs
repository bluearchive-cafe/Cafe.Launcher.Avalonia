using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.UI.Services.GameRuntime;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

/// <summary>
/// <see cref="GameUninstallService"/> 删除路径的补充测试：清单文件被占用时的部分失败、
/// 游戏目录缺失时的守卫，以及重复卸载的幂等守卫语义。清单一律通过
/// <see cref="LocalInstallationStateStore.CommitAsync"/> 落盘，检查点存储绑定到测试临时目录，
/// 避免触及真实用户数据。
/// </summary>
[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class GameUninstallServiceTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    /// <summary>
    /// 实测的 Blue Archive 启动配置（ADR-032）：宿主名是配置里的 <c>name</c>，游戏可执行文件
    /// 在 <c>params</c> 里；反作弊宿主是宿主名去掉 <c>_loader_x64</c> 的同族短名，它不在配置里。
    /// 需要「判据来自配置」的用例用这一组，而不是与本判据无关的合成名。
    /// </summary>
    private const string LoaderExecutableName = "xldr_BlueArchiveOnline_JP_loader_x64";

    private const string GameExecutableName = "BlueArchive";

    private const string AntiCheatSiblingName = "xldr_BlueArchiveOnline_JP";

    private static readonly string[] LaunchParameters = ["BlueArchive.exe"];

    [Fact]
    public async Task UninstallAsync_WhenDeletingUnmanagedResources_ReportsProgressAndFinishesAfterShortcutCleanup()
    {
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "managed.bin");
        for (var index = 0; index < 200; index++)
        {
            await WriteGameFileAsync(gamePath, $"resources/{index}.bin");
        }

        var store = await CreateCommittedStoreAsync(gamePath, "managed.bin");
        var snapshot = Snapshot(await store.ReadAsync(gamePath));
        var shortcut = new TestGameShortcutService();
        var service = CreateService(store, shortcutService: shortcut);
        var updates = new List<GameOperationProgress>();
        var sawPartialDeletion = false;

        var result = await service.UninstallAsync(snapshot, UninstallScope.GameDirectory, update =>
        {
            updates.Add(update);
            if (update.Progress == 100)
            {
                Assert.False(Directory.Exists(gamePath));
                Assert.Equal(1, shortcut.DeleteCallCount);
            }
            else if (update.Stage == GameOperationStage.Uninstalling && update.Progress > 0 && Directory.Exists(gamePath))
            {
                var remaining = Directory.EnumerateFiles(gamePath, "*", SearchOption.AllDirectories).Count();
                sawPartialDeletion |= remaining > 0 && remaining < 203;
            }
        });

        Assert.True(result.Success);
        Assert.True(sawPartialDeletion);
        Assert.Equal(GameOperationStage.UninstallScanning, updates[0].Stage);
        Assert.Equal(100, updates[^1].Progress);
        Assert.Equal(205, updates[^1].TotalEntryCount); // 203 files + game/resources directories.
        Assert.Equal(205, updates[^1].ProcessedEntryCount);
        Assert.Equal(203, result.AffectedFileCount);
        Assert.InRange(updates.Count, 4, 99); // Percentage gate plus an explicit final-cleanup stage.
        Assert.Equal(GameOperationStage.UninstallCleanup, updates[^1].Stage);
        Assert.NotNull(result.UninstallDetails);
        Assert.Empty(result.UninstallDetails.Leftovers);
        Assert.All(updates, update => Assert.False(update.CanStop));
        Assert.Equal(updates.Select(update => update.Progress).Order(), updates.Select(update => update.Progress));
        Assert.All(updates.Skip(1).SkipLast(1), update => Assert.InRange(update.Progress, 0, 95));
    }

    [Fact]
    public async Task UninstallAsync_WhenGameStartsDuringScanning_RefusesBeforeDeletingAnything()
    {
        var gamePath = CreateGameDirectory();
        var keep = await WriteGameFileAsync(gamePath, "managed.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "managed.bin");
        var gameStarted = false;
        var service = CreateService(store, processTracker: new GameProcessTracker(
            (_, _) => Task.FromResult<IReadOnlyList<string>>(gameStarted ? ["BlueArchive"] : [])));
        var snapshot = Snapshot(await store.ReadAsync(gamePath));

        var result = await service.UninstallAsync(snapshot, UninstallScope.GameDirectory, update =>
        {
            if (update.Stage == GameOperationStage.UninstallScanning)
            {
                gameStarted = true;
            }
        });

        Assert.False(result.Success);
        Assert.Equal(GameOperationErrorCode.GameRunning, result.ErrorCode);
        Assert.True(File.Exists(keep));
        Assert.Equal(LocalInstallationStateKind.Valid, (await store.ReadAsync(gamePath)).Kind);
    }

    static GameUninstallServiceTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public async Task UninstallAsync_WhenAManifestFileIsReadOnly_RemovesItInsteadOfFailing()
    {
        // 只读属性是手工拷贝过、或被打过更新包标记的文件留下的常见形态。卸载侧此前走裸
        // File.Delete 且不清属性，清单里只要有一个这样的文件就整次卸载以
        // UnauthorizedAccessException 中止——而同一次删除在更新/安装那条路径上是能过的。
        var gamePath = CreateGameDirectory();
        var readOnlyPath = await WriteGameFileAsync(gamePath, "data/readonly.bin");
        var afterPath = await WriteGameFileAsync(gamePath, "data/after.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/readonly.bin", "data/after.bin");
        var localGame = await store.ReadAsync(gamePath);
        Assert.Equal(LocalInstallationStateKind.Valid, localGame.Kind);
        var shortcut = new TestGameShortcutService();
        var service = CreateService(store, shortcutService: shortcut);
        var snapshot = new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.Ready,
            LocalGame = localGame
        };
        File.SetAttributes(readOnlyPath, FileAttributes.ReadOnly);

        var result = await service.UninstallAsync(snapshot, UninstallScope.GameDirectory, _ => { });

        Assert.True(result.Success, result.Message);
        // 只读的那个与它之后的文件都已删除：删除按清单一侧推进，不再中途抛出。
        Assert.False(File.Exists(readOnlyPath));
        Assert.False(File.Exists(afterPath));
        Assert.Equal(1, shortcut.DeleteCallCount);
    }

    [Theory]
    [InlineData(LauncherRuntimeState.RemoteUnavailable)]
    [InlineData(LauncherRuntimeState.BelowLowestVersion)]
    [InlineData(LauncherRuntimeState.UpdateAvailable)]
    [InlineData(LauncherRuntimeState.Corrupted)]
    [InlineData(LauncherRuntimeState.IoFailure)]
    public async Task UninstallAsync_WhenSnapshotDoesNotAllowLaunchButMetadataIsValid_RemovesLocalInstallation(
        LauncherRuntimeState runtimeState)
    {
        var gamePath = CreateGameDirectory();
        var managedPath = await WriteGameFileAsync(gamePath, "data/managed.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var snapshot = Snapshot(await store.ReadAsync(gamePath));
        snapshot.RuntimeState = runtimeState;

        var result = await CreateService(store).UninstallAsync(snapshot, UninstallScope.GameDirectory, _ => { });

        Assert.True(result.Success, result.Message);
        Assert.False(File.Exists(managedPath));
        Assert.Equal(LocalInstallationStateKind.NotInstalled, (await store.ReadAsync(gamePath)).Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UninstallAsync_WhenMetadataIsCorrupted_DeletesTheWholeDirectoryByDefault(bool corruptConfig)
    {
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        await File.WriteAllTextAsync(Path.Combine(gamePath,
            corruptConfig ? LauncherPaths.GameConfigFileName : LauncherPaths.ManifestFileName), "invalid JSON");
        var snapshot = Snapshot(await store.ReadAsync(gamePath));
        snapshot.RuntimeState = LauncherRuntimeState.Corrupted;
        var service = CreateService(store);
        Assert.True((await service.ValidateAsync(gamePath)).Success);
        var progress = new List<GameOperationProgress>();

        var result = await service.UninstallAsync(snapshot, UninstallScope.GameDirectory, progress.Add);

        Assert.True(result.Success, result.Message);
        Assert.False(Directory.Exists(gamePath));
        Assert.Equal(GameOperationStage.UninstallScanning, progress[0].Stage);
        Assert.Equal(100, progress[^1].Progress);
        Assert.All(progress, item => Assert.False(item.CanStop));
    }

    [Fact]
    public async Task UninstallAsync_WhenMetadataBreaksAfterConfirmation_StillDeletesTheConfirmedDirectory()
    {
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var snapshot = Snapshot(await store.ReadAsync(gamePath));
        var service = CreateService(store);
        Assert.True((await service.ValidateAsync(gamePath)).Success);
        await File.WriteAllTextAsync(Path.Combine(gamePath, LauncherPaths.ManifestFileName), "invalid JSON");

        var result = await service.UninstallAsync(snapshot, UninstallScope.GameDirectory, _ => { });

        Assert.True(result.Success, result.Message);
        Assert.False(Directory.Exists(gamePath));
    }

    [Theory]
    [InlineData(LoaderExecutableName)]
    [InlineData(GameExecutableName)]
    [InlineData(AntiCheatSiblingName)]
    public async Task UninstallAsync_WhenMetadataIsCorruptedAndAProcessIsRunning_RefusesEvenWithoutRemoteConfig(
        string runningName)
    {
        var gamePath = CreateGameDirectory();
        var managedPath = await WriteGameFileAsync(gamePath, "data/managed.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        await File.WriteAllTextAsync(Path.Combine(gamePath, LauncherPaths.GameConfigFileName), "invalid JSON");
        var snapshot = Snapshot(await store.ReadAsync(gamePath));
        snapshot.RuntimeState = LauncherRuntimeState.Corrupted;
        var scans = 0;
        var tracker = new GameProcessTracker((query, _) =>
        {
            scans++;
            Assert.True(GameProcessNames.BelongsToFamily(runningName, query.KnownExeNames));
            return Task.FromResult<IReadOnlyList<string>>([runningName]);
        });
        var service = CreateService(store, processTracker: tracker);

        var validation = await service.ValidateAsync(gamePath);
        var result = await service.UninstallAsync(snapshot, UninstallScope.GameDirectoryAndManagedCompatibility, _ => { });

        Assert.False(validation.Success);
        Assert.Equal(GameOperationErrorCode.GameRunning, validation.ErrorCode);
        Assert.False(result.Success);
        Assert.Equal(GameOperationErrorCode.GameRunning, result.ErrorCode);
        Assert.Equal(2, scans);
        Assert.True(File.Exists(managedPath));
    }

    [Fact]
    public async Task UninstallAsync_WhenMetadataCannotBeRead_DoesNotTreatItAsCorrupted()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "共享冲突导致的读取失败只能在 Windows 上复现。");
        var gamePath = CreateGameDirectory();
        var managedPath = await WriteGameFileAsync(gamePath, "data/managed.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var snapshot = Snapshot(await store.ReadAsync(gamePath));
        snapshot.RuntimeState = LauncherRuntimeState.IoFailure;
        using var handle = File.Open(Path.Combine(gamePath, LauncherPaths.GameConfigFileName),
            FileMode.Open, FileAccess.Read, FileShare.None);
        var service = CreateService(store);

        var validation = await service.ValidateAsync(gamePath);
        var result = await service.UninstallAsync(snapshot, UninstallScope.GameDirectoryAndManagedCompatibility, _ => { });

        Assert.False(validation.Success);
        Assert.Equal(GameOperationErrorCode.System, validation.ErrorCode);
        Assert.False(result.Success);
        Assert.Equal(GameOperationErrorCode.System, result.ErrorCode);
        Assert.True(File.Exists(managedPath));
    }

    [Fact]
    public async Task UninstallAsync_WhenAFileIsLocked_RemovesTheOtherFilesAndReportsTheLeftover()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "共享冲突导致的删除失败只能在 Windows 上复现。");
        var gamePath = CreateGameDirectory();
        var beforePath = await WriteGameFileAsync(gamePath, "data/before.bin");
        var lockedPath = await WriteGameFileAsync(gamePath, "data/locked.bin");
        var afterPath = await WriteGameFileAsync(gamePath, "data/after.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/before.bin", "data/locked.bin", "data/after.bin");
        var shortcut = new TestGameShortcutService();
        var service = CreateService(store, shortcutService: shortcut);
        var snapshot = Snapshot(await store.ReadAsync(gamePath));
        var progress = new List<GameOperationProgress>();
        using var handle = File.Open(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var result = await service.UninstallAsync(snapshot, UninstallScope.GameDirectory, progress.Add);

        Assert.True(result.Success, result.Message);
        Assert.Contains(lockedPath, result.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(beforePath));
        Assert.True(File.Exists(lockedPath));
        Assert.False(File.Exists(afterPath));
        Assert.Equal(1, shortcut.DeleteCallCount);
        Assert.Equal(LocalInstallationStateKind.NotInstalled, (await store.ReadAsync(gamePath)).Kind);
        Assert.Equal(100, progress[^1].Progress);
        Assert.Equal(progress[^1].TotalEntryCount, progress[^1].ProcessedEntryCount);
    }

    [Fact]
    public async Task UninstallAsync_WhenGameDirectoryDoesNotExist_ReturnsGuardFailureWithoutSideEffects()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var localizer = new LocalizationService();
        var service = CreateService(new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan), localizer);
        var snapshot = new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.Ready,
            LocalGame = new LocalInstallationState { GamePath = gamePath }
        };
        var progressInvoked = false;

        var result = await service.UninstallAsync(snapshot, UninstallScope.GameDirectory, _ => progressInvoked = true);

        // 幂等守卫语义：目录不存在时按校验失败返回（不抛异常、不产生进度）。
        Assert.False(result.Success);
        Assert.Equal(GameOperationErrorCode.Uninstall, result.ErrorCode);
        Assert.Equal(localizer.F(LocalizationKeys.GamePathMissing, gamePath), result.Message);
        Assert.False(progressInvoked);
    }

    [Fact]
    public async Task UninstallAsync_WhenUsingTheDefaultScope_RemovesUnmanagedResourcesAndTheDirectory()
    {
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        await WriteGameFileAsync(gamePath, "StreamingAssets/game-downloaded.bin");
        await WriteGameFileAsync(gamePath, "user-notes.txt");
        await WriteGameFileAsync(gamePath, "download.tmp");
        Directory.CreateDirectory(Path.Combine(gamePath, "empty-folder"));
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localizer = new LocalizationService();

        var result = await CreateService(store, localizer).UninstallAsync(
            Snapshot(await store.ReadAsync(gamePath)), UninstallScope.GameDirectory, _ => { });

        Assert.True(result.Success, result.Message);
        Assert.False(Directory.Exists(gamePath));
        Assert.Equal(localizer.T(LocalizationKeys.UninstallCompleted), result.Message);
    }

    [Fact]
    public async Task UninstallAsync_WhenThorough_DoesNotClaimTheFolderWasRetained()
    {
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var service = CreateService(store);

        var result = await service.UninstallAsync(Snapshot(localGame), UninstallScope.GameDirectoryAndManagedCompatibility, _ => { });

        Assert.True(result.Success);
        Assert.False(Directory.Exists(gamePath));
        // 目录整棵删掉了，就不许再提「仍位于」——那是被同一次操作当场证伪的一句话（ADR-030 口径）。
        Assert.DoesNotContain(gamePath, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UninstallAsync_LogsTheActualDirectoryDeletionCountsAndBytes()
    {
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        await WriteGameFileAsync(gamePath, "untracked.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var expectedBytes = Directory.EnumerateFiles(gamePath, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
        var diagnostics = new RecordingDiagnostics();
        var service = CreateService(store, diagnostics: diagnostics);

        var result = await service.UninstallAsync(Snapshot(await store.ReadAsync(gamePath)),
            UninstallScope.GameDirectory, _ => { });

        Assert.True(result.Success, result.Message);
        Assert.Equal(4, result.AffectedFileCount);
        Assert.Equal(expectedBytes, result.AffectedBytes);
        var completion = Assert.Single(diagnostics.Messages,
            message => message.Contains("Game uninstall completed", StringComparison.Ordinal));
        Assert.Contains("scope: GameDirectory", completion, StringComparison.Ordinal);
        Assert.Contains($"files removed: 4 ({expectedBytes} bytes)", completion, StringComparison.Ordinal);
        Assert.Contains("leftovers: 0", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest files:", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("directory retained:", completion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_WhenMetadataIsReadable_DoesNotDeleteOrRequireManifestStatistics()
    {
        var gamePath = CreateGameDirectory();
        var managedPath = await WriteGameFileAsync(gamePath, "data/managed.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");

        var validation = await CreateService(store).ValidateAsync(gamePath);

        Assert.True(validation.Success, validation.Message);
        Assert.True(File.Exists(managedPath));
        Assert.True(File.Exists(Path.Combine(gamePath, LauncherPaths.GameConfigFileName)));
    }

    [Fact]
    public async Task UninstallAsync_WhenCalledTwice_SecondCallFailsAsGuardedIdempotentOperation()
    {
        var gamePath = CreateGameDirectory();
        var managedPath = await WriteGameFileAsync(gamePath, "data/managed.bin");
        var unknownPath = await WriteGameFileAsync(gamePath, "unknown.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var service = CreateService(store);
        var snapshot = new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.Ready,
            LocalGame = localGame
        };

        var first = await service.UninstallAsync(snapshot, UninstallScope.GameDirectory, _ => { });
        var second = await service.UninstallAsync(snapshot, UninstallScope.GameDirectory, _ => { });
        var stateAfter = await store.ReadAsync(gamePath);

        // 第一次：整个游戏目录删除，包括清单外文件。
        Assert.True(first.Success);
        Assert.False(File.Exists(managedPath));
        Assert.False(File.Exists(unknownPath));
        // 第二次：游戏目录已经不存在，守卫返回 Uninstall 错误码，
        // 不再触碰文件系统，也不会把首次卸载的结果改写成失败。
        Assert.False(second.Success);
        Assert.Equal(GameOperationErrorCode.Uninstall, second.ErrorCode);
        Assert.False(File.Exists(managedPath));
        Assert.False(File.Exists(unknownPath));
        Assert.Equal(LocalInstallationStateKind.NotInstalled, stateAfter.Kind);
    }

    [Fact]
    public async Task UninstallAsync_WhenUsingTheDefaultScope_RemovesTheDesktopShortcutAndDirectory()
    {
        var gamePath = CreateGameDirectory();
        var managedPath = await WriteGameFileAsync(gamePath, "data/managed.bin");
        var unknownPath = await WriteGameFileAsync(gamePath, "unknown.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var shortcut = new TestGameShortcutService();
        var service = CreateService(store, shortcutService: shortcut);

        var result = await service.UninstallAsync(Snapshot(localGame), UninstallScope.GameDirectory, _ => { });

        Assert.True(result.Success);
        // 默认卸载删除目录及其清单外内容，同时移除桌面快捷方式（ADR-046）。
        Assert.Equal(1, shortcut.DeleteCallCount);
        Assert.False(File.Exists(managedPath));
        Assert.False(File.Exists(unknownPath));
        Assert.False(Directory.Exists(gamePath));
    }

    [Fact]
    public async Task UninstallAsync_WhenScopeIsThorough_RemovesTheWholeInstallDirectory()
    {
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        await WriteGameFileAsync(gamePath, "user-notes.txt");
        await WriteGameFileAsync(gamePath, "data/patch.bin.tmp");
        Directory.CreateDirectory(Path.Combine(gamePath, "empty-folder"));
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var diagnostics = new RecordingDiagnostics();
        var service = CreateService(store, diagnostics: diagnostics);

        var result = await service.UninstallAsync(Snapshot(localGame), UninstallScope.GameDirectoryAndManagedCompatibility, _ => { });

        Assert.True(result.Success);
        // 整棵安装目录连清单外残留、暂存文件与空目录一起消失。
        Assert.False(Directory.Exists(gamePath));
        // 这一态才配得上 leftovers（它是实测的），也才不该出现「目录已保留」。
        var completion = Assert.Single(
            diagnostics.Messages,
            message => message.Contains("Game uninstall completed", StringComparison.Ordinal));
        Assert.Contains("scope: GameDirectoryAndManagedCompatibility", completion, StringComparison.Ordinal);
        Assert.Contains("leftovers: 0", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("directory retained", completion, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UninstallAsync_WithManagedCompatibilityOption_DeletesPrefixOnlyWhenSelected(bool cleanCompatibility)
    {
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        var managedPrefixRoot = GameCompatibilityPaths.GetDefaultGameCompatibilityRoot(LauncherProfiles.BlueArchiveJapan.RuntimeId);
        var prefixMarker = Path.Combine(managedPrefixRoot, "wine", "prefix", "drive_c", "marker.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(prefixMarker)!);
        await File.WriteAllTextAsync(prefixMarker, "prefix");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var service = CreateService(store);

        try
        {
            var result = await service.UninstallAsync(Snapshot(localGame),
                cleanCompatibility ? UninstallScope.GameDirectoryAndManagedCompatibility : UninstallScope.GameDirectory,
                _ => { });

            Assert.True(result.Success);
            Assert.False(Directory.Exists(gamePath));
            Assert.Equal(!cleanCompatibility, File.Exists(prefixMarker));
            Assert.Equal(!cleanCompatibility, Directory.Exists(managedPrefixRoot));
        }
        finally
        {
            if (Directory.Exists(managedPrefixRoot))
            {
                Directory.Delete(managedPrefixRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UninstallAsync_WhenCustomPrefixIsOutsideTheManagedRoot_KeepsItAndSaysSo()
    {
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        var customPrefix = Path.Combine(tempDir, "shared-wine-prefix");
        Directory.CreateDirectory(customPrefix);
        await File.WriteAllTextAsync(Path.Combine(customPrefix, "user.reg"), "reg");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var service = CreateService(store);

        var result = await service.UninstallAsync(
            Snapshot(localGame, customPrefix),
            UninstallScope.GameDirectoryAndManagedCompatibility,
            _ => { });

        Assert.True(result.Success);
        // 用户自选的前缀可能是与别的东西共用的目录：保留，并在完成文案里说清楚（ADR-030）。
        Assert.True(Directory.Exists(customPrefix));
        Assert.Contains(customPrefix, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UninstallAsync_WhenPrefixLivesInsideTheInstallDirectory_DoesNotClaimItWasKept()
    {
        // PrefixPath 是自由文本：把它填进安装目录（便携安装）也是合法配置。而安装目录整棵正是
        // 彻底清除的删除目标之一，树都删完了还报「已保留」是被同一次操作当场证伪的一句话。
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        var embeddedPrefix = Path.Combine(gamePath, "wine-prefix");
        Directory.CreateDirectory(embeddedPrefix);
        await File.WriteAllTextAsync(Path.Combine(embeddedPrefix, "user.reg"), "reg");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var localizer = new LocalizationService();
        var service = CreateService(store, localizer);

        var result = await service.UninstallAsync(
            Snapshot(localGame, embeddedPrefix),
            UninstallScope.GameDirectoryAndManagedCompatibility,
            _ => { });

        Assert.True(result.Success);
        Assert.False(Directory.Exists(gamePath));
        Assert.DoesNotContain(embeddedPrefix, result.Message, StringComparison.Ordinal);
        Assert.Equal(localizer.T(LocalizationKeys.UninstallCompleted), result.Message);
    }

    [Fact]
    public void BuildCompletionMessage_WithLeftoversAndKeptPrefix_StatesBoth()
    {
        // 复核轮：两个事实缺一不可。旧写法是二选一——只要有名有姓的残留，就把「Prefix 已保留」
        // 整条吞掉，而那个目录可能有几十 GB，用户只能自己去猜它还在不在。直接驱动文案构造，
        // 因此这一态不再需要「先造出一个删不掉的条目」（那件事只有 Windows 能复现）。
        var localizer = new LocalizationService();
        var message = GameUninstallService.BuildCompletionMessage(
            localizer,
            [@"C:\game\StreamingAssets\Xigncode:{GUID}"],
            @"D:\shared-wine-prefix");

        Assert.Contains("Xigncode:{GUID}", message, StringComparison.Ordinal);
        Assert.Contains(@"D:\shared-wine-prefix", message, StringComparison.Ordinal);
        // 占位符全部被消费：资源串与实参个数不匹配会在这里现形。
        Assert.DoesNotContain("{0}", message, StringComparison.Ordinal);
        Assert.DoesNotContain("{1}", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCompletionMessage_ForEveryCombination_ReportsOnlyWhatHappened()
    {
        var localizer = new LocalizationService();
        string[] leftovers = [@"C:\game\stuck.bin"];
        const string prefix = @"D:\shared-wine-prefix";

        Assert.Equal(
            localizer.T(LocalizationKeys.UninstallCompleted),
            GameUninstallService.BuildCompletionMessage(localizer, [], null));
        Assert.Equal(
            localizer.F(LocalizationKeys.UninstallCompletedKeptPrefix, prefix),
            GameUninstallService.BuildCompletionMessage(localizer, [], prefix));
        Assert.Equal(
            localizer.F(LocalizationKeys.UninstallCompletedWithLeftovers, leftovers[0]),
            GameUninstallService.BuildCompletionMessage(localizer, leftovers, null));
        Assert.Equal(
            localizer.F(LocalizationKeys.UninstallCompletedWithLeftoversKeptPrefix, leftovers[0], prefix),
            GameUninstallService.BuildCompletionMessage(localizer, leftovers, prefix));
        // 没保留就不许提保留，没残留就不许提残留——反向也要钉住，否则「多报一句」不会被发现。
        Assert.DoesNotContain(prefix, GameUninstallService.BuildCompletionMessage(localizer, leftovers, null), StringComparison.Ordinal);
        Assert.DoesNotContain(leftovers[0], GameUninstallService.BuildCompletionMessage(localizer, [], prefix), StringComparison.Ordinal);
    }



    [Fact]
    public void BuildCompletionMessage_WithMoreLeftoversThanTheCap_NamesOnlyTheFirstFew()
    {
        var localizer = new LocalizationService();
        var leftovers = Enumerable.Range(0, 6).Select(index => $@"C:\game\leftover-{index}.bin").ToArray();

        var message = GameUninstallService.BuildCompletionMessage(localizer, leftovers, null);

        // 上限只防病态清单把提示撑爆；完整清单留在日志里（诊断侧已单独记录）。
        Assert.Contains("leftover-4.bin", message, StringComparison.Ordinal);
        Assert.DoesNotContain("leftover-5.bin", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UninstallAsync_WhenThoroughCleanupCannotRemoveSomething_StillSucceedsAndNamesIt()
    {
        // 实机回归（2026-09-15）：反作弊留下的目录项在用户态删不掉。删不掉的项目不该把一次
        // 已经完成的卸载报成「卸载失败」——manifest 与两个状态文件都已删除，游戏确实卸载了——
        // 但留下来的东西必须点名，否则「彻底清除」说得比做得多（ADR-030）。
        // 用例靠独占句柄制造「删不掉的条目」：那是 Windows 的文件共享语义，POSIX 允许 unlink
        // 已打开的文件，Linux 上删除照常成功、残留清单为空——所以可见跳过而不是让它在
        // Linux 上红（同族先例：本文件的 UninstallAsync_WhenManifestFileIsLocked_...）。
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "删不掉的文件条目只能在 Windows 上用打开的句柄复现：POSIX 允许 unlink 已打开的文件。");
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        var lockedPath = Path.Combine(gamePath, "data", "locked.bin");
        await File.WriteAllTextAsync(lockedPath, "x");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var service = CreateService(store);

        using var handle = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var result = await service.UninstallAsync(Snapshot(localGame), UninstallScope.GameDirectoryAndManagedCompatibility, _ => { });

        Assert.True(result.Success);
        Assert.Contains(lockedPath, result.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(lockedPath));
    }

    [Fact]
    public async Task UninstallAsync_WhenOnlyTheAntiCheatHostIsStillRunning_RefusesAndNamesIt()
    {
        // 闸门要认整族进程，而不是只认配置里那个宿主（ADR-032）：反作弊宿主的名字不是宿主名，
        // 只认宿主就会放行，接着整棵删除撞在仍被占用的目录上——实机就是这么留下残留的。
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        // 配置按游戏自己带来的那份写：宿主名 + 启动参数里的游戏可执行文件。写成一个与判据
        // 无关的合成名（本文件其它用例的默认值）就会让「家族来自配置」这件事无从检验——
        // 替身照答不误，把 FromLaunchConfiguration(name, null) 这样的回归放过去。
        var store = await CreateCommittedStoreAsync(
            gamePath,
            LoaderExecutableName,
            LaunchParameters,
            "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var asked = new List<IReadOnlyList<string>>();
        var service = CreateService(
            store,
            processTracker: new GameProcessTracker((names, _) =>
            {
                asked.Add(names.KnownExeNames);
                // 与真实判据同形：只有请求的族里含配置宿主时，反作弊宿主才落在族内
                // （ExtendsProcessName 那条规则的前提），否则它根本不该被判据认领。
                return Task.FromResult<IReadOnlyList<string>>(
                    names.KnownExeNames.Contains(LoaderExecutableName, StringComparer.OrdinalIgnoreCase)
                        ? [AntiCheatSiblingName]
                        : []);
            }));

        var result = await service.UninstallAsync(Snapshot(localGame), UninstallScope.GameDirectoryAndManagedCompatibility, _ => { });

        Assert.False(result.Success);
        Assert.Equal(GameOperationErrorCode.GameRunning, result.ErrorCode);
        Assert.Contains(AntiCheatSiblingName, result.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(gamePath));

        // 判据确实来自配置的 name + params：漏掉 params 里的游戏可执行文件，
        // 或干脆拿一个空判据去问，这两条断言就红——这才是「只认宿主时会放行」的守卫形态。
        Assert.NotEmpty(asked);
        Assert.All(
            asked,
            names =>
            {
                Assert.Contains(LoaderExecutableName, names, StringComparer.OrdinalIgnoreCase);
                Assert.Contains(GameExecutableName, names, StringComparer.OrdinalIgnoreCase);
            });
    }

    [Fact]
    public async Task UninstallAsync_WhenTheGameStartedAfterThePrecheck_RefusesAndDeletesNothing()
    {
        // 确认框打开期间游戏可能被外部起来（桌面快捷方式、Steam）：预检答的是「点卸载那一刻」，
        // 而那个答复在用户读完文案再点确认时可能已经过期。删除前必须复查同一道闸门
        // （AUD-ARCH-008）。用例把两次探测分开：预检时没在跑，删除时在跑。
        var gamePath = CreateGameDirectory();
        var managedPath = await WriteGameFileAsync(gamePath, "data/managed.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var gameStarted = false;
        var shortcut = new TestGameShortcutService();
        var service = CreateService(
            store,
            shortcutService: shortcut,
            processTracker: new GameProcessTracker(
                (_, _) => Task.FromResult<IReadOnlyList<string>>(gameStarted ? ["BlueArchive"] : [])));

        var validation = await service.ValidateAsync(gamePath);
        Assert.True(validation.Success);

        gameStarted = true;
        var result = await service.UninstallAsync(Snapshot(localGame), UninstallScope.GameDirectoryAndManagedCompatibility, _ => { });

        Assert.False(result.Success);
        Assert.Equal(GameOperationErrorCode.GameRunning, result.ErrorCode);
        Assert.Contains("BlueArchive.exe", result.Message, StringComparison.Ordinal);
        // 拒绝发生在删任何东西之前：清单文件、安装状态、整个目录与桌面快捷方式都原样。
        Assert.True(File.Exists(managedPath));
        Assert.True(Directory.Exists(gamePath));
        Assert.Equal(LocalInstallationStateKind.Valid, (await store.ReadAsync(gamePath)).Kind);
        Assert.Equal(0, shortcut.DeleteCallCount);
    }

    [Fact]
    public async Task MeasureFootprintAsync_WhenTargetsExist_ReportsBothTreeSizes()
    {
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        await WriteGameFileAsync(gamePath, "unknown.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var managedPrefixRoot = GameCompatibilityPaths.GetDefaultGameCompatibilityRoot(LauncherProfiles.BlueArchiveJapan.RuntimeId);
        var prefixMarker = Path.Combine(managedPrefixRoot, "wine", "prefix", "marker.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(prefixMarker)!);
        await File.WriteAllTextAsync(prefixMarker, "0123456789");
        var service = CreateService(store);
        // 期望值在测试里另行枚举，不复用被测的测量实现。
        var expectedInstallBytes = Directory
            .EnumerateFiles(gamePath, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);

        try
        {
            var footprint = await service.MeasureFootprintAsync(Snapshot(localGame));

            Assert.Equal(expectedInstallBytes, footprint.InstallDirectoryBytes);
            Assert.Equal(10, footprint.PrefixBytes);
        }
        finally
        {
            if (Directory.Exists(managedPrefixRoot))
            {
                Directory.Delete(managedPrefixRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UninstallAsync_WhenGameRootIsAReparsePoint_ReportsFailureInsteadOfThrowing()
    {
        // 把游戏挂到别的盘（junction）是合法布局：整棵递归删除会删到本启动器管不到的地方，
        // 守卫必须拒绝，且拒绝要发生在删任何东西之前（ADR-030）。
        var realGamePath = Path.Combine(tempDir, "real", "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(realGamePath);
        var managedPath = await WriteGameFileAsync(realGamePath, "data/managed.bin");
        var store = await CreateCommittedStoreAsync(realGamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(realGamePath);
        Assert.Equal(LocalInstallationStateKind.Valid, localGame.Kind);
        var linkedGamePath = Path.Combine(tempDir, "linked", "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(Path.GetDirectoryName(linkedGamePath)!);
        TestSymlinks.CreateDirectorySymbolicLinkOrSkip(linkedGamePath, realGamePath);
        var service = CreateService(store);
        var linkedGame = new LocalInstallationState
        {
            Kind = localGame.Kind,
            GamePath = linkedGamePath,
            ConfigPath = localGame.ConfigPath,
            ManifestPath = localGame.ManifestPath,
            GameConfig = localGame.GameConfig,
            Manifest = localGame.Manifest
        };

        var result = await service.UninstallAsync(Snapshot(linkedGame), UninstallScope.GameDirectoryAndManagedCompatibility, _ => { });

        Assert.False(result.Success);
        Assert.Equal(GameOperationErrorCode.System, result.ErrorCode);
        // 拒绝理由本地化：守卫抛的是仓库自己写的英文（"…Refusing to delete a reparse point…"），
        // 它只该进日志（2026-09-15 复核轮）。
        var localizer = new LocalizationService();
        Assert.Equal(localizer.F(LocalizationKeys.UninstallRefusedByPathGuard, linkedGamePath), result.Message);
        Assert.DoesNotContain("reparse", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Refusing", result.Message, StringComparison.Ordinal);
        // 拒绝先于任何删除：清单文件、状态文件与链接都还在。
        Assert.True(File.Exists(managedPath));
        Assert.True(File.Exists(Path.Combine(realGamePath, "manifest.json")));
        Assert.True(Directory.Exists(linkedGamePath));
    }

    private static LauncherStatusSnapshot Snapshot(LocalInstallationState localGame, string? prefixPath = null) =>
        new()
        {
            RuntimeState = LauncherRuntimeState.Ready,
            LocalGame = localGame,
            Settings = new LauncherSettings
            {
                GameRuntime = new GameRuntimeSettings { PrefixPath = prefixPath }
            }
        };

    private string CreateGameDirectory()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
        return gamePath;
    }

    private static async Task<string> WriteGameFileAsync(string gamePath, string relativePath)
    {
        var fullPath = Path.Combine(gamePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, $"content:{relativePath}");
        return fullPath;
    }

    /// <summary>为给定清单文件落盘安装状态，返回已初始化的存储实例。</summary>
    private static Task<LocalInstallationStateStore> CreateCommittedStoreAsync(
        string gamePath,
        params string[] manifestPaths) =>
        CreateCommittedStoreAsync(
            gamePath,
            $"CafeLauncherTest{Guid.NewGuid():N}",
            [],
            manifestPaths);

    /// <summary>
    /// 同上，但启动配置由调用方给定。需要检验「判据来自配置」的用例必须走这一条：默认的合成名
    /// 与进程判据毫无关系，用它的替身只能证明「探针被调用过」，证明不了「判据是按配置算出来的」。
    /// </summary>
    private static async Task<LocalInstallationStateStore> CreateCommittedStoreAsync(
        string gamePath,
        string executableName,
        IReadOnlyList<string> launchParameters,
        params string[] manifestPaths)
    {
        var store = new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan);
        var committed = await store.CommitAsync(
            gamePath,
            new LocalInstallationStateCommit(
                "1.0.0",
                "manifest.json",
                executableName,
                launchParameters,
                [.. manifestPaths.Select(path => new LocalInstallationFile(
                    path,
                    new FileInfo(Path.Combine(gamePath, path.Replace('/', Path.DirectorySeparatorChar))).Length,
                    "0"))]));
        Assert.Equal(LocalInstallationStateKind.Valid, committed.Kind);
        return store;
    }

    private GameUninstallService CreateService(
        LocalInstallationStateStore store,
        LocalizationService? localizer = null,
        TestGameShortcutService? shortcutService = null,
        IGameProcessTracker? processTracker = null,
        ILauncherDiagnostics? diagnostics = null)
    {
        // 检查点存储绑定到测试临时目录，避免卸载成功路径清除真实用户目录中的续传标记。
        return new GameUninstallService(LauncherProfiles.BlueArchiveJapan, 
            store,
            diagnostics ?? new LocalDiagnostics(),
            localizer ?? new LocalizationService(),
            new GameInstallationPath(LauncherProfiles.BlueArchiveJapan),
            new DownloadCheckpointStore( tempDir.DataRoot ),
            processTracker ?? TestGameProcessTracker.None(),
            shortcutService ?? new TestGameShortcutService());
    }

    /// <summary>
    /// 记录诊断调用的替身：卸载的完成日志是排查这类反馈的唯一书面凭据（2026-09-29 反馈轮），
    /// 因此它需要有断言的地方，而不是只落到测试进程的临时日志文件里。
    /// </summary>
    private sealed class RecordingDiagnostics : ILauncherDiagnostics
    {
        public List<string> Messages { get; } = [];

        public Task DebugAsync(
            string title,
            string? message = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task MessageAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            Messages.Add($"{title}: {message}");
            return Task.CompletedTask;
        }

        public Task WarningAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            Messages.Add($"Warn: {title}: {message}");
            return Task.CompletedTask;
        }

        public void LogMessage(LogEntrySeverity severity, string title, string? message = null) =>
            Messages.Add($"{severity}: {title}: {message}");

        public Task ErrorAsync(
            string title,
            string? message,
            Exception exception,
            CancellationToken cancellationToken = default)
        {
            Messages.Add($"Error: {title}: {message}");
            return Task.CompletedTask;
        }

        public Task ErrorAsync(string title, Exception exception, CancellationToken cancellationToken = default) =>
            ErrorAsync(title, exception.Message, exception, cancellationToken);

        public Task VerboseAsync(
            string title,
            string? message = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task FatalAsync(string title, Exception exception, CancellationToken cancellationToken = default) =>
            ErrorAsync(title, exception.Message, exception, cancellationToken);

        public string LogFilePath => string.Empty;

        public LogEntrySeverity MinimumLevel => LogEntrySeverity.Info;

        public void SetMinimumLevel(LogEntrySeverity severity)
        {
        }
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
