using System;
using System.IO;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>
/// 「安装目录里还有东西吗」的判据（2026-09-29 反馈轮）：全新安装开始前要不要提示
/// 「目录中已有内容」。提示只在「启动器不认这份安装」+「盘上确实还有东西」同时成立时出现，
/// 所以「空不空」需要一个说得清的名字，而不是散在调用点的一句枚举。
/// </summary>
/// <remarks>
/// 只取第一条即返回：安装目录可能有上万条目（实测 37k 文件 / 18.5 GB），这里要回答的只是
/// 「空不空」。目录不存在与目录存在却列不出来是两件事——前者没什么可说（全新安装的默认路径
/// 就是这样），后者按「有内容」处理，宁可多提示一句也不谎报目录是空的。
/// </remarks>
internal static class InstallDirectoryContent
{
    /// <summary>目录存在且至少有一个条目时为 true。</summary>
    public static bool HasEntries(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            return entries.MoveNext();
        }
        catch (Exception exception) when (StorageFailure.IsRecoverable(exception))
        {
            return true;
        }
    }
}
