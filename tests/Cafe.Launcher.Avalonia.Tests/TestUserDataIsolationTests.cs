namespace Cafe.Launcher.Avalonia.Tests;

public sealed class TestUserDataIsolationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Resolve_WhenTestOverrideIsMissing_UsesProductLocalApplicationData(
        string? testOverride)
    {
        var localApplicationData = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));

        var result = Services.LauncherDataRoot.ResolveProcessRoot(
            testOverride,
            localApplicationData);

        Assert.Equal(
            Path.Combine(localApplicationData, Constants.LauncherConstants.ProductName),
            result);
    }

    [Fact]
    public void Resolve_WhenTestOverrideIsConfigured_UsesFullOverridePath()
    {
        var relativeOverride = Path.Combine(
            ".",
            Guid.NewGuid().ToString("N"),
            "..",
            "isolated-user-data");

        var result = Services.LauncherDataRoot.ResolveProcessRoot(
            relativeOverride,
            "unused");

        Assert.Equal(Path.GetFullPath(relativeOverride), result);
    }

    [Fact]
    public void TestProcess_DefaultSettingsPathUsesIsolatedUserDataDirectory()
    {
        var isolatedDirectory = Environment.GetEnvironmentVariable(
            Services.LauncherDataRoot.TestOverrideEnvironmentVariable);

        Assert.False(string.IsNullOrWhiteSpace(isolatedDirectory));
        Assert.StartsWith(
            Path.GetFullPath(Path.GetTempPath()),
            Path.GetFullPath(isolatedDirectory),
            StringComparison.OrdinalIgnoreCase);

        using var settingsService = new Services.LauncherSettingsService(
            Services.LauncherDataRoot.ForCurrentProcess());
        Assert.Equal(
            Path.Combine(
                Path.GetFullPath(isolatedDirectory),
                Constants.GamePaths.LauncherSettingsFileName),
            settingsService.SettingsPath);
    }

    [Fact]
    public void IsolatedUserDataDirectory_KeepsDerivedUnixSocketPathUnderKernelLimit()
    {
        // 套接字路径仅在 Unix 上被消费（Windows 的 Listen/Raise 走命名事件分支，
        // Windows runner 的用户临时目录可合法地超出该上限——b4a80c8 的守卫初版
        // 未门控，在 runneradmin 上自证其误）。跳过在 Windows 上可见。
        Assert.SkipUnless(
            !OperatingSystem.IsWindows(),
            "AF_UNIX 套接字路径上限仅在 Unix 平台约束 LauncherDataRoot.ForCurrentProcess().Root 的派生路径。");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // AUD-CI-002：Root 派生 Unix 域套接字路径（<Root>/cl-signal-<12hex>.sock），
        // AF_UNIX 的 sockaddr_un 上限是 108 字节（含终止符即 107）。隔离目录过深
        // 会让 Listen/Raise 的绑定永远失败——linux-unit-tests 首跑即因此红过。
        var isolatedDirectory = Environment.GetEnvironmentVariable(
            Services.LauncherDataRoot.TestOverrideEnvironmentVariable);

        Assert.False(string.IsNullOrWhiteSpace(isolatedDirectory));
        var socketPath = Services.CrossProcessLaunchSignal.GetSocketFilePath(
            isolatedDirectory,
            "Local\\CafeTest_Signal_" + Guid.NewGuid().ToString("N"));

        Assert.True(
            socketPath.Length <= 107,
            $"Derived Unix socket path is too long ({socketPath.Length} > 107): {socketPath}");
    }

    /// <summary>
    /// 进程根解析只允许出现在声明表内：组合根解析一次，ADR-019 保护的 pre-DI 路径各解析
    /// 一次，其余模块一律接收注入的 <c>LauncherDataRoot</c>。少了这条守卫，进程级静态会
    /// 重新在各模块里开花——那正是候选 03 要收掉的东西。
    /// </summary>
    [Fact]
    public void ProcessRootResolution_IsConfinedToDeclaredPreDiSites()
    {
        var projectRoot = TestLocalizationHelper.FindProjectRoot();
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 定义与唯一实现
            Path.Combine(projectRoot, "Services", "LauncherDataRoot.cs"),
            // 组合根：解析一次后注入所有登记项
            Path.Combine(projectRoot, "Composition", "ServiceConfiguration.cs"),
            // pre-DI：首启探测、崩溃日志器、单实例信号
            Path.Combine(projectRoot, "Program.cs"),
            // 崩溃报告进程没有容器，也没有别的解析点
            Path.Combine(projectRoot, "CrashReportApp.axaml.cs"),
            // 兼容前缀在 Windows 上复用启动器数据根；Unix 分支自取 XDG 目录，不读数据根
            Path.Combine(projectRoot, "Services", "GameRuntime", "GameCompatibilityPaths.cs")
        };

        var offenders = Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOrTestArtifact(projectRoot, path))
            .Where(path => !declared.Contains(path))
            .Where(path => File.ReadAllText(path).Contains("ForCurrentProcess", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(projectRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "这些文件解析了进程数据根，但不在声明表内。请改为接收注入的 LauncherDataRoot；"
            + "若确属 pre-DI 路径，在声明表里留名并注明理由："
            + string.Join(", ", offenders));
    }

    [Fact]
    public void PersistentUserDataPaths_UseCentralDirectoryProvider()
    {
        var projectRoot = TestLocalizationHelper.FindProjectRoot();
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(projectRoot, "Services", "LauncherDataRoot.cs"),
            Path.Combine(projectRoot, "Features", "GameOperations", "GameUninstallService.cs")
        };
        var offenders = Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relativePath = Path.GetRelativePath(projectRoot, path);
                return !relativePath.StartsWith($"tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !relativePath.StartsWith($".claude{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !relativePath.StartsWith($".worktrees{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !relativePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !relativePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
            })
            .Where(path => !allowedFiles.Contains(path))
            .Where(path => File
                .ReadAllText(path)
                .Contains(
                    "Environment.SpecialFolder.LocalApplicationData",
                    StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(projectRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static bool IsBuildOrTestArtifact(string projectRoot, string path)
    {
        var relativePath = Path.GetRelativePath(projectRoot, path);
        return relativePath.StartsWith($"tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith($".claude{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith($".worktrees{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

}
