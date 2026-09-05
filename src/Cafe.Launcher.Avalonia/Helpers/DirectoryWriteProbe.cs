using System;
using System.IO;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 目录可写性探测：以「创建即删的探针文件」验证对目标位置的真实写权限。
/// 覆盖两种形态——目录已存在时直接探测；目录不存在时向上定位最近的已存在
/// 祖先目录探测（在该处创建目录链与后续写入需要同一份写权限）。
/// </summary>
public static class DirectoryWriteProbe
{
    private const string ProbeFileName = ".launcher-write-probe.tmp";

    /// <summary>探测 directory 自身可写；目录不存在或探测失败返回 false。</summary>
    public static bool CanWrite(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }

            return TryCreateProbeFile(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 探测 targetPath 位置未来能否被创建并写入：目标已存在时探测其自身，
    /// 否则向上定位最近的已存在祖先目录并探测该处。祖先链上存在同名普通
    /// 文件阻断目录创建时直接判定不可写。
    /// </summary>
    public static bool CanCreate(string targetPath)
    {
        try
        {
            if (Directory.Exists(targetPath))
            {
                return TryCreateProbeFile(targetPath);
            }

            var ancestor = GetNearestExistingAncestorDirectory(targetPath);
            return ancestor is not null && TryCreateProbeFile(ancestor);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? GetNearestExistingAncestorDirectory(string targetPath)
    {
        var current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(targetPath));
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current))
            {
                return current;
            }

            if (File.Exists(current))
            {
                return null;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private static bool TryCreateProbeFile(string directory)
    {
        try
        {
            using var probe = new FileStream(
                Path.Combine(directory, ProbeFileName),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
