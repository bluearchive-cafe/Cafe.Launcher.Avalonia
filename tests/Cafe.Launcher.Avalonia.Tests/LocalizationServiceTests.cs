using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LocalizationServiceTests
{
    static LocalizationServiceTests()
    {
        TestLocalizationHelper.Initialize();
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
        var strings = new LocalizedStrings();
        strings.Apply(service);
        Assert.Equal(service.T("diskSpaceCheck"), strings.DiskSpaceCheck);
        Assert.Equal(service.T("verificationFailed"), strings.VerificationFailed);
    }

    [Fact]
    public void T_WhenLegalInfoRequested_ReturnsChineseCopy()
    {
        var json = File.ReadAllText(Path.Combine(FindProjectRoot(), "Assets", "Locales", "zh-Hans.json"));
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        Assert.NotNull(values);

        var copyright = values["aboutCopyrightText"];
        var disclaimer = values["aboutDisclaimerText"];

        Assert.Contains("版权所有", copyright);
        Assert.DoesNotContain("All rights reserved", copyright);
        Assert.Contains("“Cafe Launcher”", disclaimer);
        Assert.Contains("中文名“蔚蓝档案”", disclaimer);
        Assert.DoesNotContain("中文名'蔚蓝档案'", disclaimer);
    }

    [Fact]
    public void T_WhenTraditionalChineseLegalInfoRequested_ReturnsTraditionalCopy()
    {
        var json = File.ReadAllText(Path.Combine(FindProjectRoot(), "Assets", "Locales", "zh-Hant.json"));
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        Assert.NotNull(values);

        var copyright = values["aboutCopyrightText"];
        var disclaimer = values["aboutDisclaimerText"];

        Assert.Contains("版權所有", copyright);
        Assert.Contains("「Cafe Launcher」", disclaimer);
        Assert.Contains("中文名「蔚藍檔案」", disclaimer);
        // Make sure Traditional strings are not accidentally Simplified copies.
        Assert.DoesNotContain("版权所有", copyright);
        Assert.DoesNotContain("“Cafe Launcher”", disclaimer);
    }

    [Fact]
    public void T_WhenEnglishDisclaimerRequested_ReturnsQuotesWithoutEscapeCharacters()
    {
        var json = File.ReadAllText(Path.Combine(FindProjectRoot(), "Assets", "Locales", "en.json"));
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        Assert.NotNull(values);

        var disclaimer = values["aboutDisclaimerText"];

        Assert.Contains("\"Cafe Launcher\"", disclaimer, StringComparison.Ordinal);
        Assert.Contains("\"BlueArchive.Cafe\"", disclaimer, StringComparison.Ordinal);
        Assert.Contains("\"Blue Archive\"", disclaimer, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\"", disclaimer, StringComparison.Ordinal);
    }

    [Fact]
    public void LocaleFiles_HaveMatchingKeys()
    {
        var localeDirectory = Path.Combine(FindProjectRoot(), "Assets", "Locales");
        var english = ReadLocale(localeDirectory, "en.json");
        var simplifiedChinese = ReadLocale(localeDirectory, "zh-Hans.json");
        var traditionalChinese = ReadLocale(localeDirectory, "zh-Hant.json");
        var japanese = ReadLocale(localeDirectory, "ja.json");

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
        var localeDirectory = Path.Combine(FindProjectRoot(), "Assets", "Locales");
        var english = ReadLocale(localeDirectory, "en.json");
        var simplifiedChinese = ReadLocale(localeDirectory, "zh-Hans.json");
        var traditionalChinese = ReadLocale(localeDirectory, "zh-Hant.json");
        var japanese = ReadLocale(localeDirectory, "ja.json");

        foreach (var key in english.Keys)
        {
            var expected = GetFormatPlaceholders(english[key]);
            Assert.Equal(expected, GetFormatPlaceholders(simplifiedChinese[key]));
            Assert.Equal(expected, GetFormatPlaceholders(traditionalChinese[key]));
            Assert.Equal(expected, GetFormatPlaceholders(japanese[key]));
        }
    }

    [Theory]
    [InlineData(LauncherLanguages.English, "Remote Manifest", "Download Source", "Chinese Localization Settings")]
    [InlineData(LauncherLanguages.SimplifiedChinese, "远程文件清单", "下载源", "汉化管理")]
    [InlineData(LauncherLanguages.TraditionalChinese, "遠端檔案清單", "下載來源", "中文化管理")]
    [InlineData(LauncherLanguages.Japanese, "リモートマニフェスト", "ダウンロードソース", "中国語化設定")]
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
    public void T_WhenChineseLocalizationItemsRequested_ReturnsEstablishedTerminology()
    {
        var service = new LocalizationService();
        service.SetLanguage(LauncherLanguages.SimplifiedChinese);

        Assert.Equal("汉化", service.T("resourcePanelLocalizedVersion"));
        Assert.Equal("主线中配", service.T("resourcePanelMainVoice"));
        Assert.Equal("图像视频", service.T("resourcePanelMedia"));
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

    private static Dictionary<string, string> ReadLocale(string localeDirectory, string fileName)
    {
        var json = File.ReadAllText(Path.Combine(localeDirectory, fileName));
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? throw new InvalidDataException($"{fileName} is not a localization dictionary.");
    }

    private static string[] GetFormatPlaceholders(string value)
    {
        return Regex.Matches(value, @"\{\d+(?:[^}]*)\}")
            .Select(match => match.Value)
            .OrderBy(placeholder => placeholder, StringComparer.Ordinal)
            .ToArray();
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
