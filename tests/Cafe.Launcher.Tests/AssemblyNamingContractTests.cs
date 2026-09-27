using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

/// <summary>
/// ADR-043 的结构守卫：工程目录名、<c>.csproj</c> 名、<c>AssemblyName</c>、<c>RootNamespace</c>
/// 与源码命名空间同名，发行资产共用同一个产品 token。名字是隐式契约里最容易漂的一类——
/// 程序集拆分之后就漂过一次（宿主仍叫 <c>…Avalonia</c>、<c>Updater.Core</c> 的命名空间缺
/// <c>.Core</c>、三个命名空间被两个程序集共享），所以这里把关系逐条钉住，而不是靠人记住。
/// </summary>
public sealed class AssemblyNamingContractTests
{
    /// <summary>
    /// 每个工程一个 token。新增工程必须先加进这张表——下面的断言同时比对磁盘上的工程集合，
    /// 因此「忘了登记」和「登记了但没这个工程」都会红。
    /// </summary>
    private static readonly string[] DeclaredProjectTokens =
    [
        "Cafe.Launcher",
        "Cafe.Launcher.Core",
        "Cafe.Launcher.HeadlessTests",
        "Cafe.Launcher.Tests",
        "Cafe.Launcher.UI",
        "Cafe.Launcher.Updater",
        "Cafe.Launcher.Updater.Core"
    ];

    /// <summary>产品 token：exe 名、资产前缀与 macOS bundle 都用它。</summary>
    private const string HostToken = "Cafe.Launcher";

    [Fact]
    public void ProjectDirectories_FollowTheDeclaredTokenSet()
    {
        var onDisk = ProjectFiles()
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            DeclaredProjectTokens.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            onDisk);
    }

    [Fact]
    public void EachProject_CarriesItsTokenInDirectoryProjectAssemblyAndNamespace()
    {
        foreach (var token in DeclaredProjectTokens)
        {
            var project = ProjectFiles().Single(path =>
                string.Equals(Path.GetFileNameWithoutExtension(path), token, StringComparison.Ordinal));

            Assert.Equal(token, Path.GetFileName(Path.GetDirectoryName(project)!));
            Assert.Equal(
                token + ".csproj",
                Path.GetFileName(project),
                StringComparer.OrdinalIgnoreCase);

            var document = XDocument.Load(project);
            Assert.Equal(token, document.Descendants("AssemblyName").Single().Value);
            Assert.Equal(token, document.Descendants("RootNamespace").Single().Value);

            // 命名空间指明属主程序集：工程自己的源文件里不得声明别人的命名空间前缀。
            Assert.Empty(NamespaceOffenders(Path.GetDirectoryName(project)!, token));
        }
    }

    [Fact]
    public void ReleaseAssets_UseTheHostProductToken()
    {
        // 资产前缀、exe 名、macOS bundle 与 Linux wrapper 的 exec 目标必须跟宿主程序集名同源；
        // 写死第二套名字会让用户在下载页/任务管理器里看到与程序集不同的产品。
        // 例外只有 GitHub 仓库名（bluearchive-cafe/Cafe.Launcher.Avalonia…），它是历史标识。
        foreach (var relative in PackagingFiles)
        {
            var text = File.ReadAllText(TestRepository.FromRepositoryRoot(relative));

            Assert.DoesNotMatch(@"(?<!bluearchive-cafe/)Cafe\.Launcher\.Avalonia_", text);
            Assert.DoesNotContain("Cafe.Launcher.Avalonia.exe", text, StringComparison.Ordinal);
            Assert.Contains(HostToken, text, StringComparison.Ordinal);
        }

        var distribution = File.ReadAllText(
            TestRepository.FromRepositoryRoot("scripts/Build-Distribution.ps1"));
        Assert.Contains($"{HostToken}_${{Tag}}_win-x64.zip", distribution, StringComparison.Ordinal);

        var installer = File.ReadAllText(
            TestRepository.FromRepositoryRoot("installer/windows/Cafe.Launcher.iss"));
        Assert.Contains($"#define EXECUTABLE_NAME \"{HostToken}.exe\"", installer, StringComparison.Ordinal);

        var plist = File.ReadAllText(
            TestRepository.FromRepositoryRoot("installer/macos/Info.plist"));
        Assert.Contains($"<string>{HostToken}</string>", plist, StringComparison.Ordinal);

        var wrapper = File.ReadAllText(
            TestRepository.FromRepositoryRoot("installer/linux/templates/cafe-launcher"));
        Assert.Contains($"exec {{APP_DIR}}/{HostToken} ", wrapper, StringComparison.Ordinal);
    }

    private static readonly string[] PackagingFiles =
    [
        "scripts/Build-Distribution.ps1",
        "scripts/New-WindowsInstaller.ps1",
        ".github/workflows/release.yml",
        "installer/windows/Cafe.Launcher.iss",
        "installer/macos/Info.plist",
        "installer/linux/templates/cafe-launcher",
    ];

    private static string[] ProjectFiles() =>
        new[] { "src", "tests" }
            .Select(TestRepository.FromRepositoryRoot)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string[] NamespaceOffenders(string projectDirectory, string token) =>
        Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => File.ReadAllLines(path)
                .Where(line => line.StartsWith("namespace ", StringComparison.Ordinal))
                .Select(line => line["namespace ".Length..].TrimEnd(';', '{', ' '))
                .Where(ns => ns != token && !ns.StartsWith(token + ".", StringComparison.Ordinal))
                .Select(ns => $"{Path.GetFileName(path)}: {ns}"))
            .ToArray();

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
