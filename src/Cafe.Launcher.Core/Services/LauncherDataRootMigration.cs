using System;
using System.IO;
using System.Linq;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 一次性把旧版 Linux 数据根并入新的小写目录（ADR-049）。
/// </summary>
/// <remarks>
/// <para>
/// 旧布局使用 Windows 风格的 <c>$XDG_DATA_HOME/Cafe Launcher</c>；新布局统一为
/// <c>$XDG_DATA_HOME/cafe-launcher</c>。迁移把旧根里的条目逐项移入新根，必须幂等、
/// 不得覆盖新位置已有的文件；任何单项移动失败都保留原文件，旧目录因此可能留下未迁移
/// 内容而不是丢数据。
/// </para>
/// <para>
/// 运行期套接字（<c>cl-signal-*.sock</c>）不迁移：它们绑定的是旧进程的路径，移动一个
/// 已绑定的套接字文件只会留下无法清理的残骸，还可能让新进程在同一个文件名上绑定失败。
/// 旧目录若只剩这类文件就保留，下一次启动会在新数据根重新创建。
/// </para>
/// </remarks>
internal static class LauncherDataRootMigration
{
    internal static void MigrateLegacyRoot(LauncherDataRoot dataRoot, string legacyRoot)
    {
        string legacyPath;
        try
        {
            legacyPath = Path.GetFullPath(legacyRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (string.Equals(legacyPath, dataRoot.Root, StringComparison.Ordinal)
            || !Directory.Exists(legacyPath))
        {
            return;
        }

        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(legacyPath);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (IsRuntimeSocket(Path.GetFileName(entry)))
            {
                continue;
            }

            MoveEntry(entry, dataRoot.Root);
        }

        TryDeleteEmptyDirectory(legacyPath);
    }

    private static bool IsRuntimeSocket(string name) =>
        name.StartsWith("cl-signal-", StringComparison.Ordinal)
        && name.EndsWith(".sock", StringComparison.Ordinal);

    private static void MoveEntry(string source, string destinationDirectory)
    {
        var destination = Path.Combine(destinationDirectory, Path.GetFileName(source));
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            // 新位置已有同名内容时保留新内容；旧副本留在原处，不覆盖、也不删除。
            return;
        }

        try
        {
            Directory.CreateDirectory(destinationDirectory);
            if (Directory.Exists(source))
            {
                MoveDirectory(source, destination);
            }
            else
            {
                MoveFile(source, destination);
            }
        }
        catch (IOException)
        {
            // 目标不可写或跨设备复制中断：保留源文件，下一次启动会再次尝试。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void MoveDirectory(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException)
        {
            CopyDirectory(source, destination);
            Directory.Delete(source, recursive: true);
        }
    }

    private static void MoveFile(string source, string destination)
    {
        try
        {
            File.Move(source, destination);
        }
        catch (IOException)
        {
            File.Copy(source, destination);
            File.Delete(source);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
