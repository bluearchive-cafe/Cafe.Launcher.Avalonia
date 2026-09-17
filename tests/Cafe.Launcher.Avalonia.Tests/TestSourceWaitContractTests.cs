using System.Text.RegularExpressions;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 测试侧等待的超时纪律（AUD-TEST-014）：被测 VM 暴露的 <c>Pending*</c> 任务必须带
/// <c>WaitAsync</c> 上限。此前 csproj 抑制 xUnit1051 的理由声称「门控等待统一用
/// WaitAsync/预算轮询加超时上限」，实测有 14 处裸 <c>await</c>——一条回归从此既挂住 CI
/// 又不被任何断言看见。本卷只扫这一族：替身与事件处理体里的故意阻塞点（用例自己
/// 决定何时释放）不在范围内，那里长阻塞就是场景本身。
/// </summary>
/// <remarks>
/// 覆盖的是「名字可枚举」的那一族。一次性任务局部变量（<c>commitTask</c> 之类）与未来
/// 新增站点由 <c>test.ps1</c> 的 <c>--blame-hang</c> 兜底——两层各管一段，都在
/// <c>Tests.csproj</c> 的抑制理由里写明。
/// </remarks>
public sealed partial class TestSourceWaitContractTests
{
    /// <summary>被测 VM 对外暴露的门控任务名（内部属性即测试缝）。</summary>
    private static readonly string[] PendingTaskMembers =
    [
        "PendingAppearancePreview",
        "PendingCountdownTask",
        "PendingFilterTask",
        "PendingGameRuntimeStatusRefresh",
        "PendingRangeProbeTask",
        "PendingStartupUpdateCheck",
        "PendingThemeRefresh"
    ];

    [Fact]
    public void PendingTaskMembers_AreAlwaysAwaitedWithATimeout()
    {
        var (files, violations, boundedSites) = ScanTestSources();

        // 反空转基线：扫描面与「确有站点被这条规则管到」都要有实测值，否则
        // 「一个文件都没扫到」或「族名改了」与「树是干净的」不可区分。
        Assert.True(files.Count >= 200, $"Scan surface shrank unexpectedly: {files.Count} files.");
        // 基线为 2026-09-17 实测值（14 处原台账点名 + 扫描另发现的 3 处）；删站点时同步下调。
        Assert.True(
            boundedSites >= 17,
            $"Only {boundedSites} bounded Pending* awaits found — the member list or the scan is stale.");
        Assert.Equal([], violations);
    }

    private static (List<string> Files, List<string> Violations, int BoundedSites) ScanTestSources()
    {
        var root = TestRepository.FromRepositoryRoot("tests");
        var files = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        var violations = new List<string>();
        var boundedSites = 0;
        foreach (var path in files)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (!line.Contains("await", StringComparison.Ordinal))
                {
                    continue;
                }

                var member = PendingTaskMembers.FirstOrDefault(
                    name => Regex.IsMatch(line, $@"\b{name}\b"));
                if (member is null)
                {
                    continue;
                }

                // 允许成员与 .WaitAsync 之间夹一个抑制符（`PendingX!` 的既有写法）。
                if (Regex.IsMatch(line, $@"{member}\s*!?\s*\.WaitAsync\("))
                {
                    boundedSites++;
                    continue;
                }

                violations.Add($"{relative}:{index + 1} awaits {member} without .WaitAsync: {line.Trim()}");
            }
        }

        return (files, violations, boundedSites);
    }
}
