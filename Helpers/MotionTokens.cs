using System;

namespace Cafe.Launcher.Avalonia.Helpers;

public static class MotionTokens
{
    public static readonly TimeSpan FasterDuration = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan FastDuration = TimeSpan.FromMilliseconds(167);
    public static readonly TimeSpan ContentDuration = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan NormalDuration = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan OverlayDelay = TimeSpan.FromMilliseconds(50);
}
