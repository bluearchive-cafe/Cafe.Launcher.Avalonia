using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Testing;

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
    private static readonly Lazy<string> ApplicationRoot = new(FindApplicationRoot);
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Localization =
        new(ReadAllResx);

    private static readonly Lazy<string> ApplicationResourcesRoot =
        new(() => Path.Combine(ApplicationRoot.Value, "Resources"));

    /// <summary>仓库根：含解决方案文件的目录。面向读 workflow、release 脚本等仓库级文件的用例。</summary>
    public static string Root => RepositoryRoot.Value;

    /// <summary>应用工程目录：<c>src/Cafe.Launcher.Avalonia</c>。面向读源码与资源文件的用例。</summary>
    public static string ApplicationPath => ApplicationRoot.Value;

    /// <summary>本地化资源目录。</summary>
    public static string ResourcesPath => ApplicationResourcesRoot.Value;

    /// <summary>仓库根下的路径。</summary>
    public static string InRepository(params string[] segments) => Combine(Root, segments);

    /// <summary>应用工程目录下的路径。</summary>
    public static string InApplication(params string[] segments) => Combine(ApplicationPath, segments);

    /// <summary>
    /// 仓库根下的路径，接受以 <c>'/'</c> 分隔的相对路径——契约测试的字面量习惯沿用仓库的
    /// 相对引用（Bash 与 CI 里也是这个形状），由这里一次性换算成宿主分隔符。
    /// </summary>
    public static string FromRepositoryRoot(string relativePath) => CombineRelative(Root, relativePath);

    /// <summary>应用工程目录下的路径，接受以 <c>'/'</c> 分隔的相对路径。</summary>
    public static string FromApplicationRoot(string relativePath) => CombineRelative(ApplicationPath, relativePath);

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
    /// 从测试程序集的输出目录向上寻找应用工程：容器与 IDE 的运行目录深度不同，
    /// 相对层级不可依赖，只能按标志文件上溯。
    /// </summary>
    private static string FindApplicationRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var applicationProject = Path.Combine(
                directory.FullName,
                "src",
                "Cafe.Launcher.Avalonia",
                "Cafe.Launcher.Avalonia.csproj");
            if (File.Exists(applicationProject))
            {
                return Path.GetDirectoryName(applicationProject)!;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "src/Cafe.Launcher.Avalonia/Cafe.Launcher.Avalonia.csproj was not found.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cafe.Launcher.Avalonia.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cafe.Launcher.Avalonia.slnx was not found.");
    }
}
