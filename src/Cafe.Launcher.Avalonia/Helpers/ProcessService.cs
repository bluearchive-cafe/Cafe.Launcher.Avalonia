using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Helpers;

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

        var matches = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // 一次快照走完，而不是每个名字各扫一遍：名字数量随配置增长，扫描次数不该跟着长。
            foreach (var process in Process.GetProcesses())
            {
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

        return Task.FromResult<IReadOnlyList<string>>(matches);
    }

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
