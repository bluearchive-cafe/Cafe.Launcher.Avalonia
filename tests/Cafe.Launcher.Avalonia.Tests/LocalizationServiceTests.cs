using System.Collections.Generic;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LocalizationServiceTests
{
    static LocalizationServiceTests()
    {
        // Keys shared with MigrationWizardViewModelTests — both must be supersets.
        LocalizationService.InitializeForTesting(new Dictionary<string, Dictionary<string, string>>
        {
            [LauncherLanguages.English] = new()
            {
                ["trayOpenLauncher"] = "Open the launcher window",
                ["trayExitLauncher"] = "Close the launcher process",
                ["githubRepository"] = "GitHub Repository",
                ["checkUpdates"] = "Check for Updates",
                ["network"] = "Network",
                ["diskSpaceLabel"] = "Disk Space",
                ["loadingValue"] = "Loading",
                ["executableNameValue"] = "{0}.exe",
                ["proxyDirect"] = "Direct",
                ["proxySystem"] = "System",
                ["closeBehaviorMinimize"] = "Minimize to tray",
                ["closeBehaviorExit"] = "Exit",
                ["migrationWizardTitle"] = "Configuration Migration",
                ["migrationGamePathLabel"] = "Game Path",
                ["migrationSkip"] = "Skip",
                ["migrationApply"] = "Apply",
            },
            [LauncherLanguages.SimplifiedChinese] = new()
            {
                ["trayOpenLauncher"] = "打开启动器窗口",
                ["trayExitLauncher"] = "关闭启动器进程",
                ["githubRepository"] = "GitHub 仓库",
                ["checkUpdates"] = "检查更新",
                ["network"] = "网络",
                ["diskSpaceLabel"] = "磁盘空间",
                ["loadingValue"] = "加载中",
                ["executableNameValue"] = "{0}.exe",
                ["proxyDirect"] = "直接连接",
                ["proxySystem"] = "系统代理",
                ["closeBehaviorMinimize"] = "最小化到托盘",
                ["closeBehaviorExit"] = "退出",
                ["migrationWizardTitle"] = "配置迁移",
                ["migrationGamePathLabel"] = "游戏路径",
                ["migrationSkip"] = "跳过",
                ["migrationApply"] = "应用",
            },
            [LauncherLanguages.Japanese] = new()
            {
                ["trayOpenLauncher"] = "ランチャーウィンドウを開く",
                ["trayExitLauncher"] = "ランチャープロセスを終了",
                ["githubRepository"] = "GitHub リポジトリ",
                ["checkUpdates"] = "更新を確認",
                ["network"] = "ネットワーク",
                ["diskSpaceLabel"] = "ディスク容量",
                ["loadingValue"] = "読み込み中",
                ["executableNameValue"] = "{0}.exe",
                ["proxyDirect"] = "直接接続",
                ["proxySystem"] = "システムプロキシ",
                ["closeBehaviorMinimize"] = "最小化",
                ["closeBehaviorExit"] = "終了",
                ["migrationWizardTitle"] = "設定の移行",
                ["migrationGamePathLabel"] = "ゲームパス",
                ["migrationSkip"] = "スキップ",
                ["migrationApply"] = "適用",
            },
        });
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
    [InlineData(LauncherLanguages.Japanese, "GitHub リポジトリ", "更新を確認")]
    public void T_WhenAboutActionKeysRequested_ReturnsLocalizedText(
        string language,
        string expectedRepositoryText,
        string expectedCheckUpdatesText)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);

        Assert.Equal(expectedRepositoryText, service.T("githubRepository"));
        Assert.Equal(expectedCheckUpdatesText, service.T("checkUpdates"));
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
