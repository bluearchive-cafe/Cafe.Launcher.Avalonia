using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Core.Services.GameRuntime;

public static class ProcessService
{
    /// <summary>
    /// 当前正在运行、且名字属于 <paramref name="knownNames"/> 这一族的进程名（不含扩展名，去重）。
    /// 成功扫描的空结果表示没有；整体枚举失败向调用方传播，由调用方决定是否放行或重试。
    /// 名字取自系统快照，因此反作弊保护不了它——被保护的是镜像路径，不是名字。
    /// </summary>
    public static Task<IReadOnlyList<string>> FindRunningExeNamesAsync(
        IReadOnlyList<string> knownNames,
        CancellationToken cancellationToken = default) =>
        FindRunningGameProcessesAsync(new RunningGameQuery(knownNames), cancellationToken);

    /// <summary>
    /// 同 <see cref="FindRunningExeNamesAsync"/>，但额外接收安装目录：Linux 上用它经 <c>/proc</c>
    /// 的 <c>maps</c> 认出映射着安装目录的进程（覆盖运行时标记不在场的外部启动）。
    /// </summary>
    public static Task<IReadOnlyList<string>> FindRunningGameProcessesAsync(
        RunningGameQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.KnownExeNames is null || query.KnownExeNames.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Linux 走 /proc：comm 被内核截断，改以启动器所有权标记为主、安装目录 maps 与家族名为辅
        // （ADR-036）。其余平台维持按进程快照的名字家族扫描。
        var matches = OperatingSystem.IsLinux()
            ? LinuxProcessScanner.Scan(query, cancellationToken)
            : FindRunningExeNamesBySnapshot(query.KnownExeNames, Process.GetProcesses, cancellationToken);
        return Task.FromResult(matches);
    }

    /// <summary>系统快照扫描；枚举入口可替换，以覆盖 OS 枚举失败而无需影响真实进程。</summary>
    internal static IReadOnlyList<string> FindRunningExeNamesBySnapshot(
        IReadOnlyList<string> knownNames,
        Func<Process[]> enumerateProcesses,
        CancellationToken cancellationToken)
    {
        var matches = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 一次快照走完；整体枚举失败必须与成功扫描但没有匹配区分。
        foreach (var process in enumerateProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (process)
            {
                var name = TryReadProcessName(process);
                if (name.Length > 0
                    && GameProcessNames.BelongsToFamily(name, knownNames)
                    && seen.Add(name))
                {
                    matches.Add(name);
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// 进程句柄已不可用：进程已退出、句柄已释放，或无权访问它。
    /// </summary>
    /// <remarks>
    /// <para>调用方的回退值各不相同（读不到「是否退出」就当作已退出、读不到退出码就报 -1、
    /// 观察与释放失败即忽略），但「哪些异常属于这一类」是同一个判据，不该逐处重写——重写
    /// 会让一次遗漏看起来像一次刻意的收窄。</para>
    /// <para>逐进程读名字还容忍 <see cref="NotSupportedException"/>；整体枚举失败另由
    /// <see cref="IsEnumerationFailure"/> 分类，不能等同于某个进程句柄失效。</para>
    /// </remarks>
    internal static bool IsProcessUnavailable(Exception exception) =>
        exception is InvalidOperationException or Win32Exception or ObjectDisposedException;

    /// <summary>系统进程快照或 proc 根整体不可读；取消与程序错误不属于可放行的扫描失败。</summary>
    internal static bool IsEnumerationFailure(Exception exception) =>
        exception is InvalidOperationException or Win32Exception or System.IO.IOException or UnauthorizedAccessException;

    /// <summary>
    /// 从系统快照读进程名，读不到（枚举与读取之间已退出、或访问被拒）返回空串。反作弊保护的
    /// 是镜像路径而不是名字，所以「游戏运行中」闸门靠的正是这次读取；进程名提取的唯一定义，
    /// <see cref="GameProcessTracker"/> 注册时记名也走这里。
    /// </summary>
    internal static string TryReadProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return "";
        }
    }
}
