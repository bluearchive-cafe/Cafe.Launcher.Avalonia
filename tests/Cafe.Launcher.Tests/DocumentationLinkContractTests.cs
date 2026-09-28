using System.Text.RegularExpressions;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

/// <summary>
/// 文档契约：Markdown 里的相对链接必须指向真实存在的文件或目录。
///
/// 程序集与工程改名那批之后，`docs/research` 与几份 ADR 的相对链接一次性断掉 39 条，而没有任何
/// 守卫会红——链接腐烂是「文档里唯一会静默失效的东西」。历史 ADR 允许保留当时的决策叙述，
/// 但**引用**必须一直可点。
/// </summary>
public sealed class DocumentationLinkContractTests
{
    private static readonly Regex LinkPattern = new(@"\]\((?<target>[^)\s]+)\)", RegexOptions.Compiled);

    /// <summary>反空转下限：仓库当前约 200 条相对链接。</summary>
    private const int MinimumInspectedLinks = 150;

    [Fact]
    public void MarkdownLinks_ResolveToExistingTargets()
    {
        var repositoryRoot = TestRepository.Root;
        var broken = new List<string>();
        var inspected = 0;

        foreach (var document in MarkdownFiles(repositoryRoot))
        {
            var directory = Path.GetDirectoryName(document)!;
            foreach (Match match in LinkPattern.Matches(File.ReadAllText(document)))
            {
                var target = match.Groups["target"].Value;
                if (IsExternalOrAnchor(target))
                {
                    continue;
                }

                var path = target.Split('#')[0];
                if (path.Length == 0)
                {
                    continue;
                }

                inspected++;
                var resolved = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    broken.Add($"{Relative(repositoryRoot, document)} -> {target}");
                }
            }
        }

        Assert.True(
            inspected >= MinimumInspectedLinks,
            $"只检查了 {inspected} 条相对链接（下限 {MinimumInspectedLinks}），扫描域可能退化了。");
        Assert.True(
            broken.Count == 0,
            "以下 Markdown 相对链接指向不存在的目标（改名/搬迁后记得同步文档引用）：\n"
            + string.Join("\n", broken));
    }

    private static bool IsExternalOrAnchor(string target) =>
        target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith('#');

    private static IEnumerable<string> MarkdownFiles(string repositoryRoot) =>
        Directory
            .EnumerateFiles(repositoryRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal);

    private static string Relative(string repositoryRoot, string path) =>
        Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');
}
