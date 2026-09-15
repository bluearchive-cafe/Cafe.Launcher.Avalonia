using System;
using System.Collections.Generic;
using System.IO;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 目录树的占用大小（ADR-030）：只读元数据，不读文件内容。
/// </summary>
/// <remarks>
/// 逐条容错：一个取不到属性的条目（权限、临时锁）不应该让「将删除多少」这个展示数字
/// 变成异常。reparse point 既不计入也不再向内遍历——与删除口径一致（删除不会跟随
/// reparse point），顺带避免自引用链接把遍历变成死循环。根自身是 reparse point 时同样
/// 按 0 报（2026-09-15 复核轮）：删除路径的守卫会拒绝这种根，遍历却会跟着链接走进目标盘。
/// </remarks>
public static class DirectorySizeProbe
{
    /// <summary>目录树内文件的字节合计；路径为空、不存在或不可读时返回 0。</summary>
    public static long Measure(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return 0;
        }

        if (IsReparsePoint(path))
        {
            return 0;
        }

        long total = 0;
        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pending.Push(entry);
                        }
                        else
                        {
                            total += new FileInfo(entry).Length;
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // 单个条目读不到就跳过：展示数字允许偏小，不允许抛。
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return total;
    }

    /// <summary>读不到属性时按「不是链接」处理：展示数字允许偏小，不允许抛。</summary>
    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
