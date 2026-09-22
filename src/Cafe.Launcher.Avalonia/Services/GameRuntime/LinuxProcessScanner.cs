using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Linux 上「游戏家族在不在跑」的扫描（P0-B）：枚举 <c>/proc</c>，先按 <c>comm</c>/<c>cmdline</c>
/// 找候选，再读候选的 <c>environ</c> 认启动器写入的所有权标记。标记随 UMU/Proton 传给整族进程
/// （实测，见判据设计稿 §6），因此不依赖内核 15 字符截断的 <c>comm</c>，也能跨「启动器重启」
/// 「宿主退出」认出上一次启动的游戏。
/// </summary>
/// <remarks>
/// <para>读不到 <c>environ</c> 的进程（不同 uid）按「没有标记」处理；枚举失败整体吞掉，与
/// <see cref="ProcessService"/> 的 fail-open 语义一致（宁可放行也不误拦）。</para>
/// <para>前缀 / <c>maps</c> 归属需要调用方提供 prefix 与安装目录上下文，本片未接；标记足够覆盖
/// 计划里 P0-B 的三条完成标准（外部启动、宿主退出、不误报），见 ADR-036。</para>
/// </remarks>
internal static class LinuxProcessScanner
{
    /// <summary>扫一遍 <c>/proc</c>，返回在跑的家族名（去重，优先可读的家族名）。</summary>
    public static IReadOnlyList<string> Scan(IReadOnlyList<string> knownNames, CancellationToken cancellationToken)
    {
        if (knownNames is null || knownNames.Count == 0)
        {
            return [];
        }

        try
        {
            return SelectRunning(ReadRecords(knownNames, cancellationToken), knownNames);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            // 枚举失败按没在跑处理（与 ProcessService 同一取舍）。
            return [];
        }
    }

    /// <summary>纯选择：从记录里挑出属于家族的显示名，合成记录即可测。</summary>
    internal static IReadOnlyList<string> SelectRunning(
        IEnumerable<UnixProcessRecord> records,
        IReadOnlyList<string> knownNames)
    {
        if (knownNames is null || knownNames.Count == 0)
        {
            return [];
        }

        var query = new UnixGameProcessQuery(knownNames, GameId: null, PrefixPath: null, InstallDirectory: null);
        var familyNames = new List<string>();
        var strongNames = new List<string>();
        var seenFamily = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenStrong = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var match = UnixGameProcessMatcher.Match(record, query);
            if (match is null || match.DisplayName.Length == 0)
            {
                continue;
            }

            // 决定「在不在跑」的是 matcher（标记/前缀/maps/comm）；这里只是分类展示名：
            // 名字能对上家族的进程按家族名报，对不上的（运行器辅助进程）只在没有家族名时兜底。
            if (IsFamilyNamed(record, knownNames))
            {
                if (seenFamily.Add(match.DisplayName))
                {
                    familyNames.Add(match.DisplayName);
                }
            }
            else if (seenStrong.Add(match.DisplayName))
            {
                strongNames.Add(match.DisplayName);
            }
        }

        // 有家族名时只报家族名（可读）；否则退回标记命中——可能含运行器辅助进程，
        // 但闸门宁可报得糙也不能漏拦，报法本身由调用方统一（DescribeForDisplay）。
        return familyNames.Count > 0 ? familyNames : strongNames;
    }

    private static bool IsFamilyNamed(UnixProcessRecord record, IReadOnlyList<string> knownNames) =>
        GameProcessNames.BelongsToFamily(record.Comm, knownNames)
        || record.Arguments.Any(argument => GameProcessNames.BelongsToFamily(argument, knownNames));

    private static IEnumerable<UnixProcessRecord> ReadRecords(
        IReadOnlyList<string> knownNames,
        CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!int.TryParse(Path.GetFileName(directory), out var processId))
            {
                continue;
            }

            var comm = UnixProcessRecordParser.ParseComm(ReadBytes(Path.Combine(directory, "comm")));
            var arguments = UnixProcessRecordParser.ParseArguments(ReadBytes(Path.Combine(directory, "cmdline")));
            if (comm.Length == 0 && arguments.Count == 0)
            {
                continue;
            }

            // environ 只对候选读：标记只会出现在游戏家族进程上，而家族进程必然带家族名或 PE 参数。
            var candidate = GameProcessNames.BelongsToFamily(comm, knownNames)
                || arguments.Any(GameProcessNames.LooksLikeExecutable);
            var environment = candidate
                ? UnixProcessRecordParser.ParseEnvironment(ReadBytes(Path.Combine(directory, "environ")))
                : EmptyEnvironment;

            yield return new UnixProcessRecord(
                ProcessId: processId,
                ParentProcessId: 0,
                Comm: comm,
                Arguments: arguments,
                MappedFiles: [],
                Environment: environment);
        }
    }

    private static byte[] ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyEnvironment =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
