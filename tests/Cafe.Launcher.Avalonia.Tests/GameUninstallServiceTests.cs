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

    /// <summary>
    /// 实测的 Blue Archive 启动配置（ADR-032）：宿主名是配置里的 <c>name</c>，游戏可执行文件
    /// 在 <c>params</c> 里；反作弊宿主是宿主名去掉 <c>_loader_x64</c> 的同族短名，它不在配置里。
    /// 需要「判据来自配置」的用例用这一组，而不是与本判据无关的合成名。
    /// </summary>
    private const string LoaderExecutableName = "xldr_BlueArchiveOnline_JP_loader_x64";

    private const string GameExecutableName = "BlueArchive";

    private const string AntiCheatSiblingName = "xldr_BlueArchiveOnline_JP";

    private static readonly string[] LaunchParameters = ["BlueArchive.exe"];

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
            UninstallScope.ThoroughCleanup,
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

        var result = await service.UninstallAsync(Snapshot(localGame), UninstallScope.ThoroughCleanup, _ => { });

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
                asked.Add(names);
                // 与真实判据同形：只有请求的族里含配置宿主时，反作弊宿主才落在族内
                // （ExtendsProcessName 那条规则的前提），否则它根本不该被判据认领。
                return Task.FromResult<IReadOnlyList<string>>(
                    names.Contains(LoaderExecutableName, StringComparer.OrdinalIgnoreCase)
                        ? [AntiCheatSiblingName]
                        : []);
            }));

        var result = await service.UninstallAsync(Snapshot(localGame), UninstallScope.ThoroughCleanup, _ => { });

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
        var result = await service.UninstallAsync(Snapshot(localGame), UninstallScope.ThoroughCleanup, _ => { });

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
        var store = new LocalInstallationStateStore();
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
        IGameProcessTracker? processTracker = null)
    {
        // 检查点存储绑定到测试临时目录，避免卸载成功路径清除真实用户目录中的续传标记。
        return new GameUninstallService(
            store,
            new LocalDiagnostics(),
            localizer ?? new LocalizationService(),
            new GameInstallationPath(),
            new DownloadCheckpointStore( TestDataRoot.ForDirectory(Path.Combine(tempDir)) ),
            processTracker ?? new GameProcessTracker(),
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
