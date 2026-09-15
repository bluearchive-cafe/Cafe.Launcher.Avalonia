using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Resources;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ResxResourceContractTests
{
    private static readonly string ResxDir;
    private static readonly string[] AllLocales = ["en", "zh-Hans", "zh-Hant", "ja"];
    private static readonly string[] DynamicProductionKeys =
    [
        "fileOperationFailed",
        "gameLaunchFailed",
        "networkWithMessage"
    ];
    private static readonly Dictionary<string, Dictionary<string, string>> ResxValues = new(StringComparer.Ordinal);

    static ResxResourceContractTests()
    {
        ResxDir = Path.Combine(TestLocalizationHelper.FindProjectRoot(), "Resources");

        foreach (var locale in AllLocales)
        {
            var file = locale == "en"
                ? "LauncherStrings.resx"
                : $"LauncherStrings.{locale}.resx";
            ResxValues[locale] = TestLocalizationHelper.ReadResx(Path.Combine(ResxDir, file));
        }
    }

    [Fact]
    public void Resx_NeutralContainsAllExpectedKeys()
    {
        // 555 → 557（ADR-030 的两个新串）→ 558（对话框先弹、尺寸后到，见 ADR-030 的第 4 条决策）
        // → 563（「游戏启动后的行为」的设置行、说明、保持窗口选项与两条启动提示，见 ADR-031；
        // 最小化与退出两个选项复用既有串，不新增同义 key）
        // → 564（彻底清除完成但有个别项目删不掉时的结果串，见 ADR-030）
        // → 565（复核轮：残留与「Prefix 已保留」同时成立时的结果串，两种事实都要在提示里）。
        Assert.Equal(565, ResxValues["en"].Count);
    }

    [Fact]
    public void Resx_AllFourFiles_HaveIdenticalKeySets()
    {
        var enKeys = ResxValues["en"].Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        foreach (var locale in new[] { "zh-Hans", "zh-Hant", "ja" })
        {
            var keys = ResxValues[locale].Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            Assert.Equal(enKeys, keys);
        }
    }

    [Fact]
    public void Resx_AllFourFiles_KeysAreSortedOrdinal()
    {
        foreach (var locale in AllLocales)
        {
            var keys = ResxValues[locale].Keys.ToArray();
            Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), keys);
        }
    }

    [Fact]
    public void Resx_NoTwoKeysDifferOnlyByCase()
    {
        foreach (var locale in AllLocales)
        {
            var keys = ResxValues[locale].Keys.Select(k => k.ToLowerInvariant()).ToArray();
            Assert.Equal(keys.Distinct().Count(), keys.Length);
        }
    }

    [Fact]
    public void Resx_FormatPlaceholders_MatchNeutralForEveryKey()
    {
        var enDict = ResxValues["en"];
        var placeholderPattern = new Regex(@"\{\d+(?:[^}]*)\}", RegexOptions.Compiled);

        foreach (var locale in new[] { "zh-Hans", "zh-Hant", "ja" })
        {
            var localeDict = ResxValues[locale];
            foreach (var key in enDict.Keys)
            {
                var enPlaceholders = placeholderPattern.Matches(enDict[key])
                    .Select(m => m.Value).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                var localePlaceholders = placeholderPattern.Matches(localeDict[key])
                    .Select(m => m.Value).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                Assert.Equal(enPlaceholders, localePlaceholders);
            }
        }
    }

    [Theory]
    [InlineData("en", "en-US")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("zh-Hant", "zh-TW")]
    [InlineData("ja", "ja-JP")]
    public void Resx_EachFormatValue_FormatsUnderItsOwnCulture(string locale, string cultureName)
    {
        var culture = new CultureInfo(cultureName);
        var placeholderPattern = new Regex(@"\{(\d+)(?:[^}]*)\}", RegexOptions.Compiled);
        var dict = ResxValues[locale];

        foreach (var (key, value) in dict)
        {
            var matches = placeholderPattern.Matches(value);
            if (matches.Count == 0) continue;

            var maxIndex = matches.Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Max();
            var args = new object[maxIndex + 1];
            for (var i = 0; i <= maxIndex; i++)
                args[i] = i;

            try
            {
                var formatted = string.Format(culture, value, args);
                Assert.NotNull(formatted);
                Assert.NotEmpty(formatted);
            }
            catch (FormatException)
            {
                Assert.Fail($"Key '{key}' in locale '{locale}' failed to format with {maxIndex + 1} args.");
            }
        }
    }

    [Fact]
    public void Resx_AllValues_AreNonEmpty()
    {
        foreach (var locale in AllLocales)
        {
            var dict = ResxValues[locale];
            foreach (var (key, value) in dict)
            {
                Assert.False(string.IsNullOrEmpty(value),
                    $"Key '{key}' in '{locale}' has an empty value.");
            }
        }
    }

    [Fact]
    public void ResourceManager_WhenAssemblyLoaded_ResolvesEveryKeyForEveryCulture()
    {
        var cultures = new Dictionary<string, CultureInfo>(StringComparer.Ordinal)
        {
            ["en"] = new CultureInfo("en-US"),
            ["zh-Hans"] = new CultureInfo("zh-CN"),
            ["zh-Hant"] = new CultureInfo("zh-TW"),
            ["ja"] = new CultureInfo("ja-JP")
        };

        foreach (var (locale, culture) in cultures)
        {
            foreach (var key in ResxValues[locale].Keys)
            {
                var result = LauncherStrings.ResourceManager.GetString(key, culture);
                Assert.NotNull(result);
                Assert.Equal(
                    ResxValues[locale][key].Replace("\r\n", "\n"),
                    result.Replace("\r\n", "\n"));
            }
        }
    }

    [Theory]
    [InlineData("en-GB", "en", "languageAuto")]
    [InlineData("zh-HK", "zh-Hant", "languageAuto")]
    public void ResourceManager_WhenUsingRegionalSystemCulture_UsesExpectedResourceFallback(
        string cultureName,
        string expectedLocale,
        string key)
    {
        var value = LauncherStrings.ResourceManager.GetString(key, new CultureInfo(cultureName));

        Assert.Equal(ResxValues[expectedLocale][key], value);
    }

    [Fact]
    public void LauncherStrings_StronglyTypedAccessors_CoverEveryNeutralResourceKey()
    {
        var accessors = typeof(LauncherStrings)
            .GetProperties(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ResxValues["en"].Count, accessors.Count);
        foreach (var key in ResxValues["en"].Keys)
        {
            var accessorName = char.ToUpperInvariant(key[0]) + key[1..];
            Assert.Contains(accessorName, accessors);
        }
    }

    [Fact]
    public void ProductionCallSites_DoNotUseRawResourceKeyLiterals()
    {
        var root = TestLocalizationHelper.FindProjectRoot();
        var tCallPattern = new Regex("\\.(?:T|F)\\(\\\"(?<key>[^\\\"]+)\\\"", RegexOptions.Compiled);
        var i18nIndexPattern = new Regex("\\bI18n\\[\\\"(?<key>[^\\\"]+)\\\"\\]", RegexOptions.Compiled);
        var productionFiles = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        var rawLiterals = productionFiles
            .SelectMany(path => tCallPattern.Matches(File.ReadAllText(path))
                .Select(match => match.Groups["key"].Value)
                .Concat(i18nIndexPattern.Matches(File.ReadAllText(path))
                    .Select(match => match.Groups["key"].Value)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            rawLiterals.Length == 0,
            "Production code must reference LocalizationKeys constants instead of raw " +
            "resource-key literals (see AGENTS.md). Raw keys found: " +
            string.Join(", ", rawLiterals));
    }

    [Fact]
    public void LocalizationKeys_Constants_CoverEveryNeutralResourceKey()
    {
        var constants = typeof(LocalizationKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetValue(null)!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ResxValues["en"].Count, constants.Count);
        foreach (var key in ResxValues["en"].Keys)
        {
            Assert.True(constants.Contains(key), $"LocalizationKeys is missing constant for resource key: {key}");
        }
    }

    [Fact]
    public void DynamicProductionResourceKeys_ExistInNeutralResources()
    {
        Assert.All(DynamicProductionKeys, key => Assert.Contains(key, ResxValues["en"]));
    }

    /// <summary>
    /// XAML 面的键契约（架构评审 2026-09-14 · 候选 04）。.cs 面那条键契约靠「禁止裸字面量、
    /// 强制引用 <see cref="LocalizationKeys"/> 常量」实现，而 XAML 绑定路径本身就是字符串、
    /// 无法引用 C# 常量，于是同一份词表在 .axaml 侧退化成无人检查的字符串——拼错键时构建与
    /// 测试全绿，运行期由 <c>LocalizationService.T</c> 的 null 分支静默降级为固定串
    /// （<c>LocalizationService.cs:214</c>）。这一半只做存在性检查：XAML 里写的键必须在中立
    /// .resx 里存在。其余三个语言文件的键集由 Resx_AllFourFiles_HaveIdenticalKeySets 钉住，
    /// 故比对中立文件即覆盖四个语言。
    /// </summary>
    private static readonly Regex XamlResourceKeyPattern = new(
        """I18n\[\s*['"]?(?<key>[A-Za-z_][A-Za-z0-9_]*)['"]?\s*\]""",
        RegexOptions.Compiled);

    private static readonly Regex XamlCommentPattern = new(
        "<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// 扫描域＝应用工程下全部 <c>.axaml</c>（递归，排除 bin/obj），不维护手抄文件清单：
    /// AUD-TEST-006 已证明这类清单会漂移一次（拆分出主叠层时漏掉共享的 ViewFiles，
    /// 令牌扫描就此失去对最新叠层的覆盖）。
    /// </summary>
    private static string[] XamlFilesInScope()
    {
        var root = TestLocalizationHelper.FindProjectRoot();
        var separator = Path.DirectorySeparatorChar;
        var files = Directory
            .GetFiles(root, "*.axaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(files);
        return files;
    }

    /// <summary>
    /// 取出 XAML 里的键引用及其行号。注释先剥掉：注掉的绑定不该让守卫红，
    /// 也不该假装自己是活引用。
    /// </summary>
    /// <remarks>
    /// 剥法是「抹成等长空白」而不是删掉：注释里的换行必须留在原处，否则它之后的每个键
    /// 报出的行号都会偏小（多行注释吃掉几行就偏几行），失败信息会把人指到别的行上。
    /// 2026-09-15 复核轮修正——此前用 Replace(…, string.Empty)，实测 16 个含键文件里有
    /// 4 个、约三成的键行号不准。
    /// </remarks>
    private static (string Key, int Line)[] ResourceKeysInXaml(string text)
    {
        var scanned = XamlCommentPattern.Replace(
            text,
            match => string.Concat(match.Value.Select(character => character == '\n' ? '\n' : ' ')));
        return XamlResourceKeyPattern
            .Matches(scanned)
            .Select(match => (
                match.Groups["key"].Value,
                scanned.Take(match.Index).Count(character => character == '\n') + 1))
            .ToArray();
    }

    /// <summary>
    /// 正相断言：扫描器必须读到两种合法的字面量键形态，且不被注释与无关索引器迷惑。
    /// 它是唯一能证明提取本身有效的断言——若它失效，下方两条契约会退化为永远为真。
    /// </summary>
    [Fact]
    public void XamlResourceKeyScan_ReadsBothLiteralForms_AndIgnoresComments()
    {
        const string fixture = """
            <TextBlock Text="{Binding Shell.I18n[close]}"/>
            <TextBlock Text="{Binding Shell.I18n['cancel']}"/>
            <!-- <TextBlock Text="{Binding Shell.I18n[retiredKey]}"/> -->
            <TextBlock Text="{Binding Shell.Other[close]}"/>
            """;

        var keys = ResourceKeysInXaml(fixture).Select(entry => entry.Key).ToArray();

        Assert.Equal(new[] { "close", "cancel" }, keys);
    }

    /// <summary>
    /// 行号本身也要准：失败信息是 <c>文件:行号 → 键</c>，行号偏小就等于把人指到别的行上。
    /// 多行注释被抹掉换行时，它之后的每个键都会偏小——所以这一条钉住「注释吃掉的行还算数」。
    /// </summary>
    [Fact]
    public void XamlResourceKeyScan_ReportsLinesThatSurviveMultiLineComments()
    {
        const string fixture = """
            <UserControl>
              <!--
                <TextBlock Text="{Binding Shell.I18n[retired]}"/>
                <TextBlock Text="{Binding Shell.I18n[alsoRetired]}"/>
              -->
              <TextBlock Text="{Binding Shell.I18n[close]}"/>
            </UserControl>
            """;

        var entry = Assert.Single(ResourceKeysInXaml(fixture));

        Assert.Equal("close", entry.Key);
        Assert.Equal(6, entry.Line);
    }

    [Fact]
    public void XamlResourceBindings_UseOnlyKeysThatExistInNeutralResources()
    {
        var root = TestLocalizationHelper.FindProjectRoot();
        var missing = XamlFilesInScope()
            .SelectMany(path => ResourceKeysInXaml(File.ReadAllText(path))
                .Select(entry => (
                    File: Path.GetRelativePath(root, path).Replace('\\', '/'),
                    entry.Key,
                    entry.Line)))
            .Where(entry => !ResxValues["en"].ContainsKey(entry.Key))
            .Select(entry => $"{entry.File}:{entry.Line} → {entry.Key}")
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "XAML 绑定了中立 resx 中不存在的资源键，运行期会静默降级为 \"Localization unavailable.\"。"
            + "请改用 Resources/LauncherStrings.resx 里的键；若这确实是按变量索引的绑定而非字面量键，"
            + "在此显式豁免并说明理由，不要放宽 XamlResourceKeyPattern："
            + string.Join(", ", missing));
    }

    /// <summary>
    /// 反空转：扫描域或提取模式一旦失效（递归退化成 TopDirectoryOnly、绑定形态变化后正则
    /// 不再命中），上一条契约会变成永远为真的空断言。基线为候选 04 落地时的实测值；真移除
    /// 了绑定／文案就同步下调，扫描失效应表现为红。
    /// </summary>
    [Fact]
    public void XamlKeyScan_StillCoversTheKeyBearingFiles()
    {
        const int landedKeyBearingFiles = 16;
        const int landedReferences = 545;
        const int landedDistinctKeys = 264;

        var perFile = XamlFilesInScope()
            .Select(path => ResourceKeysInXaml(File.ReadAllText(path)))
            .ToArray();
        var references = perFile.Sum(entries => entries.Length);
        var keyBearingFiles = perFile.Count(entries => entries.Length > 0);
        var distinctKeys = perFile
            .SelectMany(entries => entries)
            .Select(entry => entry.Key)
            .Distinct(StringComparer.Ordinal)
            .Count();

        Assert.True(
            keyBearingFiles >= landedKeyBearingFiles,
            $"扫描到 {keyBearingFiles} 个含键引用的 .axaml，低于落地基线 {landedKeyBearingFiles}——"
            + "先确认扫描域是否仍覆盖 Views/ 与 Controls/ 的子目录。");
        Assert.True(
            references >= landedReferences,
            $"扫描到 {references} 处键引用，低于落地基线 {landedReferences}——"
            + "先确认绑定形态是否变化到 XamlResourceKeyPattern 不再命中。");
        Assert.True(
            distinctKeys >= landedDistinctKeys,
            $"扫描到 {distinctKeys} 个不同的键，低于落地基线 {landedDistinctKeys}。");
    }

    [Fact]
    public void ResourceManager_ContainsExpectedSatelliteAssemblies()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(LauncherStrings).Assembly.Location)!;
        var assemblyName = typeof(LauncherStrings).Assembly.GetName().Name + ".resources.dll";

        foreach (var cultureName in new[] { "ja", "zh-Hans", "zh-Hant" })
        {
            Assert.True(File.Exists(Path.Combine(assemblyDirectory, cultureName, assemblyName)),
                $"Satellite assembly missing for {cultureName}.");
        }
    }
}
