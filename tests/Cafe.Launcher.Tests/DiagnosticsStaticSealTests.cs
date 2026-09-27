using System.IO;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

/// <summary>
/// 共享静态日志缝的单一所有方守卫（R2-c12）：<c>RegisterSharedLogger</c> 的调用
/// 只允许出现在组合根（ServiceConfiguration）里，登记按进程先注册者胜——
/// 后续构造的容器（多容器测试）不得改绑共享缝，各自的实例门面走自己注入的
/// <see cref="UnifiedLogger"/>。
/// </summary>
public sealed class DiagnosticsStaticSealTests
{
    /// <summary>允许出现 <c>RegisterSharedLogger(</c> 的生产文件：定义与唯一所有方。</summary>
    private static readonly string[] AllowedFiles =
    [
        "src/Cafe.Launcher.Core/Composition/LauncherCoreServiceCollectionExtensions.cs",
        "src/Cafe.Launcher.Core/Services/Diagnostics/LocalDiagnostics.cs",
    ];

    [Fact]
    public void RegisterSharedLogger_WhenAlreadyRegistered_KeepsFirstRegistrant()
    {
        var saved = LocalDiagnostics.SharedLoggerForTests;
        using var tempDir = TestDirectory.Create();
        try
        {
            // 先清空共享槽（同进程内其他测试可能已建过容器），再验证「先注册者胜」。
            LocalDiagnostics.SharedLoggerForTests = null;
            using var first = new UnifiedLogger(tempDir.Sub("first"));
            using var second = new UnifiedLogger(tempDir.Sub("second"));

            Assert.True(LocalDiagnostics.RegisterSharedLogger(first));
            Assert.False(LocalDiagnostics.RegisterSharedLogger(second));
        }
        finally
        {
            LocalDiagnostics.SharedLoggerForTests = saved;
        }
    }

    [Fact]
    public void RegisterSharedLogger_IsCalledOnlyByTheCompositionRoot()
    {
        // 扫描域是全部生产源码（host + Core）：共享日志器的定义已随诊断实现迁入 Core，
        // 只扫宿主会让这条守卫随搬迁静默缩小。路径一律相对仓库根。
        var callers = new[] { TestRepository.HostPath, TestRepository.CorePath, TestRepository.PresentationPath }
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => File.ReadAllText(file).Contains("RegisterSharedLogger(", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(TestRepository.Root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedFiles, callers);
    }
}
