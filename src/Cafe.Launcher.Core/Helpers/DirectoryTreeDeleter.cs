using System;
using System.Collections.Generic;
using System.IO;

namespace Cafe.Launcher.Core.Helpers;

/// <summary>
/// 目录树的整体删除（ADR-046）。路径守卫、扫描计划与删除动作放在一处：目标必须
/// 落在调用方声明的根之内，且不得是盘根或 reparse point。
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
    /// <returns>
    /// 删不掉的项目（目录项，或因此留下来的目录本身）；为空表示整棵树已经删干净。
    /// 调用方要据此如实上报：安装目录能被删到什么程度不由调用方决定。
    /// </returns>
    /// <exception cref="InvalidOperationException">见 <see cref="EnsureDeletable"/>。</exception>
    public static IReadOnlyList<string> Delete(string path, string allowedRoot) =>
        CreatePlan([new DirectoryDeletionTarget(path, allowedRoot)]).Delete().Leftovers;

    /// <summary>
    /// 扫描所有目标并生成一个删除计划；扫描不删除任何项目、不跟随链接、按子目录先于父目录
    /// 排列。调用方可在扫描结束后重新检查游戏运行状态，再执行计划。
    /// </summary>
    public static DirectoryDeletionPlan CreatePlan(
        IReadOnlyList<DirectoryDeletionTarget> targets,
        System.Threading.CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var normalizedTargets = new List<DirectoryDeletionTarget>();
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDeletable(target.Path, target.AllowedRoot);
            normalizedTargets.Add(new DirectoryDeletionTarget(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Path)),
                Path.GetFullPath(target.AllowedRoot)));
        }

        var pending = new Stack<DirectoryDeletionEntry>();
        var directories = new List<DirectoryDeletionEntry>();
        var entries = new List<DirectoryDeletionEntry>();
        var seen = new HashSet<string>(GamePathValidator.PathComparer);
        foreach (var target in normalizedTargets)
        {
            if (Directory.Exists(target.Path))
            {
                pending.Push(new DirectoryDeletionEntry(target.Path, target.Path));
            }
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            if (!seen.Add(current.Path))
            {
                continue;
            }

            try
            {
                var attributes = File.GetAttributes(current.Path);
                if ((attributes & FileAttributes.ReparsePoint) != 0
                    || (attributes & FileAttributes.Directory) == 0)
                {
                    if (string.Equals(current.Path, current.Root, GamePathValidator.PathComparison))
                    {
                        EnsureDeletable(current.Path, current.Root);
                    }

                    entries.Add(current);
                    continue;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                entries.Add(current);
                continue;
            }

            directories.Add(current);
            foreach (var path in EnumerateEntries(current.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = new DirectoryDeletionEntry(path, current.Root);
                try
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.Directory) != 0
                        && (attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(entry);
                        continue;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // 扫描不了的项目仍纳入计划，执行时重试；失败时按路径交还，不能悄悄漏掉。
                }

                if (seen.Add(path))
                {
                    entries.Add(entry);
                }
            }
        }

        for (var index = directories.Count - 1; index >= 0; index--)
        {
            entries.Add(directories[index]);
        }

        return new DirectoryDeletionPlan(normalizedTargets, entries);
    }

    private static string[] EnumerateEntries(string directory)
    {
        try
        {
            return Directory.GetFileSystemEntries(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 目录本身仍在计划中；无法枚举时不会递归删除，非空或无权限会成为可见残留。
            return [];
        }
    }
}
