using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 游戏进程识别规则（ADR-032）。实机确认游戏目录里的进程对象被反作弊保护——可执行文件路径
/// 读不到——所以只能按名字识别；这里钉住名字集合从哪来、以及「同族」的边界在哪。
/// </summary>
public sealed class GameProcessNamesTests
{
    // Blue Archive 实机启动后同时存在的三个进程（名字取自系统快照，路径全部不可读）。
    private static readonly IReadOnlyList<string> BlueArchiveFamily = GameProcessNames.FromLaunchConfiguration(
        "xldr_BlueArchiveOnline_JP_loader_x64",
        ["BlueArchive.exe"]);

    [Fact]
    public void FromLaunchConfiguration_TakesTheHostAndExecutableLaunchParams()
    {
        Assert.Equal(["xldr_BlueArchiveOnline_JP_loader_x64", "BlueArchive"], BlueArchiveFamily);
    }

    [Fact]
    public void FromLaunchConfiguration_IgnoresNonExecutableLaunchParamsAndDuplicates()
    {
        var names = GameProcessNames.FromLaunchConfiguration(
            "host.exe",
            ["game.exe", "-someflag", "--arg=1", "GAME.EXE", ""]);

        Assert.Equal(["host", "game"], names);
    }

    [Fact]
    public void FromLaunchConfiguration_WhenNothingUsable_ReturnsEmpty()
    {
        Assert.Empty(GameProcessNames.FromLaunchConfiguration(null, null));
        Assert.Empty(GameProcessNames.FromLaunchConfiguration("   ", ["-flag"]));
    }

    [Fact]
    public void FromLaunchConfiguration_NormalizesQuotedAndPaddedParameters()
    {
        // params 是游戏自己写的命令行片段，引号与空白都是合法写法。判后缀必须先归一，
        // 否则 "C:\dir\BlueArchive.exe" 会因为结尾那个引号被当成「不是可执行文件」丢掉：
        // 游戏可执行文件静默移出家族，闸门对「游戏正在跑」这件事少一半判据
        // （2026-09-15 复核轮；形态不是实测抓来的，实机配置未随仓库固定）。
        Assert.Equal(
            ["host", "BlueArchive"],
            GameProcessNames.FromLaunchConfiguration(
                "\"host.exe\"",
                ["\"C:\\dir\\BlueArchive.exe\"", "BlueArchive.exe ", " C:\\dir\\BlueArchive.exe "]));

        // 归一不等于放宽：引号里不是可执行文件名就照样丢掉。
        Assert.Empty(GameProcessNames.FromLaunchConfiguration(null, ["\"C:\\dir\"", "\"-flag\""]));
    }

    [Theory]
    [InlineData("xldr_BlueArchiveOnline_JP_loader_x64.exe")]
    [InlineData("xldr_BlueArchiveOnline_JP_loader_x64")]
    [InlineData("xldr_BlueArchiveOnline_JP")]        // 宿主去掉 _loader_x64 后缀的同族子进程
    [InlineData("BlueArchive.exe")]
    [InlineData("BlueArchive")]
    [InlineData("XLDR_BLUEARCHIVEONLINE_JP")]        // 名字比较不区分大小写
    public void BelongsToFamily_WhenProcessIsInTheGameFamily_ReturnsTrue(string processName)
    {
        Assert.True(GameProcessNames.BelongsToFamily(processName, BlueArchiveFamily));
    }

    [Theory]
    [InlineData("BlueArchiveData")]      // 同前缀但分界不是 '_'：不算同族
    [InlineData("BlueArchiveSomething")]
    [InlineData("xldr")]                 // 单段名字太泛，不能凭前缀认领整族
    [InlineData("explorer")]
    [InlineData("Cafe.Launcher.Avalonia")]
    [InlineData("")]
    [InlineData(null)]
    public void BelongsToFamily_WhenProcessIsNotInTheGameFamily_ReturnsFalse(string? processName)
    {
        Assert.False(GameProcessNames.BelongsToFamily(processName, BlueArchiveFamily));
    }

    [Fact]
    public void BelongsToFamily_WhenKnownNamesAreEmpty_ReturnsFalse()
    {
        Assert.False(GameProcessNames.BelongsToFamily("BlueArchive", []));
    }

    [Fact]
    public void DescribeForDisplay_AppendsTheExecutableExtensionAndJoinsWithASeparator()
    {
        // 两道闸门（卸载 / 下载、安装、修复）共用这一处报法：用户看到的名字不因入口而变样。
        Assert.Equal(
            "xldr_BlueArchiveOnline_JP.exe / BlueArchive.exe",
            GameProcessNames.DescribeForDisplay(["xldr_BlueArchiveOnline_JP", "BlueArchive"]));
        Assert.Equal("BlueArchive.exe", GameProcessNames.DescribeForDisplay(["BlueArchive"]));
        Assert.Equal("", GameProcessNames.DescribeForDisplay([]));
    }
}
