using System.Text.RegularExpressions;
using Cafe.Launcher.Avalonia.Services.Update;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 安装器脚本与启动器之间靠字面量握手：判断「装上还是便携」的所有权标记，以及
/// 「启动器正开着时先让你关掉」的单实例互斥体名。两边不一致不会有编译错误——只会让
/// 自更新挑错包，或让安装器在启动器运行时静默覆盖文件。
/// </summary>
public sealed class LauncherUpdateHostInfoProviderContractTests
{
    private static readonly Regex AppMutexDefine = new(
        @"^#define\s+APP_MUTEX\s+""(?<mutex>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex MutexNameConstant = new(
        @"MutexName\s*=\s*@?""(?<name>[^""]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void InstallMarker_MatchesTheInstallerScriptLiteral()
    {
        var installerScript = InstallerScript();
        var markerPath = $@"{{app}}\{LauncherUpdateHostInfoProvider.InstallMarkerFileName}";

        // 只断言「脚本里出现过这个文件名」的话，注释里提一句也能满足。这里钉住真正消费它的
        // 位置：安装时创建（files 项），以及卸载校验与存在性判断两处 ExpandConstant。
        Assert.Contains($"Name: \"{markerPath}\"", installerScript, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(installerScript, $"ExpandConstant('{markerPath}')") >= 2,
            $"安装器脚本里 ExpandConstant('{markerPath}') 少于两处：卸载校验与存在性判断缺一，"
            + "装上/便携的判定就会跟着失效。");
    }

    [Fact]
    public void SingleInstanceMutex_MatchesTheInstallerAppMutex()
    {
        var installerScript = InstallerScript();
        var appMutex = FindAppMutex(installerScript);
        var mutexName = FindMutexName(ReadProgramSource());

        Assert.True(
            appMutex is not null,
            "installer/Cafe.Launcher.Avalonia.iss 必须用 #define APP_MUTEX \"…\" 声明单实例互斥体名。");
        Assert.True(
            mutexName is not null,
            "src/Cafe.Launcher.Avalonia/Program.cs 必须用 MutexName 常量声明单实例互斥体名。");
        Assert.True(
            string.Equals(appMutex, mutexName, StringComparison.Ordinal),
            $"安装器的 AppMutex（{appMutex}）与 Program.cs 的 MutexName（{mutexName}）不一致："
            + "不一致时安装器认不出正在运行的启动器，只会撞在占用的文件上。");

        // AppMutex 与 #define 脱钩时，上面比对了半天也拦不住安装器。
        Assert.Contains("AppMutex={#APP_MUTEX}", installerScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// 两个解析器也喂一份手写输入：证明比对真的会红，而不是「解析失败的两个空值恰好相等」。
    /// </summary>
    [Fact]
    public void AppMutexComparison_ReportsAMismatchFedByHand()
    {
        Assert.Equal(
            FindAppMutex("#define APP_MUTEX \"Local\\Cafe_Launcher_SI\""),
            FindMutexName("    private const string MutexName = @\"Local\\Cafe_Launcher_SI\";"));
        Assert.NotEqual(
            FindAppMutex("#define APP_MUTEX \"Local\\Other_SI\""),
            FindMutexName("    private const string MutexName = @\"Local\\Cafe_Launcher_SI\";"));
        Assert.Null(FindAppMutex("; APP_MUTEX 在别处赋值"));
        Assert.Null(FindMutexName("private const string SomethingElse = \"x\";"));
    }

    private static string? FindAppMutex(string installerScript) =>
        AppMutexDefine.Match(installerScript) is { Success: true } match
            ? match.Groups["mutex"].Value
            : null;

    private static string? FindMutexName(string programSource) =>
        MutexNameConstant.Match(programSource) is { Success: true } match
            ? match.Groups["name"].Value
            : null;

    private static string InstallerScript() =>
        File.ReadAllText(Path.Combine(TestRepository.Root, "installer", "Cafe.Launcher.Avalonia.iss"));

    private static string ReadProgramSource() =>
        File.ReadAllText(TestRepository.FromApplicationRoot("Program.cs"));

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
