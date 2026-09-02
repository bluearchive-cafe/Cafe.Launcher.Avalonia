using System;
using System.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// 统一的"用系统文件管理器打开本地目录"实现。此前 WindowChromeViewModel、
/// MainWindow 与 GameShortcutService 各自持有一份几乎相同的 Process.Start
/// 逻辑（仅 GameShortcutService 在 Windows 上显式使用 explorer.exe），现在全部
/// 经由本类型，平台差异只在一处维护。打开 URL 仍走 ExternalLinkService 的
/// scheme 白名单，二者职责不同。
/// </summary>
public static class ShellFolderOpener
{
    /// <summary>
    /// Opens the directory in the OS file manager. Windows pins explorer.exe so the
    /// folder always opens in Explorer regardless of default-handler registration;
    /// other platforms hand the path to the shell. Returns whether the launched
    /// process reported success; failures of Process.Start propagate to the caller.
    /// </summary>
    public static bool OpenInFileManager(string directory)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { directory },
                UseShellExecute = true
            }
            : new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            };

        return Process.Start(startInfo) is not null;
    }
}
