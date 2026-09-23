using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 候选总表（`docs/design/candidates-ledger-2026-09.md`）是**视图**，状态的所有方是
/// `.repository-audit/findings.json`。这条契约钉死两者之间的四件事：机器可读的「开放发现」标记
/// 集合必须等于台账的 open 集；§3.4 的条数散文（台账总条数、已解决／开放／已书面接受风险）必须
/// 等于台账的实际计数；§3.4 的已解决类别表（每类的条数与编号）必须逐项等于台账里 resolved 的
/// 集合；以及文档必须写明自己只是视图。少了这些，「活口清单」与检索索引会悄悄漂移，而漂移的
/// 表现是裁定者照着一份过期的表做决定，不是任何东西变红——2026-09 复核时就同时存在四处这种
/// 漂移（总条数写 50、已解决写 45、开放写 3、类别表漏 `AUD-ARCH-005`／`AUD-PERF-005` 且多出
/// 一个台账里不存在的 `AUD-MAINT-008`）。
/// </summary>
/// <remarks>
/// 标记是唯一机器可读的声明；散文只被读四处（三个条数锚点与 §3.4 的类别表），每处都要求
/// 「恰好命中一次」，并全部与 `findings.json` 比对，因此本契约不会随表格重排而失效。改标记等于
/// 声明台账状态变了——正确顺序是先改台账，再改标记，最后改散文。
/// </remarks>
public sealed class CandidatesLedgerContractTests
{
    private const string LedgerRelativePath = "docs/design/candidates-ledger-2026-09.md";
    private const string FindingsRelativePath = ".repository-audit/findings.json";

    private static readonly Regex OpenFindingsMarker =
        new(@"<!--\s*open-findings:(?<ids>[^>]*?)-->", RegexOptions.Compiled);

    // 散文锚点：每条都要求恰好命中一次，避免表格重排或同一说法出现两次时契约悄悄失效。
    private static readonly Regex LedgerTotalCount = new(@"现行台账 (?<count>\d+) 条", RegexOptions.Compiled);
    private static readonly Regex FindingsOwnerCount = new(@"findings\.json`，(?<count>\d+) 条", RegexOptions.Compiled);
    private static readonly Regex ResolvedCount = new(@"\*\*已解决 (?<count>\d+) 条\*\*", RegexOptions.Compiled);
    private static readonly Regex OpenCount = new(@"\*\*开放 (?<count>\d+) 条\*\*", RegexOptions.Compiled);
    private static readonly Regex AcceptedRiskCount = new(@"\*\*已书面接受风险 (?<count>\d+) 条\*\*", RegexOptions.Compiled);
    private static readonly Regex AcceptedRiskParagraph = new(@"^.*已书面接受风险.*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex FindingIdReference = new(@"AUD-[A-Z]+-\d{3}", RegexOptions.Compiled);

    // §3.4 的类别行：| `AUD-ARCH` | 12 | `001` `002` … |
    private static readonly Regex ResolvedTableRow = new(
        @"^\|\s*`AUD-(?<prefix>[A-Z]+)`\s*\|\s*(?<count>\d+)\s*\|\s*(?<ids>[^|]*?)\s*\|\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ListedFindingNumber = new(@"`(?<number>\d{3})`", RegexOptions.Compiled);

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

    private static HashSet<string> ReadOpenFindingIds() =>
        ReadFindingStatuses()
            .Where(entry => entry.Value == "open")
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void SectionCounts_MatchTheLedgerTheySummarise()
    {
        // 2026-09 复核发现四处散文漂移：总条数写 50（实际 55）、已解决写 45（实际 47）、
        // 开放写 3（实际 2）。数字不会被任何别的东西读到，所以只能靠这里钉。
        var ledger = ReadLedger();
        var statuses = ReadFindingStatuses();
        var accepted = statuses.Where(entry => entry.Value == "accepted-risk")
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(statuses.Count >= 50, $"台账只剩 {statuses.Count} 条，读法多半已经失效。");
        Assert.NotEmpty(accepted);

        Assert.Equal(statuses.Count, ReadProseCount(ledger, LedgerTotalCount));
        Assert.Equal(statuses.Count, ReadProseCount(ledger, FindingsOwnerCount));
        Assert.Equal(statuses.Values.Count(status => status == "resolved"), ReadProseCount(ledger, ResolvedCount));
        Assert.Equal(statuses.Values.Count(status => status == "open"), ReadProseCount(ledger, OpenCount));
        Assert.Equal(accepted.Count, ReadProseCount(ledger, AcceptedRiskCount));

        // 条数对了但换了一号同样是漂移：那段散文点名的编号集合也要等于台账的 accepted-risk 集。
        var paragraph = AcceptedRiskParagraph.Matches(ledger);
        Assert.True(
            paragraph.Count == 1,
            $"「已书面接受风险」那段应恰好出现一次，实际 {paragraph.Count} 处；本契约靠它读编号集合。");

        var named = FindingIdReference.Matches(paragraph[0].Value)
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(
            named.SetEquals(accepted),
            $"「已书面接受风险」段落点名的编号与台账不一致：段落 {string.Join(' ', named.Order(StringComparer.Ordinal))} / 台账 {string.Join(' ', accepted.Order(StringComparer.Ordinal))}。");
    }

    [Fact]
    public void ResolvedFindingsTable_MatchesTheLedgerItSummarises()
    {
        // §3.4 的类别表是已解决发现的检索索引；它同时存在三类漂移：某类漏号、某类多号、
        // 多出一个台账里根本不存在的编号（AUD-MAINT-008 就从未在 findings.json 里出现过）。
        // 三类的判据都来自台账，因此这里逐类比对条数与编号集合，而不是只比对总数。
        var ledger = ReadLedger();
        var expected = ReadFindingStatuses()
            .Where(entry => entry.Value == "resolved")
            .GroupBy(entry => entry.Key.Split('-')[1], StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.Key.Split('-')[2]).Order(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        var rows = ResolvedTableRow.Matches(ledger);
        Assert.True(
            rows.Count == expected.Count,
            $"§3.4 的类别表有 {rows.Count} 行，台账里 resolved 覆盖 {expected.Count} 个类别（{string.Join(' ', expected.Keys.Order(StringComparer.Ordinal))}）。");

        foreach (Match row in rows)
        {
            var prefix = row.Groups["prefix"].Value;
            Assert.True(
                expected.ContainsKey(prefix),
                $"§3.4 的类别表出现了台账里不存在的类别 `AUD-{prefix}`。");
            var expectedNumbers = expected[prefix];

            var declaredCount = int.Parse(row.Groups["count"].Value, CultureInfo.InvariantCulture);
            Assert.True(
                declaredCount == expectedNumbers.Count,
                $"§3.4 的 `AUD-{prefix}` 行写 {declaredCount} 条，台账里 resolved 的是 {expectedNumbers.Count} 条。");

            var listed = ListedFindingNumber.Matches(row.Groups["ids"].Value)
                .Select(match => match.Groups["number"].Value)
                .Order(StringComparer.Ordinal)
                .ToList();
            Assert.True(
                listed.SequenceEqual(expectedNumbers, StringComparer.Ordinal),
                $"§3.4 的 `AUD-{prefix}` 行编号与台账不一致：表里 {string.Join(' ', listed)} / 台账 {string.Join(' ', expectedNumbers)}。");

            expected.Remove(prefix);
        }

        Assert.Empty(expected);
    }

    private static string ReadLedger() => File.ReadAllText(TestRepository.FromRepositoryRoot(LedgerRelativePath));

    private static int ReadProseCount(string ledger, Regex pattern)
    {
        var matches = pattern.Matches(ledger);
        Assert.True(
            matches.Count == 1,
            $"{LedgerRelativePath} 里 {pattern} 应恰好命中一处，实际 {matches.Count} 处：散文锚点必须唯一，否则契约读到的可能不是 §3.4 那段。");

        return int.Parse(matches[0].Groups["count"].Value, CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, string> ReadFindingStatuses()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(TestRepository.FromRepositoryRoot(FindingsRelativePath)));
        var findings = document.RootElement.GetProperty("findings");

        var statuses = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var finding in findings.EnumerateObject())
        {
            statuses[finding.Name] = finding.Value.GetProperty("status").GetString()!;
        }

        return statuses;
    }
}
