using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LocalizationTerminologyTests
{
    static LocalizationTerminologyTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void LocaleFiles_CanonicalDomainTerms_ArePresentAndDistinct(string fileName)
    {
        var locale = ReadLocale(fileName);

        var launchCheck = GetRequiredValue(locale, "launchCheck");
        var resourcePanel = GetRequiredValue(locale, "resourcePanel");
        Assert.False(string.IsNullOrWhiteSpace(launchCheck));
        Assert.False(string.IsNullOrWhiteSpace(resourcePanel));
        Assert.NotEqual(launchCheck, resourcePanel);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void LocaleFiles_ConsumedResourceCopy_UsesLocalizedResourcesTerm(string fileName)
    {
        var locale = ReadLocale(fileName);
        var consumedKeys = new[]
        {
            "resourcePanelDescription",
            "resourcePanelLocalizedVersion",
            "setupWizardDownloadSourceCafeDescription",
            "setupWizardDownloadSourceCafeRecommendationReason",
            "setupWizardDownloadSourceHint"
        };

        var localizedResourcesTerm = GetRequiredValue(locale, "resourcePanelLocalizedVersion");
        Assert.All(consumedKeys, key => Assert.Contains(
            localizedResourcesTerm,
            GetRequiredValue(locale, key),
            StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    public void LocaleFiles_ChineseBannerKeys_UseConsistentTerminology(string fileName)
    {
        var locale = ReadLocale(fileName);

        Assert.False(string.IsNullOrWhiteSpace(locale["banner"]));
        Assert.Equal(locale["banner"], locale["banners"]);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void LocaleFiles_CarouselPage_UsesCompactLanguageNeutralFormat(string fileName)
    {
        var locale = ReadLocale(fileName);

        Assert.Equal("{0} / {1}", locale["carouselPage"]);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void LocaleFiles_FatalConcept_UsesSameTermAcrossFilterAndLevel(string fileName)
    {
        var locale = ReadLocale(fileName);

        Assert.Equal(locale["logFilterFatal"], locale["logLevelFatal"]);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void LocaleFiles_ProxyModes_HaveDistinctNamesAndDescriptions(string fileName)
    {
        var locale = ReadLocale(fileName);
        var names = new[] { locale["proxyAuto"], locale["proxyDirect"], locale["proxySystem"] };
        var descriptions = new[]
        {
            locale["setupWizardProxyAutoDescription"],
            locale["setupWizardProxyDirectDescription"],
            locale["setupWizardProxySystemDescription"]
        };

        Assert.All(names, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.All(descriptions, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.Equal(3, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, descriptions.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    public void LocaleFiles_RemoteContentCard_UsesBannerTerminology(string fileName)
    {
        var locale = ReadLocale(fileName);

        Assert.Contains(locale["banner"], locale["showRemoteContentCard"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void LocaleFiles_RefreshTooltip_ExplainsUserVisibleScope(string fileName)
    {
        var locale = ReadLocale(fileName);

        Assert.False(string.IsNullOrWhiteSpace(locale["refreshTooltip"]));
        Assert.DoesNotContain("API", locale["refreshTooltip"], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void LocaleFiles_ManifestModeLabels_AreConsistentAcrossSettingsAndStatus(string fileName)
    {
        var locale = ReadLocale(fileName);

        Assert.Equal(locale["launchCheckLocalManifest"], locale["statusLaunchCheckLocal"]);
        Assert.Equal(locale["launchCheckRemoteManifest"], locale["statusLaunchCheckRemote"]);
    }

    [Fact]
    public void SetupWizard_AutomaticLanguageSummary_UsesLocalizedLocaleKey()
    {
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.SimplifiedChinese);
        var viewModel = new SetupWizardViewModel(
            localizer,
            new GameInstallationPath(),
            new LocalInstallationStateStore(),
            new LocalDiagnostics())
        {
            Language = LauncherLanguages.Auto
        };

        viewModel.NextCommand.Execute(null);

        Assert.Equal(localizer.T("languageAuto"), viewModel.Steps[0].Summary);
        Assert.DoesNotContain("Auto", viewModel.Steps[0].Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void LanguageSelectors_AutomaticOption_UsesLocalizedLocaleKey()
    {
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.SimplifiedChinese);
        var settingsOptions = new SettingsOptionsViewModel(localizer, new DiskSpaceService());
        var setupWizard = new SetupWizardViewModel(
            localizer,
            new GameInstallationPath(),
            new LocalInstallationStateStore(),
            new LocalDiagnostics());
        var dialogs = new DialogsViewModel(
            localizer,
            new NoticeStateService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "notice.json")),
            setupWizard);

        settingsOptions.RefreshDisplayNames();
        dialogs.ApplyLanguage();

        Assert.Equal(
            localizer.T("languageAuto"),
            settingsOptions.Language.Single(option => option.Code == LauncherLanguages.Auto).DisplayName);
        Assert.Equal(
            localizer.T("languageAuto"),
            dialogs.LanguageOptions.Single(option => option.Code == LauncherLanguages.Auto).DisplayName);
    }

    private static Dictionary<string, string> ReadLocale(string locale)
    {
        if (locale is not ("en" or "zh-Hans" or "zh-Hant" or "ja"))
        {
            throw new ArgumentException($"Unexpected locale: {locale}", nameof(locale));
        }

        var resxFile = locale == "en"
            ? "LauncherStrings.resx"
            : $"LauncherStrings.{locale}.resx";
        var path = Path.Combine(TestLocalizationHelper.FindProjectRoot(), "Resources", resxFile);
        return TestLocalizationHelper.ReadResx(path);
    }

    private static string GetRequiredValue(Dictionary<string, string> locale, string key)
    {
        Assert.True(locale.TryGetValue(key, out var value), $"Missing locale key: {key}");
        return value;
    }
}
