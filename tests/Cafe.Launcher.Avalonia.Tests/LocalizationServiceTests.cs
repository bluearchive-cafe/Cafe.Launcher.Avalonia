using System.Collections.Generic;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LocalizationServiceTests
{
    static LocalizationServiceTests()
    {
        LocalizationService.InitializeForTesting(new Dictionary<string, Dictionary<string, string>>
        {
            [LauncherLanguages.English] = new()
            {
                ["trayOpenLauncher"] = "Open the launcher window",
                ["trayExitLauncher"] = "Close the launcher process",
                ["githubRepository"] = "GitHub Repository",
                ["checkUpdates"] = "Check for Updates",
            },
            [LauncherLanguages.SimplifiedChinese] = new()
            {
                ["trayOpenLauncher"] = "打开启动器窗口",
                ["trayExitLauncher"] = "关闭启动器进程",
                ["githubRepository"] = "GitHub 仓库",
                ["checkUpdates"] = "检查更新",
            },
            [LauncherLanguages.Japanese] = new()
            {
                ["trayOpenLauncher"] = "ランチャーウィンドウを開く",
                ["trayExitLauncher"] = "ランチャープロセスを終了",
                ["githubRepository"] = "GitHub リポジトリ",
                ["checkUpdates"] = "更新を確認",
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
}
