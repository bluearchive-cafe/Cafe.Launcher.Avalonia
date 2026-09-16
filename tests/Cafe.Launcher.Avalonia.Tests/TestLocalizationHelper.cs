using System.Collections.Generic;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 测试的本地化资源入口。实现已收敛到 <see cref="TestRepository"/>（路径与 <c>.resx</c>
/// 解析结果缓存那里，安装动作每次重做）；本类型保留为既有调用点的转发面。
/// </summary>
/// <remarks>
/// 所有使用 <c>LocalizationService</c> 的测试类必须在静态构造函数里调用
/// <see cref="Initialize"/>：<c>InitializeForTesting</c> 覆盖的是整个进程的测试资源集
/// （最后者胜），资源来自磁盘上的 <c>.resx</c>，因此测试读到的永远是已提交的数据。
/// </remarks>
public static class TestLocalizationHelper
{
    public static void Initialize() => TestRepository.InitializeLocalizationResources();

    public static Dictionary<string, string> ReadResx(string path) => TestRepository.ReadResx(path);

    /// <summary>应用工程目录：<c>src/Cafe.Launcher.Avalonia</c>。</summary>
    public static string FindProjectRoot() => TestRepository.ApplicationPath;

    /// <summary>仓库根：含解决方案文件的目录。</summary>
    public static string FindRepositoryRoot() => TestRepository.Root;
}
