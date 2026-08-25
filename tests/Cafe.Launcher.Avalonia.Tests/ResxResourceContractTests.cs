using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
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
    public void Resx_NeutralContainsAll447Keys()
    {
        Assert.Equal(447, ResxValues["en"].Count);
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
    public void ProductionLiteralResourceKeys_ExistInNeutralResources()
    {
        var root = TestLocalizationHelper.FindProjectRoot();
        var keyPattern = new Regex("\\.(?:T|F)\\(\\\"(?<key>[^\\\"]+)\\\"", RegexOptions.Compiled);
        var productionFiles = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        var referencedKeys = productionFiles
            .SelectMany(path => keyPattern.Matches(File.ReadAllText(path))
                .Select(match => match.Groups["key"].Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(referencedKeys);
        Assert.All(referencedKeys, key => Assert.True(
            ResxValues["en"].ContainsKey(key), $"Missing neutral resource key: {key}"));
    }

    [Fact]
    public void DynamicProductionResourceKeys_ExistInNeutralResources()
    {
        Assert.All(DynamicProductionKeys, key => Assert.Contains(key, ResxValues["en"]));
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
