using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>
/// 删除清单列出的文件，逐文件回调百分比。由两处共用：更新/安装要清掉的旧文件，以及卸载。
/// </summary>
/// <remarks>
/// <para>三条要点都是被真实事故换来的，因此只该有一处定义：</para>
/// <list type="number">
/// <item><description>目标路径经 <see cref="GamePathValidator.GetSafeFilePath"/> 解析。它比
/// <c>GetSafePath</c> 多拒一类：归一到游戏根目录自身的条目（空路径、<c>"."</c>、<c>"sub/.."</c>），
/// 否则 <c>File.Delete</c> 会落在游戏根目录上。</description></item>
/// <item><description>删除前清掉只读属性。手工拷贝过或被打过更新包标记的文件带只读，
/// <c>File.Delete</c> 会抛 <see cref="UnauthorizedAccessException"/>——这正是它此前让整次
/// 安装/更新（以及卸载）直接中止的原因。</description></item>
/// <item><description>已经不在盘上的条目不算错误。</description></item>
/// </list>
/// <para>此前这套语义在下载执行器里齐备，而卸载侧是另一份裸 <c>File.Delete</c> 循环：清单里
/// 只要有一个只读文件，整次卸载就失败。</para>
/// </remarks>
internal static class ManifestFileRemover
{
    /// <summary>删除 <paramref name="files"/> 中每个文件，并逐文件回调进度百分比。</summary>
    /// <param name="progress">按清单顺序回调；空清单不会回调（循环体不执行）。</param>
    /// <returns>
    /// 实测结果（2026-09-29 反馈轮）：调用方要能说出「计划删多少」与「实际删了多少」的区别，
    /// 否则「清单里 157 个文件」会被当成「删掉了 157 个文件」去汇报。
    /// </returns>
    public static ManifestRemovalResult DeleteAll(
        string gamePath,
        IReadOnlyList<ManifestFile> files,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        var removedCount = 0;
        var missingCount = 0;
        var removedBytes = 0L;

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var removedSize = DeleteFileIfPresent(GamePathValidator.GetSafeFilePath(gamePath, files[i].Path));
            if (removedSize is null)
            {
                missingCount++;
            }
            else
            {
                removedCount++;
                removedBytes += removedSize.Value;
            }

            progress?.Invoke(StageProgressReporter.Percent(i + 1, files.Count));
        }

        return new ManifestRemovalResult(removedCount, removedBytes, missingCount);
    }

    /// <summary>
    /// 删除存在的文件，删除前先清掉只读属性。文件不在盘上是空操作。
    /// </summary>
    /// <returns>被删除文件的字节数；文件本来就不在盘上时为 <see langword="null"/>。</returns>
    public static long? DeleteFileIfPresent(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return null;
        }

        var length = info.Length;
        if ((info.Attributes & FileAttributes.ReadOnly) != 0)
        {
            info.Attributes &= ~FileAttributes.ReadOnly;
        }

        info.Delete();
        return length;
    }

}

/// <summary>
/// 一次清单删除的实测结果：真正删掉的文件数与字节数，以及本来就不在盘上的条目数
/// （清单过期、用户手工删过、上一次卸载删过都会让后者非零）。
/// </summary>
/// <param name="RemovedCount">实际删除的文件数。</param>
/// <param name="RemovedBytes">实际删除的字节数。</param>
/// <param name="MissingCount">清单列出、但盘上本来就没有的条目数。</param>
internal readonly record struct ManifestRemovalResult(int RemovedCount, long RemovedBytes, int MissingCount);
