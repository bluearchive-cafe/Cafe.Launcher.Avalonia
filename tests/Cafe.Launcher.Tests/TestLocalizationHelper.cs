using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

/// <summary>
/// 本地化测试对已提交 <c>.resx</c> 与 UI 工程路径的读取入口；生产与测试的运行时查找
/// 都走 <c>LocalizationService</c> 内嵌资源，不再有进程级的资源安装步骤。
/// </summary>
public static class TestLocalizationHelper
{
    public static Dictionary<string, string> ReadResx(string path) => TestRepository.ReadResx(path);

    /// <summary>
    /// 表现层工程目录：<c>src/Cafe.Launcher.UI</c>。本地化资源与 XAML 归表现层，
    /// 因此这条入口指 UI 工程；需要宿主文件的用例请直接用 TestRepository.FromHostRoot。
    /// </summary>
    public static string FindProjectRoot() => TestRepository.PresentationPath;

    /// <summary>仓库根：含解决方案文件的目录。</summary>
    public static string FindRepositoryRoot() => TestRepository.Root;
}
