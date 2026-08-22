using System.Globalization;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LanguageFontFamilyServiceTests : LocalizationTestBase
{
    [Theory]
    [InlineData(LauncherLanguages.English, "Segoe UI")]
    [InlineData(LauncherLanguages.SimplifiedChinese, "Microsoft YaHei UI")]
    [InlineData(LauncherLanguages.TraditionalChinese, "Microsoft JhengHei UI")]
    [InlineData(LauncherLanguages.Japanese, "Yu Gothic UI")]
    public void GetForEffectiveLanguage_ReturnsExactSystemFont(
        string language,
        string expectedFamilyName)
    {
        var result = LanguageFontFamilyService.GetForEffectiveLanguage(language);

        Assert.Equal(expectedFamilyName, result.Name);
    }

    [Fact]
    public void GetForEffectiveLanguage_WhenLanguageIsAuto_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LanguageFontFamilyService.GetForEffectiveLanguage(LauncherLanguages.Auto));
    }

    [Fact]
    public void Auto_IsResolvedByLocalizationBeforeFontMapping()
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            var localizer = new LocalizationService();

            var effectiveLanguage = localizer.SetLanguage(LauncherLanguages.Auto);
            var result = LanguageFontFamilyService.GetForEffectiveLanguage(effectiveLanguage);

            Assert.Equal(LauncherLanguages.SimplifiedChinese, effectiveLanguage);
            Assert.Equal("Microsoft YaHei UI", result.Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
