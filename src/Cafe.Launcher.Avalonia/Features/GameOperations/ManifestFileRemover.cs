using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

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
    public static void DeleteAll(
        string gamePath,
        IReadOnlyList<ManifestFile> files,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteFileIfPresent(GamePathValidator.GetSafeFilePath(gamePath, files[i].Path));
            progress?.Invoke(Percent(i + 1, files.Count));
        }
    }

    /// <summary>
    /// 删除存在的文件，删除前先清掉只读属性。文件不在盘上是空操作。
    /// </summary>
    public static void DeleteFileIfPresent(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return;
        }

        if ((info.Attributes & FileAttributes.ReadOnly) != 0)
        {
            info.Attributes &= ~FileAttributes.ReadOnly;
        }

        info.Delete();
    }

    /// <summary>逐文件百分比。存量调用都不以空清单进循环，这里的守卫只防后来者。</summary>
    private static int Percent(int completed, int total) =>
        total > 0 ? (int)Math.Round(completed * 100d / total) : 100;
}
