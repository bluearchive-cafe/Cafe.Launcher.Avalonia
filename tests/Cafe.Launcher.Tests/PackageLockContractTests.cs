using System.Text.Json;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

/// <summary>
/// 依赖契约：提交的 <c>packages.lock.json</c> 必须是**无 RID** 形态。
///
/// 仓库只提交无 RID 的依赖图（原因写在 <c>Directory.Build.props</c>：一个 lock 只能固定一个
/// 上下文），而 CI 用 <c>RestoreLockedMode=true</c> 还原。本地带 <c>-r</c> 的还原
/// （<c>verify.ps1</c>、<c>Build-Distribution.ps1</c>）会把 RID 段写回 lock；这样提交上去，CI 会以
/// NU1004 失败（"Project's runtime identifiers: , lock file's runtime identifiers win-x64"）。
/// 这条守卫把「提交前先给锁文件 <c>git restore</c>」从口头约定变成红/绿——路径改名成未跟踪文件时
/// <c>git restore</c> 会静默不生效，正是本守卫要挡住的那次事故。
/// </summary>
public sealed class PackageLockContractTests
{
    [Fact]
    public void CommittedLockFiles_CarryOnlyTheRuntimeIdentifierFreeGraph()
    {
        var lockFiles = LockFiles();

        // 反空转：一个 lock 文件都没找到时必须红，而不是安静通过。
        Assert.True(lockFiles.Length >= 5, $"只找到 {lockFiles.Length} 个 packages.lock.json。");

        var offenders = new List<string>();
        foreach (var path in lockFiles)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var target in document.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                // 无 RID 的键就是 "net10.0"；带 RID 的还原写成 "net10.0/win-x64"。
                if (target.Name.Contains('/', StringComparison.Ordinal))
                {
                    offenders.Add($"{Relative(path)}: {target.Name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "提交的 packages.lock.json 含 RID 专属依赖段——本地带 -r 的还原改写后没有还原回去，"
            + "CI 的 locked mode 会以 NU1004 失败。提交前对这些文件执行 git restore：\n"
            + string.Join("\n", offenders));
    }

    private static string[] LockFiles() =>
        new[] { "src", "tests" }
            .Select(TestRepository.FromRepositoryRoot)
            .SelectMany(root => Directory.EnumerateFiles(root, "packages.lock.json", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string Relative(string path) =>
        Path.GetRelativePath(TestRepository.Root, path).Replace(Path.DirectorySeparatorChar, '/');
}
