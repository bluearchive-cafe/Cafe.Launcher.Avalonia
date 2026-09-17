using System.Text.Json;
using System.Text.RegularExpressions;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 候选总表（`docs/design/candidates-ledger-2026-09.md`）是**视图**，状态的所有方是
/// `.repository-audit/findings.json`。这条契约钉死两者之间最要紧的一条：总表声明的「开放发现」
/// 集合必须与台账的 open 集完全相等——否则「活口清单」会悄悄漂移，而漂移的表现是裁定者照着
/// 一份过期的表做决定，不是任何东西变红。
/// </summary>
/// <remarks>
/// 唯一输入是总表头部那行 <c>&lt;!-- open-findings: … --&gt;</c>：让机器可读的声明只有一处，
/// 免得去解析散文。改那行等于声明台账状态变了——正确顺序是先改台账，再改标记。
/// </remarks>
public sealed class CandidatesLedgerContractTests
{
    private const string LedgerRelativePath = "docs/design/candidates-ledger-2026-09.md";
    private const string FindingsRelativePath = ".repository-audit/findings.json";

    private static readonly Regex OpenFindingsMarker =
        new(@"<!--\s*open-findings:(?<ids>[^>]*?)-->", RegexOptions.Compiled);

    [Fact]
    public void OpenFindingsMarker_MatchesTheOpenFindingsInTheLedger()
    {
        var ledger = File.ReadAllText(TestRepository.FromRepositoryRoot(LedgerRelativePath));
        var match = OpenFindingsMarker.Match(ledger);
        Assert.True(
            match.Success,
            $"{LedgerRelativePath} 必须保留一行 `<!-- open-findings: … -->`：它是本契约的唯一输入。");

        var declared = match.Groups["ids"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(declared);

        var open = ReadOpenFindingIds();
        Assert.NotEmpty(open);
        Assert.Equal(open.Order(StringComparer.Ordinal), declared.Order(StringComparer.Ordinal));

        // 标记不能领先于正文：声明的每个编号都要在表里真的有一行（标记行本身除外）。
        var body = ledger.Remove(match.Index, match.Length);
        foreach (var id in declared)
        {
            Assert.Contains(id, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ledger_NamesTheOwnersItIsOnlyAViewOf()
    {
        // 「本表不定义状态」这件事必须写在文档里，否则下一位维护者会就地改状态、造出第二个真相来源。
        var ledger = File.ReadAllText(TestRepository.FromRepositoryRoot(LedgerRelativePath));

        Assert.Contains("这是视图，不是所有者", ledger, StringComparison.Ordinal);
        Assert.Contains("findings.json", ledger, StringComparison.Ordinal);
        Assert.Contains(".repository-audit/history/", ledger, StringComparison.Ordinal);
    }

    private static HashSet<string> ReadOpenFindingIds()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(TestRepository.FromRepositoryRoot(FindingsRelativePath)));
        var findings = document.RootElement.GetProperty("findings");

        var open = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in findings.EnumerateObject())
        {
            if (finding.Value.GetProperty("status").GetString() == "open")
            {
                open.Add(finding.Name);
            }
        }

        return open;
    }
}
