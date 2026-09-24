using System.Xml.Linq;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

// CrashReportWindow 自带 token 族（隔离报告进程，主 App 资源字典可能不可用），
// 因此在共享令牌纪律之外。这一卷给它两条自己的扫描（D16）：族内不留孤儿令牌，
// 消费点不用裸字面量——此前它既无守卫（被 UiStyleContractTests 显式豁免），
// 族里也确实躺着两个从未被消费的 Crash.Spacing 条目。
public sealed partial class UiStyleContractTests
{
    private const string CrashReportView = "Views/CrashReportWindow.axaml";

    /// <summary>CrashReportWindow 上必须走令牌的尺寸与排版属性（消费点，不含令牌自身定义；
    /// 属性形式与 Setter 的 Property/Value 形式都在扫描内）。</summary>
    private static readonly string[] CrashTokenizedAttributes =
    [
        "Width",
        "Height",
        "MinWidth",
        "MinHeight",
        "MaxWidth",
        "MaxHeight",
        "Margin",
        "Padding",
        "BorderThickness",
        "CornerRadius",
        "FontSize",
        "LineHeight",
        "FontWeight",
        "FontFamily"
    ];

    [Fact]
    public void CrashReportTokens_AreEveryDeclaredTokenConsumed()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot(CrashReportView));
        var declared = DeclaredCrashTokenKeys(document);
        var referenced = document
            .Descendants()
            .SelectMany(element => element.Attributes())
            .SelectMany(attribute => CrashTokenReferences(attribute.Value))
            .ToHashSet(StringComparer.Ordinal);

        // 反空转基线：族里今天有 24 个条目（内含颜色主题字典），扫到零个即扫描失效。
        Assert.True(declared.Count >= 20, $"Crash.* token family shrank unexpectedly: {declared.Count} declared.");
        Assert.Equal([], declared.Where(key => !referenced.Contains(key)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CrashReportView_SizingAndTypographyAttributes_DoNotUseRawLiterals()
    {
        var document = XDocument.Load(TestRepository.FromApplicationRoot(CrashReportView));
        var xKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
        var rawAttributes = document
            .Descendants()
            .Where(element => element.Attribute(xKey) is null)
            .SelectMany(element => element.Attributes())
            .Where(attribute => CrashTokenizedAttributes.Contains(attribute.Name.LocalName, StringComparer.Ordinal))
            .Where(attribute => !attribute.Value.TrimStart().StartsWith('{'))
            .Select(attribute => $"{CrashReportView}: {attribute.Name.LocalName}=\"{attribute.Value}\"");
        var rawSetterValues = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Setter")
            .SelectMany(element => element.Attributes()
                .Where(attribute => attribute.Name.LocalName == "Property"
                    && CrashTokenizedAttributes.Contains(attribute.Value, StringComparer.Ordinal))
                .Select(attribute => (Property: attribute.Value, Value: element.Attribute("Value")?.Value)))
            .Where(item => item.Value is not null
                && !item.Value.TrimStart().StartsWith('{'))
            .Select(item => $"{CrashReportView}: Setter {item.Property}=\"{item.Value}\"");

        Assert.Equal([], rawAttributes.Concat(rawSetterValues).ToArray());
    }

    private static HashSet<string> DeclaredCrashTokenKeys(XDocument document)
    {
        var xKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
        return document
            .Descendants()
            .Select(element => element.Attribute(xKey)?.Value)
            .Where(key => key?.StartsWith("Crash.", StringComparison.Ordinal) == true)
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> CrashTokenReferences(string attributeValue)
    {
        const string staticPrefix = "{StaticResource ";
        const string dynamicPrefix = "{DynamicResource ";
        if (!attributeValue.StartsWith('{') || !attributeValue.EndsWith('}'))
        {
            yield break;
        }

        var prefix = attributeValue.StartsWith(staticPrefix, StringComparison.Ordinal)
            ? staticPrefix
            : attributeValue.StartsWith(dynamicPrefix, StringComparison.Ordinal)
                ? dynamicPrefix
                : null;
        if (prefix is null)
        {
            yield break;
        }

        var key = attributeValue[prefix.Length..^1];
        if (key.StartsWith("Crash.", StringComparison.Ordinal))
        {
            yield return key;
        }
    }
}
