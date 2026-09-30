using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Cafe.Launcher.Core.Helpers;

/// <summary>一棵待删除的目录树，以及调用方允许它所属的根。</summary>
public readonly record struct DirectoryDeletionTarget(string Path, string AllowedRoot);

/// <summary>删除已处理的条目数（包括无法删除的条目），以及计划的条目总数。</summary>
public readonly record struct DirectoryDeletionProgress(int ProcessedEntries, int TotalEntries);

/// <summary>整棵删除的实际文件数、字节数与无法删除的路径；目录和链接不计入文件字节数。</summary>
public sealed record DirectoryDeletionResult(int RemovedFiles, long RemovedBytes, IReadOnlyList<string> Leftovers);

/// <summary>
/// 删除前扫描出的条目快照。文件与链接在前、目录按子先父后排列；删除时重新读取属性，
/// 并检查条目的祖先没有变成链接，因此扫描到的树不能被链接替换后指向树外。
/// </summary>
public sealed class DirectoryDeletionPlan
{
    private readonly IReadOnlyList<DirectoryDeletionTarget> targets;
    private readonly IReadOnlyList<DirectoryDeletionEntry> entries;

    internal DirectoryDeletionPlan(
        IReadOnlyList<DirectoryDeletionTarget> targets,
        IReadOnlyList<DirectoryDeletionEntry> entries)
    {
        this.targets = targets;
        this.entries = entries;
    }

    /// <summary>扫描到的文件、链接与目录总数；进度按处理条目计数，而非按清单或字节计数。</summary>
    public int TotalEntries => entries.Count;

    /// <summary>
    /// 再次检查所有目标后删除快照中的条目。单项 I/O 失败继续处理其他条目，取消则停止；
    /// 100% 表示所有计划条目已处理，残留是否为空由结果单独表达。
    /// </summary>
    public DirectoryDeletionResult Delete(
        Action<DirectoryDeletionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var target in targets)
        {
            DirectoryTreeDeleter.EnsureDeletable(target.Path, target.AllowedRoot);
        }

        var blocked = new HashSet<string>(GamePathValidator.PathComparer);
        var removedFiles = 0;
        var removedBytes = 0L;
        progress?.Invoke(new DirectoryDeletionProgress(0, TotalEntries));
        for (var index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            try
            {
                EnsureParentsAreNotLinks(entry);
                var attributes = File.GetAttributes(entry.Path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(entry.Path, recursive: false);
                }
                else
                {
                    var isLink = (attributes & FileAttributes.ReparsePoint) != 0;
                    var length = isLink ? 0 : new FileInfo(entry.Path).Length;
                    if (!isLink && (attributes & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(entry.Path, attributes & ~FileAttributes.ReadOnly);
                    }

                    File.Delete(entry.Path);
                    if (!isLink)
                    {
                        removedFiles++;
                        removedBytes += length;
                    }
                }
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                if (EntryStillListed(entry.Path))
                {
                    // Xigncode:{GUID} 列得出来却被 Win32 当作数据流，GetAttributes 会报不存在。
                    blocked.Add(entry.Path);
                }

                // 扫描后已经消失的项目视为已处理；不是本次实际删除，因而不计入实测文件数。
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException)
            {
                blocked.Add(entry.Path);
            }

            progress?.Invoke(new DirectoryDeletionProgress(index + 1, TotalEntries));
        }

        return new DirectoryDeletionResult(removedFiles, removedBytes, [.. blocked]);
    }

    private static void EnsureParentsAreNotLinks(DirectoryDeletionEntry entry)
    {
        var parent = Path.GetDirectoryName(entry.Path);
        while (parent is not null && DirectoryTreeDeleter.IsUnder(parent, entry.Root))
        {
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("A deletion-plan ancestor became a reparse point.");
            }

            parent = Path.GetDirectoryName(parent);
        }
    }

    private static bool EntryStillListed(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is null || !Directory.Exists(parent))
        {
            return false;
        }

        try
        {
            foreach (var candidate in Directory.EnumerateFileSystemEntries(parent))
            {
                if (string.Equals(candidate, path, GamePathValidator.PathComparison))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}

/// <summary>计划中的一个条目与它所属的删除根；不缓存删除时需要重新检查的属性。</summary>
internal readonly record struct DirectoryDeletionEntry(string Path, string Root);
