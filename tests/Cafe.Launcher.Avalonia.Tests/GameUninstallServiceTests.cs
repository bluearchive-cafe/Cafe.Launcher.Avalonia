using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// <see cref="GameUninstallService"/> 删除路径的补充测试：清单文件被占用时的部分失败、
/// 游戏目录缺失时的守卫，以及重复卸载的幂等守卫语义。清单一律通过
/// <see cref="LocalInstallationStateStore.CommitAsync"/> 落盘，检查点存储绑定到测试临时目录，
/// 避免触及真实用户数据。
/// </summary>
[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class GameUninstallServiceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    static GameUninstallServiceTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public async Task UninstallAsync_WhenManifestFileIsLocked_FailsAndKeepsRemainingFiles()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "共享冲突导致的删除失败只能在 Windows 上用打开的文件流复现。");
        var gamePath = CreateGameDirectory();
        var beforePath = await WriteGameFileAsync(gamePath, "data/before.bin");
        var lockedPath = await WriteGameFileAsync(gamePath, "data/locked.bin");
        var afterPath = await WriteGameFileAsync(gamePath, "data/after.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/before.bin", "data/locked.bin", "data/after.bin");
        var localGame = await store.ReadAsync(gamePath);
        Assert.Equal(LocalInstallationStateKind.Valid, localGame.Kind);
        var shortcut = new TestGameShortcutService();
        var service = CreateService(store, shortcutService: shortcut);
        var snapshot = new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.Ready,
            LocalGame = localGame
        };
        var progress = new List<GameOperationProgress>();
        // FileShare.None 独占打开：File.Delete 将因共享冲突抛出 IOException。
        await using var lockStream = File.Open(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await service.UninstallAsync(snapshot, UninstallScope.ManifestFilesOnly, progress.Add);
        var stateAfter = await store.ReadAsync(gamePath);
        await lockStream.DisposeAsync();

        // 实现语义：IO 异常中止整个卸载并上报 System 错误，错误信息带出被锁文件路径。
        Assert.False(result.Success);
        Assert.Equal(GameOperationErrorCode.System, result.ErrorCode);
        Assert.Contains(lockedPath, result.Message, StringComparison.Ordinal);
        // 被锁文件之前的文件已删除，被锁文件与其后的文件保持原样（删除按清单一侧推进）。
        Assert.False(File.Exists(beforePath));
        Assert.True(File.Exists(lockedPath));
        Assert.True(File.Exists(afterPath));
        // 卸载没有走到成功那一半，桌面快捷方式必须保留（ADR-030）。
        Assert.Equal(0, shortcut.DeleteCallCount);
        // 安装状态尚未进入删除阶段：状态仍为 Valid，目录结构完整保留。
        Assert.Equal(LocalInstallationStateKind.Valid, stateAfter.Kind);
        // 仅成功删除的首个文件上报了一次卸载进度，随后被锁中断。
        var uninstalling = progress.Where(item => item.Stage == GameOperationStage.Uninstalling).ToList();
        Assert.Single(uninstalling);
        Assert.Equal(33, uninstalling[0].Progress);
    }

    [Fact]
    public async Task UninstallAsync_WhenGameDirectoryDoesNotExist_ReturnsGuardFailureWithoutSideEffects()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        var localizer = new LocalizationService();
        var service = CreateService(new LocalInstallationStateStore(), localizer);
        var snapshot = new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.Ready,
            LocalGame = new LocalInstallationState { GamePath = gamePath }
        };
        var progressInvoked = false;

        var result = await service.UninstallAsync(snapshot, UninstallScope.ManifestFilesOnly, _ => progressInvoked = true);

        // 幂等守卫语义：目录不存在时按校验失败返回（不抛异常、不产生进度）。
        Assert.False(result.Success);
        Assert.Equal(GameOperationErrorCode.Uninstall, result.ErrorCode);
        Assert.Equal(localizer.F(LocalizationKeys.GamePathMissing, gamePath), result.Message);
        Assert.False(progressInvoked);
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

        var first = await service.UninstallAsync(snapshot, UninstallScope.ManifestFilesOnly, _ => { });
        var second = await service.UninstallAsync(snapshot, UninstallScope.ManifestFilesOnly, _ => { });
        var stateAfter = await store.ReadAsync(gamePath);

        // 第一次：清单文件删除、安装状态被清除，非清单文件不受影响。
        Assert.True(first.Success);
        Assert.False(File.Exists(managedPath));
        Assert.True(File.Exists(unknownPath));
        // 第二次：安装状态已不存在，守卫按元数据缺失拒绝并返回 Uninstall 错误码，
        // 不再触碰文件系统，也不会把首次卸载的结果改写成失败。
        Assert.False(second.Success);
        Assert.Equal(GameOperationErrorCode.Uninstall, second.ErrorCode);
        Assert.False(File.Exists(managedPath));
        Assert.True(File.Exists(unknownPath));
        Assert.Equal(LocalInstallationStateKind.NotInstalled, stateAfter.Kind);
    }

    [Fact]
    public async Task UninstallAsync_WhenManifestOnly_RemovesTheDesktopShortcutAndKeepsTheDirectory()
    {
        var gamePath = CreateGameDirectory();
        var managedPath = await WriteGameFileAsync(gamePath, "data/managed.bin");
        var unknownPath = await WriteGameFileAsync(gamePath, "unknown.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var shortcut = new TestGameShortcutService();
        var service = CreateService(store, shortcutService: shortcut);

        var result = await service.UninstallAsync(Snapshot(localGame), UninstallScope.ManifestFilesOnly, _ => { });

        Assert.True(result.Success);
        // 标准卸载也删桌面快捷方式，但清单外的文件与目录本身原样保留（ADR-030）。
        Assert.Equal(1, shortcut.DeleteCallCount);
        Assert.False(File.Exists(managedPath));
        Assert.True(File.Exists(unknownPath));
        Assert.True(Directory.Exists(gamePath));
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
        var service = CreateService(store);

        var result = await service.UninstallAsync(Snapshot(localGame), UninstallScope.ThoroughCleanup, _ => { });

        Assert.True(result.Success);
        // 整棵安装目录连清单外残留、暂存文件与空目录一起消失。
        Assert.False(Directory.Exists(gamePath));
    }

    [Fact]
    public async Task UninstallAsync_WhenScopeIsThorough_RemovesTheManagedCompatibilitySubtree()
    {
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        var managedPrefixRoot = GameCompatibilityPaths.GetDefaultGameCompatibilityRoot(GameRuntimeIds.BlueArchiveJapan);
        var prefixMarker = Path.Combine(managedPrefixRoot, "wine", "prefix", "drive_c", "marker.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(prefixMarker)!);
        await File.WriteAllTextAsync(prefixMarker, "prefix");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var service = CreateService(store);

        try
        {
            var result = await service.UninstallAsync(Snapshot(localGame), UninstallScope.ThoroughCleanup, _ => { });

            Assert.True(result.Success);
            // 受管子树的消费者是运行器，不是清单：彻底清除按目录删，不看清单。
            Assert.False(Directory.Exists(managedPrefixRoot));
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
            UninstallScope.ThoroughCleanup,
            _ => { });

        Assert.True(result.Success);
        // 用户自选的前缀可能是与别的东西共用的目录：保留，并在完成文案里说清楚（ADR-030）。
        Assert.True(Directory.Exists(customPrefix));
        Assert.Contains(customPrefix, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MeasureFootprintAsync_WhenTargetsExist_ReportsBothTreeSizes()
    {
        var gamePath = CreateGameDirectory();
        await WriteGameFileAsync(gamePath, "data/managed.bin");
        await WriteGameFileAsync(gamePath, "unknown.bin");
        var store = await CreateCommittedStoreAsync(gamePath, "data/managed.bin");
        var localGame = await store.ReadAsync(gamePath);
        var managedPrefixRoot = GameCompatibilityPaths.GetDefaultGameCompatibilityRoot(GameRuntimeIds.BlueArchiveJapan);
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

        var result = await service.UninstallAsync(Snapshot(linkedGame), UninstallScope.ThoroughCleanup, _ => { });

        Assert.False(result.Success);
        Assert.Contains("reparse", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GameOperationErrorCode.System, result.ErrorCode);
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
    private static async Task<LocalInstallationStateStore> CreateCommittedStoreAsync(
        string gamePath,
        params string[] manifestPaths)
    {
        var store = new LocalInstallationStateStore();
        var committed = await store.CommitAsync(
            gamePath,
            new LocalInstallationStateCommit(
                "1.0.0",
                "manifest.json",
                $"CafeLauncherTest{Guid.NewGuid():N}",
                [],
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
        TestGameShortcutService? shortcutService = null)
    {
        // 检查点存储绑定到测试临时目录，避免卸载成功路径清除真实用户目录中的续传标记。
        return new GameUninstallService(
            store,
            new LocalDiagnostics(),
            localizer ?? new LocalizationService(),
            new GameInstallationPath(),
            new DownloadCheckpointStore( TestDataRoot.ForDirectory(Path.Combine(tempDir)) ),
            new GameProcessTracker(),
            shortcutService ?? new TestGameShortcutService());
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            const int maxRetries = 5;
            for (var attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                    break;
                }
                catch (IOException)
                {
                    if (attempt == maxRetries - 1)
                    {
                        throw;
                    }

                    Thread.Sleep(TimeSpan.FromMilliseconds(200 * (attempt + 1)));
                }
                catch (UnauthorizedAccessException)
                {
                    if (attempt == maxRetries - 1)
                    {
                        throw;
                    }

                    Thread.Sleep(TimeSpan.FromMilliseconds(200 * (attempt + 1)));
                }
            }
        }
    }
}
