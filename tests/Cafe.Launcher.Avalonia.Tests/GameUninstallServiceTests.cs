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
        var service = CreateService(store);
        var snapshot = new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.Ready,
            LocalGame = localGame
        };
        var progress = new List<GameOperationProgress>();
        // FileShare.None 独占打开：File.Delete 将因共享冲突抛出 IOException。
        await using var lockStream = File.Open(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await service.UninstallAsync(snapshot, progress.Add);
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

        var result = await service.UninstallAsync(snapshot, _ => progressInvoked = true);

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

        var first = await service.UninstallAsync(snapshot, _ => { });
        var second = await service.UninstallAsync(snapshot, _ => { });
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
        LocalizationService? localizer = null)
    {
        // 检查点存储绑定到测试临时目录，避免卸载成功路径清除真实用户目录中的续传标记。
        return new GameUninstallService(
            store,
            new LocalDiagnostics(),
            localizer ?? new LocalizationService(),
            new GameInstallationPath(),
            new DownloadCheckpointStore(Path.Combine(tempDir, "download_state.json")),
            new GameProcessTracker());
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
