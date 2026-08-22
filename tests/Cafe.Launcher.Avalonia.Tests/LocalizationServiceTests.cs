using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.IO;
using System.Text.RegularExpressions;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class LocalizationServiceTests : LocalizationTestBase
{
    static LocalizationServiceTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void T_WhenCurrentLanguageKeyIsMissing_DoesNotFallbackToEnglish()
    {
        LocalizationService.InitializeForTesting(new Dictionary<string, Dictionary<string, string>>
        {
            [LauncherLanguages.English] = new(StringComparer.Ordinal)
            {
                ["onlyEnglish"] = "English only"
            },
            [LauncherLanguages.Japanese] = new(StringComparer.Ordinal)
        });

        var service = new LocalizationService();
        service.SetLanguage(LauncherLanguages.Japanese);

        Assert.Equal("Localization unavailable.", service.T("onlyEnglish"));

        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void T_WhenRegionalAutoCultureRequested_UsesResourceManagerFallback()
    {
        var savedCulture = CultureInfo.CurrentCulture;
        var savedUiCulture = CultureInfo.CurrentUICulture;
        var savedDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var savedDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            CultureInfo.CurrentUICulture = new CultureInfo("zh-HK");
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
            TestLocalizationHelper.Initialize();

            var service = new LocalizationService();
            service.SetLanguage(LauncherLanguages.Auto);

            Assert.Equal(
                Resources.LauncherStrings.ResourceManager.GetString(
                    "languageAuto", CultureInfo.CurrentUICulture),
                service.T("languageAuto"));
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
            CultureInfo.CurrentUICulture = savedUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = savedDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = savedDefaultUiCulture;
            TestLocalizationHelper.Initialize();
        }
    }

    [Fact]
    public void SetLanguage_RaisesLanguageChanged()
    {
        var service = new LocalizationService();
        var changeCount = 0;
        service.LanguageChanged += (_, _) => changeCount++;

        var resolvedLanguage = service.SetLanguage(LauncherLanguages.SimplifiedChinese);

        Assert.Equal(LauncherLanguages.SimplifiedChinese, resolvedLanguage);
        Assert.Equal(1, changeCount);
    }

    [Theory]
    [InlineData(LauncherLanguages.English, "Open the launcher window", "Close the launcher process")]
    [InlineData(LauncherLanguages.SimplifiedChinese, "打开启动器窗口", "关闭启动器进程")]
    [InlineData(LauncherLanguages.TraditionalChinese, "開啟啟動器視窗", "關閉啟動器處理程序")]
    [InlineData(LauncherLanguages.Japanese, "ランチャーウィンドウを開く", "ランチャープロセスを終了")]
    public void T_WhenTrayKeysRequested_ReturnsLocalizedText(
        string language,
        string expectedOpenText,
        string expectedExitText)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);

        Assert.Equal(expectedOpenText, service.T("trayOpenLauncher"));
        Assert.Equal(expectedExitText, service.T("trayExitLauncher"));
    }

    [Theory]
    [InlineData(LauncherLanguages.English, "GitHub Repository", "Check for Updates")]
    [InlineData(LauncherLanguages.SimplifiedChinese, "GitHub 仓库", "检查更新")]
    [InlineData(LauncherLanguages.TraditionalChinese, "GitHub 儲存庫", "檢查更新")]
    [InlineData(LauncherLanguages.Japanese, "GitHub リポジトリ", "更新を確認")]
    public void T_WhenAboutActionKeysRequested_ReturnsLocalizedText(
        string language,
        string expectedRepositoryText,
        string expectedCheckUpdatesText)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);

        Assert.Equal(expectedRepositoryText, service.T("gitHubRepository"));
        Assert.Equal(expectedCheckUpdatesText, service.T("checkUpdates"));
    }

    [Theory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public void T_WhenPreflightAndVerificationKeysRequested_ReturnsLocalizedFormattedText(string language)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);

        Assert.NotEqual("diskSpaceCheck", service.F("diskSpaceCheck", "10B", "20B"));
        Assert.NotEqual("diskSpaceInsufficientDetail", service.F("diskSpaceInsufficientDetail", "10B", "--"));
        Assert.NotEqual("verificationRetry", service.F("verificationRetry", 2, 1, 3));
        Assert.NotEqual("verificationFailed", service.F("verificationFailed", 2));
        using var strings = new LocalizedTextCatalog(service);
        Assert.Equal(service.T("diskSpaceCheck"), strings["diskSpaceCheck"]);
        Assert.Equal(service.T("verificationFailed"), strings["verificationFailed"]);
    }

    [Theory]
    [InlineData(
        LauncherLanguages.English,
        "Log Files",
        "View, export, or open the directory containing logs")]
    [InlineData(
        LauncherLanguages.SimplifiedChinese,
        "日志文件",
        "查看、导出或打开日志所在目录")]
    [InlineData(
        LauncherLanguages.TraditionalChinese,
        "日誌檔案",
        "查看、匯出或開啟日誌所在目錄")]
    [InlineData(
        LauncherLanguages.Japanese,
        "ログファイル",
        "ログを表示、エクスポート、または保存先フォルダーを開く")]
    public void LocalizedTextCatalog_WhenLogFileKeysRequested_MapsLocalizedValues(
        string language,
        string expectedTitle,
        string expectedDescription)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);
        using var strings = new LocalizedTextCatalog(service);

        Assert.Equal(expectedTitle, strings["logFiles"]);
        Assert.Equal(expectedDescription, strings["logFilesDescription"]);
    }

    [Theory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public void LocalizedTextCatalog_WhenGamePathStatusKeysRequested_MapsLocalizedValues(string language)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);
        using var strings = new LocalizedTextCatalog(service);

        Assert.Equal(service.T("setupWizardGamePathAvailable"), strings["setupWizardGamePathAvailable"]);
        Assert.Equal(service.T("setupWizardGamePathChecking"), strings["setupWizardGamePathChecking"]);
        Assert.Equal(service.T("setupWizardGamePathCorrupted"), strings["setupWizardGamePathCorrupted"]);
        Assert.Equal(service.T("setupWizardGamePathInaccessible"), strings["setupWizardGamePathInaccessible"]);
        Assert.Equal(service.T("setupWizardGamePathInstalled"), strings["setupWizardGamePathInstalled"]);
    }

    [Fact]
    public void LocalizedTextCatalog_WhenLanguageChanges_NotifiesIndexerBindings()
    {
        var service = new LocalizationService();
        service.SetLanguage(LauncherLanguages.English);
        using var strings = new LocalizedTextCatalog(service);
        var indexerChanged = false;
        strings.PropertyChanged += (_, eventArgs) => indexerChanged |= eventArgs.PropertyName == "Item[]";

        service.SetLanguage(LauncherLanguages.SimplifiedChinese);

        Assert.True(indexerChanged);
        Assert.Equal(service.T("setupWizardGamePathAvailable"), strings["setupWizardGamePathAvailable"]);
        Assert.Equal(service.T("setupWizardGamePathChecking"), strings["setupWizardGamePathChecking"]);
        Assert.Equal(service.T("setupWizardGamePathCorrupted"), strings["setupWizardGamePathCorrupted"]);
        Assert.Equal(service.T("setupWizardGamePathInaccessible"), strings["setupWizardGamePathInaccessible"]);
        Assert.Equal(service.T("setupWizardGamePathInstalled"), strings["setupWizardGamePathInstalled"]);
    }

    [Fact]
    public void T_WhenEnglishDisclaimerRequested_ReturnsQuotesWithoutEscapeCharacters()
    {
        var values = TestLocalizationHelper.ReadResx(
            Path.Combine(TestLocalizationHelper.FindProjectRoot(), "Resources", "LauncherStrings.resx"));

        var disclaimer = values["aboutDisclaimerText"];

        Assert.Contains("\"Cafe Launcher\"", disclaimer, StringComparison.Ordinal);
        Assert.Contains("\"BlueArchive.Cafe\"", disclaimer, StringComparison.Ordinal);
        Assert.Contains("\"Blue Archive\"", disclaimer, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\"", disclaimer, StringComparison.Ordinal);
    }

    [Fact]
    public void LocaleFiles_HaveMatchingKeys()
    {
        var resxDir = Path.Combine(TestLocalizationHelper.FindProjectRoot(), "Resources");
        var english = TestLocalizationHelper.ReadResx(Path.Combine(resxDir, "LauncherStrings.resx"));
        var simplifiedChinese = TestLocalizationHelper.ReadResx(Path.Combine(resxDir, "LauncherStrings.zh-Hans.resx"));
        var traditionalChinese = TestLocalizationHelper.ReadResx(Path.Combine(resxDir, "LauncherStrings.zh-Hant.resx"));
        var japanese = TestLocalizationHelper.ReadResx(Path.Combine(resxDir, "LauncherStrings.ja.resx"));

        Assert.Equal(
            english.Keys.OrderBy(key => key, StringComparer.Ordinal),
            simplifiedChinese.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(
            english.Keys.OrderBy(key => key, StringComparer.Ordinal),
            traditionalChinese.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(
            english.Keys.OrderBy(key => key, StringComparer.Ordinal),
            japanese.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void LocaleFiles_HaveMatchingFormatPlaceholders()
    {
        var resxDir = Path.Combine(TestLocalizationHelper.FindProjectRoot(), "Resources");
        var english = TestLocalizationHelper.ReadResx(Path.Combine(resxDir, "LauncherStrings.resx"));
        var simplifiedChinese = TestLocalizationHelper.ReadResx(Path.Combine(resxDir, "LauncherStrings.zh-Hans.resx"));
        var traditionalChinese = TestLocalizationHelper.ReadResx(Path.Combine(resxDir, "LauncherStrings.zh-Hant.resx"));
        var japanese = TestLocalizationHelper.ReadResx(Path.Combine(resxDir, "LauncherStrings.ja.resx"));

        foreach (var key in english.Keys)
        {
            var expected = GetFormatPlaceholders(english[key]);
            Assert.Equal(expected, GetFormatPlaceholders(simplifiedChinese[key]));
            Assert.Equal(expected, GetFormatPlaceholders(traditionalChinese[key]));
            Assert.Equal(expected, GetFormatPlaceholders(japanese[key]));
        }
    }

    [Theory]
    [InlineData(LauncherLanguages.English, "Remote Manifest", "Download Source", "Resource Panel")]
    [InlineData(LauncherLanguages.SimplifiedChinese, "远程文件清单", "下载源", "资源面板")]
    [InlineData(LauncherLanguages.TraditionalChinese, "遠端檔案清單", "下載來源", "資源面板")]
    [InlineData(LauncherLanguages.Japanese, "リモートマニフェスト", "ダウンロードソース", "リソースパネル")]
    public void T_WhenCanonicalTermsRequested_ReturnsConsistentTerminology(
        string language,
        string expectedManifest,
        string expectedDownloadSource,
        string expectedResourcePanel)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);

        Assert.Equal(expectedManifest, service.T("launchCheckRemoteManifest"));
        Assert.Equal(expectedDownloadSource, service.T("downloadSource"));
        Assert.Equal(expectedResourcePanel, service.T("resourcePanel"));
    }

    [Fact]
    public void LocaleFiles_KeepKeysSortedOrdinal()
    {
        var resxDir = Path.Combine(TestLocalizationHelper.FindProjectRoot(), "Resources");

        foreach (var fileName in new[] { "LauncherStrings.resx", "LauncherStrings.zh-Hans.resx", "LauncherStrings.zh-Hant.resx", "LauncherStrings.ja.resx" })
        {
            var keys = TestLocalizationHelper.ReadResx(Path.Combine(resxDir, fileName)).Keys.ToArray();

            Assert.Equal(keys.OrderBy(key => key, StringComparer.Ordinal), keys);
        }
    }

    [Fact]
    public void T_WhenChineseLocalizationItemsRequested_ReturnsEstablishedTerminology()
    {
        var service = new LocalizationService();
        service.SetLanguage(LauncherLanguages.SimplifiedChinese);
        var values = TestLocalizationHelper.ReadResx(Path.Combine(
            TestLocalizationHelper.FindProjectRoot(), "Resources", "LauncherStrings.zh-Hans.resx"));

        Assert.Equal(values["resourcePanelLocalizedVersion"], service.T("resourcePanelLocalizedVersion"));
        Assert.Equal(values["resourcePanelMainVoice"], service.T("resourcePanelMainVoice"));
        Assert.Equal(values["resourcePanelMedia"], service.T("resourcePanelMedia"));
    }

    [Fact]
    public void T_WhenResourceKeyIsMissing_ReturnsSafeFallbackText()
    {
        var service = new LocalizationService();
        service.SetLanguage(LauncherLanguages.English);

        var value = service.T("nonexistentLocalizationKey");

        Assert.Equal("Localization unavailable.", value);
    }

    [Fact]
    public void T_WhenResourceKeyIsMissing_RaisesApplicationFailureEvent()
    {
        var service = new LocalizationService();
        LocalizationFailureEventArgs? failure = null;
        service.LocalizationFailure += (_, eventArgs) => failure = eventArgs;

        _ = service.T("nonexistentLocalizationKey");

        Assert.NotNull(failure);
        Assert.IsType<MissingManifestResourceException>(failure!.Exception);
    }

    [Fact]
    public void SetLanguage_AutoAfterManual_RestoresStartupCulture()
    {
        var savedCulture = CultureInfo.CurrentCulture;
        var savedUiCulture = CultureInfo.CurrentUICulture;
        var savedDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var savedDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            CultureInfo.CurrentUICulture = new CultureInfo("zh-HK");
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;

            var snapshot = new SystemCultureSnapshot();
            snapshot.Capture();

            var service = new LocalizationService(snapshot, new LocalDiagnostics());

            service.SetLanguage(LauncherLanguages.Japanese);

            service.SetLanguage(LauncherLanguages.Auto);
            Assert.Equal(LauncherLanguages.TraditionalChinese, service.CurrentLanguage);
            Assert.Equal("en-GB", CultureInfo.CurrentCulture.Name);
            Assert.Equal("zh-HK", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("en-GB", CultureInfo.DefaultThreadCurrentCulture!.Name);
            Assert.Equal("zh-HK", CultureInfo.DefaultThreadCurrentUICulture!.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
            CultureInfo.CurrentUICulture = savedUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = savedDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = savedDefaultUiCulture;
        }
    }

    [Theory]
    [InlineData(LauncherLanguages.English, "en-US")]
    [InlineData(LauncherLanguages.SimplifiedChinese, "zh-CN")]
    [InlineData(LauncherLanguages.TraditionalChinese, "zh-TW")]
    [InlineData(LauncherLanguages.Japanese, "ja-JP")]
    public void SetLanguage_ManualSelection_AppliesAllProcessCultureSettings(string language, string cultureName)
    {
        var savedCulture = CultureInfo.CurrentCulture;
        var savedUiCulture = CultureInfo.CurrentUICulture;
        var savedDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var savedDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            var service = new LocalizationService();

            service.SetLanguage(language);

            Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
            Assert.Equal(cultureName, CultureInfo.CurrentUICulture.Name);
            Assert.Equal(cultureName, CultureInfo.DefaultThreadCurrentCulture!.Name);
            Assert.Equal(cultureName, CultureInfo.DefaultThreadCurrentUICulture!.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
            CultureInfo.CurrentUICulture = savedUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = savedDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = savedDefaultUiCulture;
        }
    }

    [Theory]
    [InlineData(LauncherLanguages.English, "3 files need repair (12 MB)", "ETA 1 minute")]
    [InlineData(LauncherLanguages.SimplifiedChinese, "需要修复 3 个文件（12 MB）", "预计剩余时间 1 minute")]
    [InlineData(LauncherLanguages.TraditionalChinese, "需要修復 3 個檔案（12 MB）", "預計剩餘時間 1 minute")]
    [InlineData(LauncherLanguages.Japanese, "3 個のファイルを修復する必要があります（12 MB）", "残り時間 1 minute")]
    public void F_WhenOperationProgressRequested_ReturnsLocalizedText(
        string language,
        string expectedRepairText,
        string expectedEstimatedText)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);

        Assert.Equal(expectedRepairText, service.F("repairFilesNeeded", 3, "12 MB"));
        Assert.Equal(expectedEstimatedText, service.F("estimatedTimeRemaining", "1 minute"));
    }

    private static string[] GetFormatPlaceholders(string value)
    {
        return Regex.Matches(value, @"\{\d+(?:[^}]*)\}")
            .Select(m => m.Value)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }
}
