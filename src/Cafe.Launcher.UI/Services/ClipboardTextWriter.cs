using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace Cafe.Launcher.UI.Services;

/// <summary>
/// 写系统剪贴板。剪贴板属于窗口（TopLevel）而不属于视图模型，因此这里只提供
/// 一个进程级的静态入口，由 <see cref="PresentationEnvironment"/> 作为平台效果暴露给
/// 视图模型；无桌面宿主或平台不支持剪贴板时返回 false，调用方按失败提示。
/// </summary>
internal static class ClipboardTextWriter
{
    public static async Task<bool> TryWriteAsync(string text)
    {
        var clipboard = ResolveClipboard();
        if (clipboard is null)
        {
            return false;
        }

        try
        {
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception)
        {
            // 剪贴板被其它进程占用或平台拒绝写入：由调用方提示，不在此抛出。
            return false;
        }
    }

    private static IClipboard? ResolveClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }

        return null;
    }
}
