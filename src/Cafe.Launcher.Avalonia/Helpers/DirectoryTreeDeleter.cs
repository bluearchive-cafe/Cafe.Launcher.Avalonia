using System;
using System.Collections.Generic;
using System.IO;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 目录树的整体删除（ADR-030）。递归删除是卸载「彻底清除」才有的能力，因此
/// 路径守卫与删除动作放在一处：目标必须落在调用方声明的根之内，且不得是盘根或
/// reparse point。
/// </summary>
/// <remarks>
/// <para>与 <see cref="GamePathValidator"/> 的分工：后者面向「游戏目录内的单个文件」，
/// 本类面向「一棵目录树」。</para>
/// <para>这里刻意自己遍历而不是用 <c>Directory.Delete(path, recursive: true)</c>：实测
/// Windows 上树内只要有一个 junction/符号链接，后者就抛 <see cref="UnauthorizedAccessException"/>
/// （<c>RemoveDirectoryRecursive</c>），整棵删除会失败在一次可预期的布局上。显式遍历把链接
/// 当作链接本身删掉（删的是目录项，不跟随进目标），其余照常；这也让测量口径与删除口径一致
/// （<see cref="DirectorySizeProbe"/> 同样跳过 reparse point）。由
/// <c>DirectoryTreeDeleterTests</c> 钉住该语义。</para>
/// </remarks>
public static class DirectoryTreeDeleter
{
    /// <summary>
    /// <paramref name="path"/> 是否就是 <paramref name="allowedRoot"/>，或位于其下。
    /// </summary>
    public static bool IsUnder(string path, string allowedRoot)
    {
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        return string.Equals(target, root, GamePathValidator.PathComparison)
            || target.StartsWith(root + Path.DirectorySeparatorChar, GamePathValidator.PathComparison);
    }

    /// <summary>
    /// 只跑守卫不删东西：目标必须落在 <paramref name="allowedRoot"/> 之内、不是盘根、不是
    /// reparse point（不存在时后两条无从判定，视为通过）。调用方可以先用它做预检，
    /// 让「拒绝」发生在删任何东西之前。
    /// </summary>
    /// <exception cref="InvalidOperationException">任一守卫不通过。</exception>
    public static void EnsureDeletable(string path, string allowedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedRoot);

        if (!IsUnder(path, allowedRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to delete a directory outside {allowedRoot}: {path}");
        }

        var fullPath = Path.GetFullPath(path);
        var volumeRoot = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(volumeRoot)
            && string.Equals(
                Path.TrimEndingDirectorySeparator(fullPath),
                Path.TrimEndingDirectorySeparator(volumeRoot),
                GamePathValidator.PathComparison))
        {
            throw new InvalidOperationException(
                $"Refusing to delete a drive root as a directory tree: {path}");
        }

        if (Directory.Exists(fullPath)
            && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Refusing to delete a reparse point as a directory tree: {path}");
        }
    }

    /// <summary>
    /// 递归删除 <paramref name="path"/>；目标不存在时视为已完成。
    /// </summary>
    /// <exception cref="InvalidOperationException">见 <see cref="EnsureDeletable"/>。</exception>
    /// <exception cref="IOException">删除过程中被占用、无权限等——由调用方定性。</exception>
    public static void Delete(string path, string allowedRoot)
    {
        EnsureDeletable(path, allowedRoot);

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            return;
        }

        DeleteLevelByLevel(fullPath);
    }

    /// <summary>
    /// 自顶向下收集、自底向上删除：每个目录被收集时它的子项已开始处理，因此逆序删除时
    /// 目录必为空。链接在被遇到时就按链接删掉，父目录随即可能变空。
    /// </summary>
    private static void DeleteLevelByLevel(string root)
    {
        var pending = new Stack<string>();
        var directories = new List<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            directories.Add(current);

            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    // 删目录项本身，不跟随进目标。
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        Directory.Delete(entry, recursive: false);
                    }
                    else
                    {
                        File.Delete(entry);
                    }

                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                // 只读文件删不掉：与 Directory.Delete(recursive: true) 一样先摘掉该属性。
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                }

                File.Delete(entry);
            }
        }

        for (var index = directories.Count - 1; index >= 0; index--)
        {
            Directory.Delete(directories[index], recursive: false);
        }
    }
}
