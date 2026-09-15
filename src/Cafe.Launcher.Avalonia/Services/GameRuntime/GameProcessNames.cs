using System;
using System.Collections.Generic;
using System.IO;

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

    /// <summary>去掉路径与 <c>.exe</c> 扩展名，留下可直接比较的进程名。</summary>
    internal static string WithoutExtension(string? exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName))
        {
            return "";
        }

        var name = Path.GetFileName(exeName.Trim());
        return name.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^ExecutableExtension.Length]
            : name;
    }

    private const string ExecutableExtension = ".exe";

    private static void AddName(List<string> names, HashSet<string> seen, string? candidate)
    {
        var name = WithoutExtension(candidate);
        if (name.Length > 0 && seen.Add(name))
        {
            names.Add(name);
        }
    }

    private static bool LooksLikeExecutable(string? parameter) =>
        !string.IsNullOrWhiteSpace(parameter)
        && parameter.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase);
}
