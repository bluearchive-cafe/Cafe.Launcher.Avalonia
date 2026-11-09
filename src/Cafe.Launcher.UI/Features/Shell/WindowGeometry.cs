using System;
using Avalonia;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Features.Shell;

/// <summary>
/// 普通状态下的窗口几何值快照：在 UI 线程取好，之后可以安全地在线程池上写回设置，
/// 不必回头读窗口属性。
/// </summary>
internal readonly record struct WindowGeometry(PixelPoint Position, double Width, double Height)
{
    /// <summary>最大化/最小化时的尺寸不是用户设定的窗口几何，只在普通状态采集。</summary>
    public static WindowGeometry? TryCapture(
        bool isNormalState,
        PixelPoint position,
        double width,
        double height) =>
        isNormalState ? new WindowGeometry(position, width, height) : null;

    public void ApplyTo(LauncherSettings settings)
    {
        if (!settings.RememberWindowPositionAndSize)
        {
            return;
        }

        settings.WindowPositionX = Position.X;
        settings.WindowPositionY = Position.Y;
        if (double.IsFinite(Width) && Width > 0)
        {
            settings.WindowWidth = Width;
        }

        if (double.IsFinite(Height) && Height > 0)
        {
            settings.WindowHeight = Height;
        }
    }
}

/// <summary>恢复目标：只包含需要改写的项；未保存或非法的维度保持 <c>null</c>。</summary>
internal readonly record struct WindowGeometryRestore(double? Width, double? Height, PixelPoint? Position);

/// <summary>窗口几何恢复的纯计算：按最小尺寸钳制、丢弃非法值，不触达窗口。</summary>
internal static class WindowGeometryPolicy
{
    public static WindowGeometryRestore Restore(
        LauncherSettings settings,
        double minWidth,
        double minHeight)
    {
        if (!settings.RememberWindowPositionAndSize)
        {
            return default;
        }

        double? width = settings.WindowWidth is double savedWidth
            && double.IsFinite(savedWidth)
            && savedWidth > 0
                ? Math.Max(minWidth, savedWidth)
                : null;
        double? height = settings.WindowHeight is double savedHeight
            && double.IsFinite(savedHeight)
            && savedHeight > 0
                ? Math.Max(minHeight, savedHeight)
                : null;
        PixelPoint? position = settings.WindowPositionX is int x && settings.WindowPositionY is int y
            ? new PixelPoint(x, y)
            : null;

        return new WindowGeometryRestore(width, height, position);
    }
}
