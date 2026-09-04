using System;
using Avalonia;
using Avalonia.Controls;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Window-backed <see cref="IWindowMetricsService"/>. The active window attaches itself
/// on construction (and detaches on close) so the singleton never roots a dead window.
/// Metrics are snapshotted on the UI thread (attach + size/scaling changes) so that
/// background threads can query them without touching Avalonia properties.
/// Without an attached window — e.g. in headless tests — the default target applies.
/// </summary>
public sealed class WindowMetricsService : IWindowMetricsService
{
    private static readonly PixelSize FallbackClientSize = new(1920, 1080);

    private readonly object attachLock = new();
    private TopLevel? owner;
    private PixelSize physicalClientSize = FallbackClientSize;

    /// <inheritdoc />
    public event Action? PhysicalSizeChanged;

    /// <summary>Registers the window whose client size backs metric queries.</summary>
    public void Attach(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        lock (attachLock)
        {
            if (ReferenceEquals(owner, topLevel))
            {
                return;
            }

            if (owner is not null)
            {
                owner.PropertyChanged -= OnOwnerPropertyChanged;
                owner.ScalingChanged -= OnOwnerScalingChanged;
            }

            owner = topLevel;
            topLevel.PropertyChanged += OnOwnerPropertyChanged;
            topLevel.ScalingChanged += OnOwnerScalingChanged;
            physicalClientSize = Measure(topLevel);
        }
    }

    /// <summary>Removes the registration when the window closes.</summary>
    public void Detach(TopLevel topLevel)
    {
        lock (attachLock)
        {
            if (!ReferenceEquals(owner, topLevel))
            {
                return;
            }

            owner.PropertyChanged -= OnOwnerPropertyChanged;
            owner.ScalingChanged -= OnOwnerScalingChanged;
            owner = null;
            physicalClientSize = FallbackClientSize;
        }
    }

    public PixelSize GetPhysicalClientSize()
    {
        lock (attachLock)
        {
            return physicalClientSize;
        }
    }

    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TopLevel.ClientSizeProperty)
        {
            return;
        }

        RefreshSnapshot(sender);
    }

    private void OnOwnerScalingChanged(object? sender, EventArgs e) => RefreshSnapshot(sender);

    private void RefreshSnapshot(object? sender)
    {
        bool sizeChanged;
        lock (attachLock)
        {
            if (sender is not TopLevel topLevel || !ReferenceEquals(topLevel, owner))
            {
                return;
            }

            var previous = physicalClientSize;
            physicalClientSize = Measure(topLevel);
            sizeChanged = previous != physicalClientSize;
        }

        // 订阅方（壁纸按需重解码）依赖此通知；锁外触发避免回调重入锁。
        if (sizeChanged)
        {
            PhysicalSizeChanged?.Invoke();
        }
    }

    private static PixelSize Measure(TopLevel topLevel)
    {
        var scaling = topLevel.RenderScaling;
        var client = topLevel.ClientSize;
        if (client.Width <= 0 || client.Height <= 0)
        {
            // 布局尚未发生（窗口构造后、Show 前）：返回默认目标而非 1×1 坏快照。
            return FallbackClientSize;
        }

        return new PixelSize(
            Math.Max(1, (int)Math.Ceiling(client.Width * scaling)),
            Math.Max(1, (int)Math.Ceiling(client.Height * scaling)));
    }
}
