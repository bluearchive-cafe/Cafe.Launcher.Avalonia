using System;
using Avalonia.Controls;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 把只应执行一次的启动处理器挂到 <see cref="Window.Opened"/> 上，并在首次触发后自行摘除。
/// Avalonia 的 <c>Window</c> 在每次 Hide 之后的 Show 都会重新触发 Opened
/// （<c>ShowCore</c> 不区分「首次打开」与「隐藏后恢复」，Hide 会复位 <c>_shown</c>），
/// 而托盘恢复走的正是 Show：处理器若一直挂着，「--launch-game 首实例流程」会在每次
/// 从托盘恢复窗口时重跑——重新执行启动命令、再次弹出「启动器已最小化到托盘」提示并把
/// 窗口再次最小化。与 <c>MainWindow.PlayShellEntranceOnce</c> 的自摘除是同一契约。
/// </summary>
internal static class WindowOpenedOnce
{
    /// <summary>
    /// Subscribes <paramref name="handler"/> to the window's first Opened and detaches it
    /// before the handler body runs, so even a handler that itself shows or hides the
    /// window cannot re-enter.
    /// </summary>
    public static void Subscribe(Window window, EventHandler handler)
    {
        void OnOpened(object? sender, EventArgs eventArgs)
        {
            window.Opened -= OnOpened;
            handler(sender, eventArgs);
        }

        window.Opened += OnOpened;
    }
}
