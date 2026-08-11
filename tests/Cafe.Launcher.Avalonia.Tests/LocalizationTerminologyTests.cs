using Cafe.Launcher.Avalonia.Models;
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
    [InlineData("en", "Launch verification", "Resource Panel")]
    [InlineData("zh-Hans", "启动校验", "资源面板")]
    [InlineData("zh-Hant", "啟動校驗", "資源面板")]
    [InlineData("ja", "起動チェック", "リソースパネル")]
    public void LocaleFiles_CanonicalDomainTerms_MatchFourLanguageBaseline(
        string fileName,
        string expectedLaunchVerification,
        string expectedResourcePanel)
    {
        var locale = ReadLocale(fileName);

        Assert.Equal(expectedLaunchVerification, GetRequiredValue(locale, "launchCheck"));
        Assert.Equal(expectedResourcePanel, GetRequiredValue(locale, "resourcePanel"));
    }

    [Theory]
    [InlineData("en", "Localized resources")]
    [InlineData("zh-Hans", "本地化资源")]
    [InlineData("zh-Hant", "本地化資源")]
    [InlineData("ja", "ローカライズリソース")]
    public void LocaleFiles_ConsumedResourceCopy_UsesCanonicalLocalizedResourcesTerm(
        string fileName,
        string expectedTerm)
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

        Assert.All(consumedKeys, key => Assert.Contains(
            expectedTerm,
            GetRequiredValue(locale, key),
            StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("zh-Hans", "横幅")]
    [InlineData("zh-Hant", "橫幅")]
    public void LocaleFiles_ChineseBannerKeys_UseBannerTerminology(string fileName, string expected)
    {
        var locale = ReadLocale(fileName);

        Assert.Equal(expected, locale["banner"]);
        Assert.Equal(expected, locale["banners"]);
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
    [InlineData("en", "Automatic system proxy", "Direct connection (no proxy)", "System proxy (configured first)",
        "Use the launcher's default network behavior.", "Connect directly without a proxy.",
        "Prefer the explicitly configured system proxy; use the automatically detected system proxy when none is configured.")]
    [InlineData("zh-Hans", "自动检测系统代理", "直连（不使用代理）", "系统代理（优先使用显式配置）",
        "使用启动器默认网络行为。", "不使用代理，直接连接。", "优先使用系统中明确配置的代理；如未配置，则使用系统自动代理。")]
    [InlineData("zh-Hant", "自動偵測系統代理", "直連（不使用代理）", "系統代理（優先使用明確設定）",
        "使用啟動器預設網路行為。", "不使用代理，直接連線。", "優先使用系統中明確設定的代理；如未設定，則使用系統自動代理。")]
    [InlineData("ja", "システムプロキシを自動検出", "直接接続（プロキシなし）", "システムプロキシ（明示設定を優先）",
        "ランチャーの既定のネットワーク動作を使用します。", "プロキシを使用せずに直接接続します。", "システムで明示的に設定されたプロキシを優先し、未設定の場合は自動検出されたシステムプロキシを使用します。")]
    public void LocaleFiles_ProxyModes_HaveDistinctAccurateNamesAndDescriptions(
        string fileName,
        string expectedAuto,
        string expectedDirect,
        string expectedSystem,
        string expectedAutoDescription,
        string expectedDirectDescription,
        string expectedSystemDescription)
    {
        var locale = ReadLocale(fileName);
        var names = new[] { locale["proxyAuto"], locale["proxyDirect"], locale["proxySystem"] };
        var descriptions = new[]
        {
            locale["setupWizardProxyAutoDescription"],
            locale["setupWizardProxyDirectDescription"],
            locale["setupWizardProxySystemDescription"]
        };

        Assert.Equal(new[] { expectedAuto, expectedDirect, expectedSystem }, names);
        Assert.Equal(
            new[] { expectedAutoDescription, expectedDirectDescription, expectedSystemDescription },
            descriptions);
        Assert.Equal(3, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, descriptions.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("zh-Hans", "显示公告、横幅、新闻和社交媒体整张卡片")]
    [InlineData("zh-Hant", "顯示公告、橫幅、新聞和社群媒體整張卡片")]
    public void LocaleFiles_RemoteContentCard_UsesBannerTerminology(string fileName, string expected)
    {
        var locale = ReadLocale(fileName);

        Assert.Equal(expected, locale["showRemoteContentCard"]);
    }

    [Theory]
    [InlineData("en", "Reload server version, announcements, and local installation state.")]
    [InlineData("zh-Hans", "重新获取服务器版本和公告，并重新读取本地安装状态")]
    [InlineData("zh-Hant", "重新取得伺服器版本與公告，並重新讀取本機安裝狀態")]
    [InlineData("ja", "サーバーのバージョンとお知らせを再取得し、ローカルのインストール状態を再確認します。")]
    public void LocaleFiles_RefreshTooltip_ExplainsUserVisibleScope(string fileName, string expected)
    {
        var locale = ReadLocale(fileName);

        Assert.Equal(expected, locale["refreshTooltip"]);
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
        Assert.Equal("自动（跟随系统语言）", viewModel.Steps[0].Summary);
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
