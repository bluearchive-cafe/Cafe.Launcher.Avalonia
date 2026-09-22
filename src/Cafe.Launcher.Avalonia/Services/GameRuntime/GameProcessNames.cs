using System;
using System.Collections.Generic;
using System.Linq;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// 「这个游戏自己的进程」叫什么（ADR-032）。游戏目录里的进程对象被反作弊保护：实机确认三个
/// 进程的可执行文件路径全部读不到（`Win32_Process.ExecutablePath` 为 null），所以「按镜像路径
/// 归属游戏目录」这条路走不通，只能按名字识别——名字来自系统快照，保护不到。
/// </summary>
/// <remarks>
/// 已知名字来自游戏自己带来的 <c>game-launcher-config.json</c>：宿主名（<c>name</c>）与启动参数里的
/// 可执行文件（<c>params</c>）。宿主还会派生一个不在配置里的同族子进程（反作弊宿主），它的名字是
/// 宿主名去掉 <c>_loader_x64</c> 后缀。Blue Archive 实测到的三个进程正好覆盖这两条规则：
/// <c>xldr_BlueArchiveOnline_JP_loader_x64</c>（配置宿主）、<c>xldr_BlueArchiveOnline_JP</c>（同族）、
/// <c>BlueArchive</c>（配置参数）。只认配置宿主一个名字时，前两者之外的那个会被漏掉——而它恰恰是
/// 强杀游戏后仍然占着安装目录的那个。
/// </remarks>
public static class GameProcessNames
{
    /// <summary>
    /// 从启动配置里取已知的游戏可执行文件名（不含扩展名，去重，保留配置顺序）。
    /// </summary>
    public static IReadOnlyList<string> FromLaunchConfiguration(
        string? hostExeName,
        IEnumerable<string>? launchParams)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddName(names, seen, hostExeName);

        if (launchParams is not null)
        {
            foreach (var parameter in launchParams)
            {
                if (LooksLikeExecutable(parameter))
                {
                    AddName(names, seen, parameter);
                }
            }
        }

        return names;
    }

    /// <summary>
    /// 进程名是否属于这组已知名字：相等，或与某个已知名「同名家族」——即以 <c>_</c> 为界互为
    /// 前缀/后缀变体（见类型注释）。<c>_</c> 分界让「同名家族」而不是「恰好同前缀」成立：
    /// <c>BlueArchive</c> 因此不会匹配上 <c>BlueArchiveData</c>。
    /// </summary>
    public static bool BelongsToFamily(string? processName, IReadOnlyList<string> knownNames)
    {
        var name = WithoutExtension(processName);
        if (name.Length == 0 || knownNames is null)
        {
            return false;
        }

        foreach (var known in knownNames)
        {
            if (string.Equals(name, known, StringComparison.OrdinalIgnoreCase)
                || ExtendsKnownName(name, known)
                || ExtendsProcessName(name, known))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>进程名以 <c>_</c> 为界在已知名之后续写（已知名的变体）。已知名可信，无额外门槛。</summary>
    private static bool ExtendsKnownName(string processName, string knownName) =>
        processName.Length > knownName.Length
        && processName.StartsWith(knownName, StringComparison.OrdinalIgnoreCase)
        && processName[knownName.Length] == '_';

    /// <summary>
    /// 已知名以 <c>_</c> 为界在进程名之后续写：宿主派生的同族进程，名字比宿主短
    /// （Blue Archive 的反作弊宿主就是这个形状）。要求进程名至少两段——单段名字太泛，
    /// 不该凭前缀把整族认领过去（<c>xldr</c> 不该匹配 <c>xldr_BlueArchiveOnline_JP_loader_x64</c>）。
    /// </summary>
    private static bool ExtendsProcessName(string processName, string knownName) =>
        processName.Length < knownName.Length
        && knownName.StartsWith(processName, StringComparison.OrdinalIgnoreCase)
        && knownName[processName.Length] == '_'
        && processName.Contains('_', StringComparison.Ordinal);

    /// <summary>
    /// 一组正在运行的游戏进程名在界面上怎么写：统一为恰好一个 <c>.exe</c> 扩展名（与启动器
    /// 别处称呼可执行文件一致），多条用语言中立的分隔符连接。卸载与下载/安装/修复两道闸门
    /// 共用它，用户看到的名字因此不会因入口不同而变样（ADR-032）。
    /// </summary>
    public static string DescribeForDisplay(IReadOnlyList<string> processNames) =>
        string.Join(" / ", processNames.Select(name => $"{WithoutExtension(name)}{ExecutableExtension}"));

    /// <summary>去掉路径与 <c>.exe</c> 扩展名，留下可直接比较的进程名。</summary>
    /// <remarks>
    /// 路径切分不按宿主平台的分隔符约定（不用 <c>Path.GetFileName</c>）：<c>params</c> 来自游戏
    /// 自带的启动配置，在任何平台上都是 Windows 形状（<c>C:\dir\BlueArchive.exe</c>），Linux 与 macOS
    /// 下游戏跑在兼容层里也一样。让宿主约定参与进来时，Unix 上 <c>\</c> 不是分隔符，整个路径会被当成
    /// 文件名，游戏可执行文件静默移出家族——判据少一半，正是 ADR-032 要避免的那件事。
    /// </remarks>
    internal static string WithoutExtension(string? exeName)
    {
        var normalized = Unquoted(exeName);
        if (normalized.Length == 0)
        {
            return "";
        }

        var name = FileNameOf(normalized);
        return name.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^ExecutableExtension.Length]
            : name;
    }

    /// <summary>最后一个路径分隔符之后的部分，<c>\</c> 与 <c>/</c> 一视同仁。</summary>
    private static string FileNameOf(string path)
    {
        var cut = path.LastIndexOfAny(PathSeparators);
        return cut >= 0 ? path[(cut + 1)..] : path;
    }

    private const string ExecutableExtension = ".exe";

    private static readonly char[] PathSeparators = ['\\', '/'];

    private static void AddName(List<string> names, HashSet<string> seen, string? candidate)
    {
        var name = WithoutExtension(candidate);
        if (name.Length > 0 && seen.Add(name))
        {
            names.Add(name);
        }
    }

    /// <summary>
    /// 启动参数里的一个词是不是「可执行文件名」。判后缀之前先去掉成对引号与首尾空白——<c>params</c>
    /// 是游戏自己写的命令行片段，带引号的路径是合法写法，而 <c>"C:\dir\BlueArchive.exe"</c> 直接判
    /// 后缀会因结尾那个引号被当成「不是可执行文件」丢掉：游戏可执行文件静默移出家族，判据少一半。
    /// 判据与提取必须走同一份归一（<see cref="WithoutExtension"/>），否则两者会各说各话。
    /// </summary>
    private static bool LooksLikeExecutable(string? parameter) =>
        Unquoted(parameter).EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>去掉首尾空白与成对的首尾引号（<c>"x.exe"</c> 与 <c> x.exe </c> 都归一成 <c>x.exe</c>）。</summary>
    private static string Unquoted(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1].Trim()
            : trimmed;
    }
}
