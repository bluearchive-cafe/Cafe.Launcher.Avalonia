using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class CodebaseAuditContractTests
{
    private static readonly string[] SupersededAuditPaths =
    [
        "docs/design/candidates-ledger-2026-09.md",
        "docs/design/decision-brief-2026-09-17.md",
        "docs/design/simplification-reuse-plan-2026-09-16.md",
        "docs/architecture-review-2026-09-13.html",
        "docs/architecture-review-2026-09-14.html",
    ];

    [Fact]
    public void AuditState_UsesOneCurrentMarkdownFile()
    {
        var auditPath = TestRepository.FromRepositoryRoot("CODEBASE_AUDIT.md");
        var audit = File.ReadAllText(auditPath);

        Assert.Contains("唯一事实源", audit, StringComparison.Ordinal);
        Assert.Contains("## 开放发现", audit, StringComparison.Ordinal);
        Assert.Contains("## 已接受风险", audit, StringComparison.Ordinal);

        var legacyAuditDirectory = TestRepository.FromRepositoryRoot(".repository-audit");
        Assert.True(
            !Directory.Exists(legacyAuditDirectory)
            || !Directory.EnumerateFiles(legacyAuditDirectory, "*", SearchOption.AllDirectories).Any(),
            "审计状态只维护在 CODEBASE_AUDIT.md；.repository-audit 下不得恢复状态文件或历史报告。");

        foreach (var relativePath in SupersededAuditPaths)
        {
            Assert.False(
                Path.Exists(TestRepository.FromRepositoryRoot(relativePath)),
                $"审计状态只维护在 CODEBASE_AUDIT.md；请勿恢复 {relativePath}。");
        }
    }
}
