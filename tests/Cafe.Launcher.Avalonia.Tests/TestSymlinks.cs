using System;
using System.Diagnostics;
using System.IO;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 创建一个指向 <paramref name="target"/> 的目录 reparse point，或在平台/权限不允许时可见地跳过。
/// </summary>
/// <remarks>
/// 符号链接在 Windows 上需要开发者模式或管理员权限；拿不到时退回 junction——它同样是
/// reparse point，而被测代码（路径校验、递归删除）关心的正是 reparse point 这个属性。
/// 两者都不可用时 SKIP 而不是红：那不是被测行为的问题。
/// </remarks>
internal static class TestSymlinks
{
    public static void CreateDirectorySymbolicLinkOrSkip(string path, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
            return;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            if (TryCreateJunction(path, target))
            {
                return;
            }

            Assert.SkipWhen(
                true,
                $"Directory symbolic links are unavailable: {exception.Message}");
        }
    }

    private static bool TryCreateJunction(string path, string target)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(target))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(10_000);
            return process.ExitCode == 0 && Directory.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
