using System.Globalization;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherCultureResolverTests
{
    [Theory]
    [InlineData(LauncherLanguages.English, LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese, LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese, LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese, LauncherLanguages.Japanese)]
    public void ResolveEffectiveLanguage_WhenExplicitCode_ReturnsSameLanguage(string input, string expected)
    {
        var result = LauncherCultureResolver.ResolveEffectiveLanguage(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(LauncherLanguages.Auto)]
    [InlineData(null)]
    [InlineData("unknown")]
    public void ResolveEffectiveLanguage_WhenAutoOrUnknown_ReturnsSystemLanguage(string? input)
    {
        var result = LauncherCultureResolver.ResolveEffectiveLanguage(input);

        Assert.Contains(result, new[] {
            LauncherLanguages.English,
            LauncherLanguages.SimplifiedChinese,
            LauncherLanguages.TraditionalChinese,
            LauncherLanguages.Japanese
        });
        Assert.NotEqual(LauncherLanguages.Auto, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ResolveSystemLanguage_WhenCultureNameIsMissing_ReturnsEnglish(string? cultureName)
    {
        var result = LauncherCultureResolver.ResolveSystemLanguage(cultureName);

        Assert.Equal(LauncherLanguages.English, result);
    }

    [Theory]
    [InlineData("zh-Hans-TW", LauncherLanguages.SimplifiedChinese)]
    [InlineData("zh-Hant-CN", LauncherLanguages.TraditionalChinese)]
    [InlineData("zh-Hantx", LauncherLanguages.SimplifiedChinese)]
    [InlineData("zh-xHant-x", LauncherLanguages.SimplifiedChinese)]
    [InlineData("zh-ATW", LauncherLanguages.SimplifiedChinese)]
    [InlineData("zh-fooTW", LauncherLanguages.SimplifiedChinese)]
    public void ResolveSystemLanguage_WhenCultureNameContainsScriptOrRegionSubtags_ReturnsExpected(
        string cultureName,
        string expected)
    {
        var result = LauncherCultureResolver.ResolveSystemLanguage(cultureName);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("zh-Hantx", false)]
    [InlineData("zh-xHant-x", false)]
    [InlineData("zh-fooTW", false)]
    [InlineData("zh-Hant", true)]
    [InlineData("zh-TW", true)]
    [InlineData("zh-Hans-TW", false)]
    [InlineData("zh-Hant-CN", true)]
    public void IsTraditionalChineseRegion_WhenSubtagsAreMissingOrMalformed_ReturnsExpected(
        string? cultureName,
        bool expected)
    {
        var result = LauncherCultureResolver.IsTraditionalChineseRegion(cultureName);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("zh-CN", LauncherLanguages.SimplifiedChinese)]
    [InlineData("zh-SG", LauncherLanguages.SimplifiedChinese)]
    [InlineData("zh-Hans", LauncherLanguages.SimplifiedChinese)]
    [InlineData("zh-TW", LauncherLanguages.TraditionalChinese)]
    [InlineData("zh-HK", LauncherLanguages.TraditionalChinese)]
    [InlineData("zh-MO", LauncherLanguages.TraditionalChinese)]
    [InlineData("zh-Hant", LauncherLanguages.TraditionalChinese)]
    [InlineData("ja-JP", LauncherLanguages.Japanese)]
    [InlineData("ja", LauncherLanguages.Japanese)]
    [InlineData("en-US", LauncherLanguages.English)]
    [InlineData("fr-FR", LauncherLanguages.English)]
    public void ResolveSystemLanguage_WhenCultureName_ReturnsExpected(string cultureName, string expected)
    {
        var result = LauncherCultureResolver.ResolveSystemLanguage(cultureName);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("zh-Hant", true)]
    [InlineData("zh-Hant-TW", true)]
    [InlineData("zh-Hans", false)]
    [InlineData("zh-Hans-CN", false)]
    [InlineData("zh-TW", true)]
    [InlineData("zh-HK", true)]
    [InlineData("zh-MO", true)]
    [InlineData("zh-CN", false)]
    [InlineData("zh-SG", false)]
    [InlineData("zh", false)]
    public void IsTraditionalChineseRegion_WhenScriptOrRegion_ReturnsExpected(string cultureName, bool expected)
    {
        var result = LauncherCultureResolver.IsTraditionalChineseRegion(cultureName);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(LauncherLanguages.English, "en-US")]
    [InlineData(LauncherLanguages.SimplifiedChinese, "zh-CN")]
    [InlineData(LauncherLanguages.TraditionalChinese, "zh-TW")]
    [InlineData(LauncherLanguages.Japanese, "ja-JP")]
    public void GetCultureFor_ReturnsExactMapping(string language, string expectedName)
    {
        var culture = LauncherCultureResolver.GetCultureFor(language);

        Assert.Equal(expectedName, culture.Name);
    }

    [Fact]
    public void GetCultureFor_WhenUnknown_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LauncherCultureResolver.GetCultureFor("invalid"));
    }
}
