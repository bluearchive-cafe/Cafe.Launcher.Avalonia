using System.Text.Json;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LocalizationTerminologyTests
{
    static LocalizationTerminologyTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Theory]
    [InlineData("en.json", "Manifest", "Launch verification", "Resource Panel", "Localized resources")]
    [InlineData("zh-Hans.json", "文件清单", "启动校验", "资源面板", "本地化资源")]
    [InlineData("zh-Hant.json", "檔案清單", "啟動校驗", "資源面板", "本地化資源")]
    [InlineData("ja.json", "マニフェスト", "起動チェック", "リソースパネル", "ローカライズリソース")]
    public void LocaleFiles_CanonicalDomainTerms_MatchFourLanguageBaseline(
        string fileName,
        string expectedManifest,
        string expectedLaunchVerification,
        string expectedResourcePanel,
        string expectedLocalizedResources)
    {
        var locale = ReadLocale(fileName);

        Assert.Equal(expectedManifest, GetRequiredValue(locale, "manifest"));
        Assert.Equal(expectedLaunchVerification, GetRequiredValue(locale, "launchCheck"));
        Assert.Equal(expectedResourcePanel, GetRequiredValue(locale, "resourcePanel"));
        Assert.Equal(expectedLocalizedResources, GetRequiredValue(locale, "localizedResources"));
    }

    [Theory]
    [InlineData("zh-Hans.json", "横幅")]
    [InlineData("zh-Hant.json", "橫幅")]
    public void LocaleFiles_ChineseBannerKeys_UseBannerTerminology(string fileName, string expected)
    {
        var locale = ReadLocale(fileName);

        Assert.Equal(expected, locale["banner"]);
        Assert.Equal(expected, locale["banners"]);
    }

    [Theory]
    [InlineData("en.json")]
    [InlineData("zh-Hans.json")]
    [InlineData("zh-Hant.json")]
    [InlineData("ja.json")]
    public void LocaleFiles_FatalConcept_UsesSameTermAcrossFilterAndLevel(string fileName)
    {
        var locale = ReadLocale(fileName);

        Assert.Equal(locale["logFilterFatal"], locale["logLevelFatal"]);
    }

    [Theory]
    [InlineData("en.json", "Automatic system proxy", "Direct connection (no proxy)", "Configured system proxy",
        "Use the system's automatically detected proxy settings.", "Connect directly without a proxy.",
        "Use the proxy explicitly configured in system settings.")]
    [InlineData("zh-Hans.json", "自动检测系统代理", "直连（不使用代理）", "已配置的系统代理",
        "使用系统自动检测到的代理设置。", "不使用代理，直接连接。", "使用系统设置中明确配置的代理。")]
    [InlineData("zh-Hant.json", "自動偵測系統代理", "直連（不使用代理）", "已設定的系統代理",
        "使用系統自動偵測到的代理設定。", "不使用代理，直接連線。", "使用系統設定中明確設定的代理。")]
    [InlineData("ja.json", "システムプロキシを自動検出", "直接接続（プロキシなし）", "設定済みシステムプロキシ",
        "システムが自動検出したプロキシ設定を使用します。", "プロキシを使用せずに直接接続します。", "システム設定で明示的に構成されたプロキシを使用します。")]
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
    [InlineData("en.json")]
    [InlineData("zh-Hans.json")]
    [InlineData("zh-Hant.json")]
    [InlineData("ja.json")]
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
            new LocalInstallationStateStore())
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
            new LocalInstallationStateStore());
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

    private static Dictionary<string, string> ReadLocale(string fileName)
    {
        var json = File.ReadAllText(Path.Combine(FindProjectRoot(), "Assets", "Locales", fileName));
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? throw new InvalidDataException($"{fileName} is not a localization dictionary.");
    }

    private static string GetRequiredValue(Dictionary<string, string> locale, string key)
    {
        Assert.True(locale.TryGetValue(key, out var value), $"Missing locale key: {key}");
        return value;
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cafe.Launcher.Avalonia.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cafe.Launcher.Avalonia.csproj was not found.");
    }
}
