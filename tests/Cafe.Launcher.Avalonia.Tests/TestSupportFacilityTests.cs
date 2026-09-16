using System.Diagnostics;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 共享测试设施自身的行为：目录隔离与清理、超时/取消、仓库与资源定位。
/// 这些设施是全部用例的地基，它们的退化（例如超时不生效、清理静默失败）会让
/// 别处的红变成另一种红，因此各自有直接的守卫。
/// </summary>
[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class TestSupportFacilityTests
{
    [Fact]
    public void Create_GivesIndependentDirectoriesUnderTheTempPath()
    {
        using var first = TestDirectory.Create();
        using var second = TestDirectory.Create();

        Assert.NotEqual(first.Path, second.Path);
        Assert.True(Directory.Exists(first.Path));
        Assert.True(Directory.Exists(second.Path));
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), first.Path, StringComparison.OrdinalIgnoreCase);

        // 隔离的含义是「一个用例的产物不会出现在另一个用例的目录里」。
        File.WriteAllText(first.Sub("probe.txt"), "first");
        Assert.True(File.Exists(first.Sub("probe.txt")));
        Assert.False(File.Exists(second.Sub("probe.txt")));
    }

    [Fact]
    public void DataRoot_DerivesWellKnownPathsFromTheDirectory()
    {
        using var directory = TestDirectory.Create();

        Assert.Equal(Path.GetFullPath(directory.Path), directory.DataRoot.Root);
        Assert.Equal(
            Path.Combine(directory.Path, Constants.GamePaths.LauncherSettingsFileName),
            directory.DataRoot.SettingsPath);
    }

    [Fact]
    public void Sub_CombinesWithoutCreating()
    {
        using var directory = TestDirectory.Create();

        var nested = directory.Sub(Path.Combine("a", "b"));

        Assert.False(Directory.Exists(nested));
    }

    [Fact]
    public void Dispose_RemovesTheDirectoryAndIsIdempotent()
    {
        var directory = TestDirectory.Create();
        var path = directory.Path;
        File.WriteAllText(directory.Sub("probe.txt"), "content");

        directory.Dispose();
        directory.Dispose();

        Assert.False(Directory.Exists(path));
    }

    /// <summary>
    /// 清理失败必须可见：普通测试的残留目录是维护问题，静默吞掉会让「磁盘上越来越多
    /// 临时目录」永远不出现在测试结果里。用打开的文件句柄占住目录——Windows 上删除
    /// 必然失败；Unix 允许删除已打开的文件，故该分支跳过（跳过在结果里可见）。
    /// </summary>
    [Fact]
    public void Dispose_WhenTheDirectoryCannotBeRemoved_ReportsTheFailure()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Unix 允许删除仍被打开的文件，无法用句柄占住目录来构造删除失败。");

        var directory = TestDirectory.Create();
        var path = directory.Path;
        try
        {
            using (new FileStream(Path.Combine(path, "locked.bin"), FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var exception = Assert.Throws<IOException>(() => directory.Dispose());

                Assert.Contains(path, exception.Message, StringComparison.Ordinal);
            }

            // 失败是「这次清理没成功」，不是「目录坏了」：句柄放掉后同一个目录仍可删除。
            Directory.Delete(path, recursive: true);
            Assert.False(Directory.Exists(path));
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    /// <summary>尽力清理不抛：无头拆卸时句柄延迟释放，让清理问题掩盖测试结果没有意义。</summary>
    [Fact]
    public void Dispose_WhenCleanupIsBestEffort_LeavesTheDirectoryInsteadOfThrowing()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Unix 允许删除仍被打开的文件，无法用句柄占住目录来构造删除失败。");

        var directory = TestDirectory.Create(TestDirectoryCleanup.BestEffort);
        var path = directory.Path;
        try
        {
            using var locked = new FileStream(
                Path.Combine(path, "locked.bin"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            directory.Dispose();

            Assert.True(Directory.Exists(path));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task UntilAsync_WhenTheConditionHoldsImmediately_DoesNotTick()
    {
        var ticks = 0;

        await TestWait.UntilAsync(
            () => true,
            TimeSpan.FromSeconds(5),
            tickAsync: () =>
            {
                ticks++;
                return ValueTask.CompletedTask;
            });

        Assert.Equal(0, ticks);
    }

    [Fact]
    public async Task UntilAsync_PollsUntilTheConditionHolds()
    {
        var remaining = 3;

        await TestWait.UntilAsync(
            () => --remaining <= 0,
            TimeSpan.FromSeconds(5));

        Assert.True(remaining <= 0);
    }

    [Fact]
    public async Task UntilAsync_WhenTimeoutElapses_ThrowsWithTheCallerContext()
    {
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => TestWait.UntilAsync(
                () => false,
                TimeSpan.FromMilliseconds(60),
                "Panel did not become visible."));

        Assert.Contains("Panel did not become visible.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("within", exception.Message, StringComparison.Ordinal);
        // 有界：超时必须真的返回，而不是一直轮询到用例超时。
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"waited {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task UntilAsync_WhenCancelled_ThrowsOperationCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TestWait.UntilAsync(
                () => false,
                TimeSpan.FromSeconds(5),
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task UntilAsync_WhenTheTickDrivesProgress_KeepsTickingUntilTheConditionHolds()
    {
        var ticks = 0;

        await TestWait.UntilAsync(
            () => ticks >= 3,
            TimeSpan.FromSeconds(5),
            tickAsync: () =>
            {
                ticks++;
                return ValueTask.CompletedTask;
            });

        Assert.Equal(3, ticks);
    }

    [Fact]
    public void Repository_LocatesTheApplicationAndItsResources()
    {
        Assert.True(File.Exists(Path.Combine(TestRepository.Root, "Cafe.Launcher.Avalonia.slnx")));
        Assert.True(File.Exists(Path.Combine(
            TestRepository.ApplicationPath,
            "Cafe.Launcher.Avalonia.csproj")));
        Assert.True(Directory.Exists(TestRepository.ResourcesPath));

        // 缓存路径：重复读取得到同一结果，而不是每次上溯目录树。
        Assert.Same(TestRepository.Root, TestRepository.Root);
        Assert.Same(TestRepository.ApplicationPath, TestRepository.ApplicationPath);
    }

    [Fact]
    public void ReadResx_ReadsTheCommittedNeutralResources()
    {
        var resources = TestRepository.ReadResx(
            Path.Combine(TestRepository.ResourcesPath, "LauncherStrings.resx"));

        Assert.NotEmpty(resources);
        Assert.Contains(Constants.LocalizationKeys.Save, resources.Keys);
    }

    /// <summary>
    /// 安装动作必须每次重做：<c>LocalizationService.InitializeForTesting</c> 是「最后者胜」
    /// 的进程级状态，缓存住安装会让一个先装了自定义资源的用例污染其后所有用例。
    /// </summary>
    [Fact]
    public void InitializeLocalizationResources_AfterCustomResources_RestoresTheRepositorySet()
    {
        try
        {
            LocalizationService.InitializeForTesting(new Dictionary<string, Dictionary<string, string>>
            {
                [LauncherLanguages.English] = new(StringComparer.Ordinal)
                {
                    [Constants.LocalizationKeys.Save] = "custom-value"
                }
            });
            var localizer = new LocalizationService();
            localizer.SetLanguage(LauncherLanguages.English);
            Assert.Equal("custom-value", localizer.T(Constants.LocalizationKeys.Save));

            TestRepository.InitializeLocalizationResources();

            var restored = new LocalizationService();
            restored.SetLanguage(LauncherLanguages.English);
            Assert.NotEqual("custom-value", restored.T(Constants.LocalizationKeys.Save));
            Assert.False(string.IsNullOrWhiteSpace(restored.T(Constants.LocalizationKeys.Save)));
        }
        finally
        {
            TestRepository.InitializeLocalizationResources();
        }
    }
}
