using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

public static class ProcessService
{
    /// <summary>
    /// 当前正在运行、且名字属于 <paramref name="knownNames"/> 这一族的进程名（不含扩展名，去重）。
    /// 空表示没有。名字取自系统快照，因此反作弊保护不了它——被保护的是镜像路径，不是名字。
    /// </summary>
    public static Task<IReadOnlyList<string>> FindRunningExeNamesAsync(
        IReadOnlyList<string> knownNames,
        CancellationToken cancellationToken = default)
    {
        if (knownNames is null || knownNames.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Linux 走 /proc：comm 被内核截断，改以启动器所有权标记为主、家族名为回退（ADR-036）。
        // 其余平台维持按进程快照的名字家族扫描。
        var matches = OperatingSystem.IsLinux()
            ? LinuxProcessScanner.Scan(knownNames, cancellationToken)
            : FindRunningExeNamesBySnapshot(knownNames, cancellationToken);
        return Task.FromResult(matches);
    }

    private static IReadOnlyList<string> FindRunningExeNamesBySnapshot(
        IReadOnlyList<string> knownNames,
        CancellationToken cancellationToken)
    {
        var matches = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // 一次快照走完，而不是每个名字各扫一遍：名字数量随配置增长，扫描次数不该跟着长。
            foreach (var process in Process.GetProcesses())
            {
                // 逐进程看一眼令牌：取消后没有必要把剩余的进程读完。真正的挂死（枚举本身
                // 不返回）令牌拦不住，由闸门的限时赛跑兜住；这里省的是取消后的尾程。
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
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // 读不到进程列表时按「没在跑」处理：这是防误删的闸门，宁可让调用方的其他检查兜底，
            // 也不要因为一次枚举失败就把人永久挡在门外。
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
    /// <para>本文件自己的两处 catch 刻意不用它：枚举进程列表失败只容忍
    /// <see cref="InvalidOperationException"/>/<see cref="Win32Exception"/>（按「没在跑」放行），
    /// 读进程名还额外容忍 <see cref="NotSupportedException"/>——两者容忍的失败面与句柄失效不同。</para>
    /// </remarks>
    internal static bool IsProcessUnavailable(Exception exception) =>
        exception is InvalidOperationException or Win32Exception or ObjectDisposedException;

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
