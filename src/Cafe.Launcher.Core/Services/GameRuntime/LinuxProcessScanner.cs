using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace Cafe.Launcher.Core.Services.GameRuntime;

/// <summary>
/// Linux 上「游戏家族在不在跑」的扫描（P0-B）：枚举 <c>/proc</c>，读取可访问进程的
/// <c>environ</c> 认启动器写入的所有权标记，不按可执行文件后缀预过滤。标记随 UMU/Proton 传给整族进程
/// （实测，见判据设计稿 §6），因此不依赖内核 15 字符截断的 <c>comm</c>，也能跨「启动器重启」
/// 「宿主退出」认出上一次启动的游戏。
/// </summary>
/// <remarks>
/// <para>读不到 <c>environ</c> 的进程（不同 uid）按「没有标记」处理；proc 根整体枚举失败
/// 向上传播，让会话监控与破坏性操作闸门分别决定如何处理未知状态。</para>
/// <para>前缀 / <c>maps</c> 归属需要调用方提供 prefix 与安装目录上下文，本片未接；标记足够覆盖
/// 计划里 P0-B 的三条完成标准（外部启动、宿主退出、不误报），见 ADR-036。</para>
/// </remarks>
public static class LinuxProcessScanner
{
    /// <summary>扫一遍 <c>/proc</c>，返回在跑的家族名（去重，优先可读的家族名）。</summary>
    public static IReadOnlyList<string> Scan(RunningGameQuery query, CancellationToken cancellationToken) =>
        Scan(query, "/proc", cancellationToken);

    /// <summary>以指定 proc 根扫描，测试用真实目录模拟枚举失败与逐进程消失。</summary>
    internal static IReadOnlyList<string> Scan(RunningGameQuery query, string procRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.KnownExeNames is null || query.KnownExeNames.Count == 0)
        {
            return [];
        }

        return SelectRunning(ReadRecords(query, procRoot, cancellationToken), query);
    }

    /// <summary>纯选择：从记录里挑出属于家族的显示名，合成记录即可测。</summary>
    internal static IReadOnlyList<string> SelectRunning(
        IEnumerable<UnixProcessRecord> records,
        RunningGameQuery query)
    {
        if (query.KnownExeNames is null || query.KnownExeNames.Count == 0)
        {
            return [];
        }

        var knownNames = query.KnownExeNames;
        var unixQuery = new UnixGameProcessQuery(
            knownNames,
            GameId: null,
            PrefixPath: null,
            InstallDirectory: query.InstallDirectory);
        var familyNames = new List<string>();
        var strongNames = new List<string>();
        var seenFamily = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenStrong = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var match = UnixGameProcessMatcher.Match(record, unixQuery);
            if (match is null || match.DisplayName.Length == 0)
            {
                continue;
            }

            // 决定「在不在跑」的是 matcher（标记/前缀/maps/comm）；这里只是分类展示名：
            // 名字能对上家族的进程按家族名报，对不上的（运行器辅助进程）只在没有家族名时兜底。
            if (UnixGameProcessMatcher.GetFamilyName(record, knownNames) is not null)
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

    /// <summary>
    /// 读「正在跑的游戏实际用了哪个 Proton 构建」：扫带 PE 参数的进程，认启动器标记，取环境里的
    /// <c>PROTONPATH</c>。UMU 不往 stdout/stderr 写东西（实测），这是唯一能拿到实际构建的地方。
    /// 只诊断用；读不到返回 null。
    /// </summary>
    public static string? TryReadRunningProtonBuild()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), out _))
                {
                    continue;
                }

                var arguments = UnixProcessRecordParser.ParseArguments(ReadBytes(Path.Combine(directory, "cmdline")));
                if (!arguments.Any(GameProcessNames.LooksLikeExecutable))
                {
                    continue;
                }

                var environment = UnixProcessRecordParser.ParseEnvironment(ReadBytes(Path.Combine(directory, "environ")));
                var build = SelectRunningProtonBuild(
                    [new UnixProcessRecord(0, 0, "", arguments, [], environment)]);
                if (build is not null)
                {
                    return build;
                }
            }
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            // 诊断增强：读不到就没有。
        }

        return null;
    }

    /// <summary>纯选择：带启动器标记且声明了 <c>PROTONPATH</c> 的记录即给出实际构建。</summary>
    internal static string? SelectRunningProtonBuild(IEnumerable<UnixProcessRecord> records)
    {
        foreach (var record in records)
        {
            if (!record.Environment.ContainsKey(UnixGameProcessMatcher.OwnershipMarkerKey))
            {
                continue;
            }

            if (record.Environment.TryGetValue("PROTONPATH", out var path) && path.Length > 0)
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<UnixProcessRecord> ReadRecords(
        RunningGameQuery query,
        string procRoot,
        CancellationToken cancellationToken)
    {
        var readMaps = !string.IsNullOrWhiteSpace(query.InstallDirectory);
        foreach (var directory in Directory.EnumerateDirectories(procRoot))
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

            // XignCode 的 .xem 和无 PE 参数的 Wine 辅助进程也可能占用游戏目录，不能按名字预过滤。
            var environment = UnixProcessRecordParser.ParseEnvironment(ReadBytes(Path.Combine(directory, "environ")));
            IReadOnlyList<string> mappedFiles = readMaps
                ? UnixProcessRecordParser.ParseMappedFiles(ReadBytes(Path.Combine(directory, "maps")))
                : [];

            yield return new UnixProcessRecord(
                ProcessId: processId,
                ParentProcessId: 0,
                Comm: comm,
                Arguments: arguments,
                MappedFiles: mappedFiles,
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

}
