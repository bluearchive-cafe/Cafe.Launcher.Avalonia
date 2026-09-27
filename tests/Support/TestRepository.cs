using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Testing;

/// <summary>
/// 测试对仓库与本地化资源的唯一定位点：路径与 <c>.resx</c> 的解析结果各缓存一次，
/// 安装到 <see cref="LocalizationService"/> 则每次调用都重新执行。
/// </summary>
/// <remarks>
/// 缓存的是「文件里写了什么」，不是「当前生效的资源集」：<see cref="InitializeLocalizationResources"/>
/// 的语义是覆盖当前值，因此每次调用都必须重装——缓存住安装动作会让一个先装了自定义资源的
/// 用例污染其后所有用例（<c>LocalizationService.InitializeForTesting</c> 是最后者胜的进程级状态）。
/// </remarks>
public static class TestRepository
{
    private static readonly string[] Locales =
    [
        LauncherLanguages.English,
        LauncherLanguages.SimplifiedChinese,
        LauncherLanguages.TraditionalChinese,
        LauncherLanguages.Japanese
    ];

    private static readonly string[] ResxFiles =
    [
        "LauncherStrings.resx",
        "LauncherStrings.zh-Hans.resx",
        "LauncherStrings.zh-Hant.resx",
        "LauncherStrings.ja.resx"
    ];

    private static readonly Lazy<string> RepositoryRoot = new(FindRepositoryRoot);
    private static readonly Lazy<string> HostRoot = new(() => FindProjectRoot("Cafe.Launcher"));
    private static readonly Lazy<string> CoreRoot = new(() => FindProjectRoot("Cafe.Launcher.Core"));
    private static readonly Lazy<string> PresentationRoot = new(() => FindProjectRoot("Cafe.Launcher.UI"));
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Localization =
        new(ReadAllResx);

    private static readonly Lazy<string> ApplicationResourcesRoot =
        new(() => Path.Combine(PresentationRoot.Value, "Resources"));

    /// <summary>仓库根：含解决方案文件的目录。面向读 workflow、release 脚本等仓库级文件的用例。</summary>
    public static string Root => RepositoryRoot.Value;

    /// <summary>WinExe 宿主工程目录：<c>src/Cafe.Launcher</c>。</summary>
    public static string HostPath => HostRoot.Value;

    /// <summary>无 Avalonia 后端工程目录。</summary>
    public static string CorePath => CoreRoot.Value;

    /// <summary>Avalonia 表现工程目录。</summary>
    public static string PresentationPath => PresentationRoot.Value;

    /// <summary>本地化资源目录（.resx 随表现层归 UI 工程）。</summary>
    public static string ResourcesPath => ApplicationResourcesRoot.Value;

    /// <summary>仓库根下的路径。</summary>
    public static string InRepository(params string[] segments) => Combine(Root, segments);

    public static string InHost(params string[] segments) => Combine(HostPath, segments);

    public static string InCore(params string[] segments) => Combine(CorePath, segments);

    public static string InPresentation(params string[] segments) => Combine(PresentationPath, segments);

    /// <summary>
    /// 仓库根下的路径，接受以 <c>'/'</c> 分隔的相对路径——契约测试的字面量习惯沿用仓库的
    /// 相对引用（Bash 与 CI 里也是这个形状），由这里一次性换算成宿主分隔符。
    /// </summary>
    public static string FromRepositoryRoot(string relativePath) => CombineRelative(Root, relativePath);

    public static string FromHostRoot(string relativePath) => CombineRelative(HostPath, relativePath);

    public static string FromCoreRoot(string relativePath) => CombineRelative(CorePath, relativePath);

    public static string FromPresentationRoot(string relativePath) => CombineRelative(PresentationPath, relativePath);

    /// <summary>
    /// 把仓库里的四种语言资源装进 <see cref="LocalizationService"/> 的测试资源槽位。
    /// 每次调用都重装，即便此前已有用例装过自定义资源。
    /// </summary>
    public static void InitializeLocalizationResources() =>
        LocalizationService.InitializeForTesting(Localization.Value);

    /// <summary>读取一个 <c>.resx</c> 文件为键值对，不经过缓存。</summary>
    public static Dictionary<string, string> ReadResx(string path)
    {
        var doc = XDocument.Load(path);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in doc.Root!.Elements("data"))
        {
            var name = data.Attribute("name")?.Value
                ?? throw new InvalidDataException($"data element without name in {path}");
            var value = data.Element("value")?.Value ?? string.Empty;
            dict[name] = value;
        }

        return dict;
    }

    private static string Combine(string root, string[] segments)
    {
        var path = root;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    private static string CombineRelative(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static Dictionary<string, Dictionary<string, string>> ReadAllResx()
    {
        var resourcesRoot = ResourcesPath;
        if (!Directory.Exists(resourcesRoot))
        {
            throw new DirectoryNotFoundException(
                $"Required localization resource directory is missing: {resourcesRoot}");
        }

        var resources = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        for (var i = 0; i < Locales.Length; i++)
        {
            var filePath = Path.Combine(resourcesRoot, ResxFiles[i]);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    $"Required localization resource is missing: {filePath}",
                    filePath);
            }

            resources[Locales[i]] = ReadResx(filePath);
        }

        return resources;
    }

    /// <summary>
    /// 从测试程序集的输出目录向上寻找工程：容器与 IDE 的运行目录深度不同，
    /// 相对层级不可依赖，只能按标志文件上溯。
    /// </summary>
    private static string FindProjectRoot(string projectName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var project = Path.Combine(directory.FullName, "src", projectName, projectName + ".csproj");
            if (File.Exists(project))
            {
                return Path.GetDirectoryName(project)!;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"src/{projectName}/{projectName}.csproj was not found.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cafe.Launcher.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cafe.Launcher.slnx was not found.");
    }
}
