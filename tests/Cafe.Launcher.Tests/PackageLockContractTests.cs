using System.Diagnostics;
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
///
/// 判据读的是 **git 索引里的那一份**（也就是提交时会进对象库的内容），不是工作区文件：CI 的
/// Release 步骤自己会做一次 RID 还原、合法地改写工作区的锁文件，按工作区判会误报。
/// </summary>
public sealed class PackageLockContractTests
{
    [Fact]
    public void CommittedLockFiles_CarryOnlyTheRuntimeIdentifierFreeGraph()
    {
        var repositoryRoot = TestRepository.Root;
        var lockFiles = LockFiles();

        // 反空转：一个 lock 文件都没找到时必须红，而不是安静通过。
        Assert.True(lockFiles.Length >= 5, $"只找到 {lockFiles.Length} 个 packages.lock.json。");

        var offenders = new List<string>();
        var inspected = 0;
        foreach (var path in lockFiles)
        {
            var relative = Path.GetRelativePath(repositoryRoot, path)
                .Replace(Path.DirectorySeparatorChar, '/');
            var committed = TryReadGitBlob(repositoryRoot, relative, ":")
                ?? TryReadGitBlob(repositoryRoot, relative, "HEAD:");
            if (committed is null)
            {
                // 新工程尚未提交过锁文件，或这次运行不在 git 检出里：这一份不参与判定。
                continue;
            }

            inspected++;
            using var document = JsonDocument.Parse(committed);
            foreach (var target in document.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                // 无 RID 的键就是 "net10.0"；带 RID 的还原写成 "net10.0/win-x64"。
                if (target.Name.Contains('/', StringComparison.Ordinal))
                {
                    offenders.Add($"{relative}: {target.Name}");
                }
            }
        }

        Assert.SkipWhen(
            inspected == 0,
            "git 里的锁文件都读不到（不是 git 检出或 git 不可用），本守卫无法判定。");

        Assert.True(inspected >= 5, $"只校验了 {inspected} 个已提交的锁文件，扫描域可能退化了。");
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

    /// <summary>
    /// 读一个 git 对象：<c>:</c> 前缀是索引（提交时会写进对象库的那一份），<c>HEAD:</c> 是最后一次提交。
    /// git 不可用或该路径不在对象库里时返回 <c>null</c>。
    /// </summary>
    private static string? TryReadGitBlob(string repositoryRoot, string relativePath, string revisionPrefix)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("show");
            startInfo.ArgumentList.Add($"{revisionPrefix}{relativePath}");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var content = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);
            return process.ExitCode == 0 && content.Length > 0 ? content : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
