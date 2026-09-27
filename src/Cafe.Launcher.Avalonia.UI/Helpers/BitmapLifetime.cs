using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 两条位图流水线共用的所有权原语（D12）：取消时的就地释放、以及「绑定引用换掉之后
/// 才释放旧位图」的延迟释放。两条流水线的输入、解码策略与陈旧判定各不相同（刻意不合并），
/// 只有这两条契约相同——此前壁纸侧各有一份具名实现，横幅侧各有一份内联副本。
/// </summary>
public static class BitmapLifetime
{
    /// <summary>
    /// 取消已请求时先释放 <paramref name="image"/> 再抛出，避免取消路径漏掉解码产物。
    /// </summary>
    public static void ThrowIfCancellationRequested(IImage? image, CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            return;
        }

        (image as IDisposable)?.Dispose();
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// 释放已从绑定上摘除的旧位图。挂到后台调度优先级：调用方刚刚替换过绑定引用，
    /// 让当前这一帧有机会先跑完，免得渲染帧读到已释放的实现（ObjectDisposedException 崩溃面）。
    /// </summary>
    public static void ReleaseAfterBindingsSettle(IImage? image)
    {
        if (image is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () => (image as IDisposable)?.Dispose(),
            DispatcherPriority.Background);
    }
}
