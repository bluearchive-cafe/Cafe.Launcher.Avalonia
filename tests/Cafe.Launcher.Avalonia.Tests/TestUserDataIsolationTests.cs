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

    /// <summary>
    /// 受管兼容子树也必须落在隔离目录内。它不是数据根的普通消费方：<c>GameCompatibilityPaths</c>
    /// 是 ADR-025 的已声明例外（静态助手、无 DI 接缝），Unix 分支从 XDG 数据主目录派生，看不见
    /// 数据根覆盖。少了这条守卫，<c>GameUninstallServiceTests</c> 的彻底清除用例会在开发机的
    /// 真实 <c>~/.local/share/cafe-launcher</c> 上写 marker 再把整棵删掉——CI 容器是新的，
    /// 所以红不了，只有开发机受伤。
    /// </summary>
    [Fact]
    public void TestProcess_ManagedCompatibilityRootStaysInsideTheIsolatedDirectory()
    {
        var isolatedDirectory = Environment.GetEnvironmentVariable(
            Services.LauncherDataRoot.TestOverrideEnvironmentVariable);

        Assert.False(string.IsNullOrWhiteSpace(isolatedDirectory));
        Assert.StartsWith(
            Path.GetFullPath(isolatedDirectory),
            Path.GetFullPath(Services.GameRuntime.GameCompatibilityPaths.GetDefaultCompatibilityRoot()),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Unix 侧的隔离手段本身也要被钉住（上一条只断言结果落在隔离目录内，把 XDG 变量改成
    /// 别的目录它照样绿）。跳在 Windows 上可见：该分支不读 XDG。
    /// </summary>
    [Fact]
    public void TestProcess_UnixDataHomeIsRedirectedToTheIsolatedDirectory()
    {
        Assert.SkipWhen(
            OperatingSystem.IsWindows(),
            "Windows 分支的兼容前缀复用启动器数据根，不读 XDG_DATA_HOME。");

        var isolatedDirectory = Environment.GetEnvironmentVariable(
            Services.LauncherDataRoot.TestOverrideEnvironmentVariable);

        Assert.Equal(
            Path.GetFullPath(isolatedDirectory!),
            Path.GetFullPath(Environment.GetEnvironmentVariable(
                Testing.TestUserDataIsolation.UnixDataHomeVariable)!));
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
    /// 一次，其余模块一律接收注入的 <c>LauncherDataRoot</c>。表里另有一处已声明例外
    /// （<c>GameCompatibilityPaths</c>：静态助手、无 DI 接缝，Windows 分支在调用时解析）。
    /// 少了这条守卫，进程级静态会重新在各模块里开花——那正是候选 03 要收掉的东西。
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

        var scanned = Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOrTestArtifact(projectRoot, path))
            .ToArray();

        // 反空转基线：本守卫的形状是「排除声明表后必须为空」，于是「根目录找错／后缀失效
        // 导致一个文件都没扫」与「树是干净的」不可区分。两条基线把这种退化态变成红的。
        // 基线为 2026-09-15 的实测值；真的删文件就同步下调，扫描失效应表现为红而不是绿。
        const int landedScannedFiles = 231;
        const int landedResolvingFiles = 5;
        var resolving = scanned
            .Where(path => File.ReadAllText(path).Contains("ForCurrentProcess", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            scanned.Length >= landedScannedFiles,
            $"只枚举到 {scanned.Length} 个 .cs 文件，低于落地基线 {landedScannedFiles}——"
            + "先确认扫描域仍是 src/ 全树（递归未退化成 TopDirectoryOnly）。");
        Assert.True(
            resolving.Length >= landedResolvingFiles,
            $"只有 {resolving.Length} 个文件含 ForCurrentProcess，低于落地基线 {landedResolvingFiles}——"
            + "要么解析点被删（同步下调基线），要么模式过时（重命名后本守卫会静默放行一切）。");

        var offenders = scanned
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
