using System;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>Defines shared durations for launcher motion and overlay transitions.</summary>
public static class MotionTokens
{
    /// <summary>Gets the shortest duration used for immediate feedback transitions.</summary>
    public static readonly TimeSpan FasterDuration = TimeSpan.FromMilliseconds(50);

    /// <summary>Gets the duration used for quick control transitions.</summary>
    public static readonly TimeSpan FastDuration = TimeSpan.FromMilliseconds(167);

    /// <summary>Gets the duration used when content enters or leaves the layout.</summary>
    public static readonly TimeSpan ContentDuration = TimeSpan.FromMilliseconds(200);

    /// <summary>Gets the default duration used for standard motion.</summary>
    public static readonly TimeSpan NormalDuration = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets the delay applied before an overlay transition begins.</summary>
    public static readonly TimeSpan OverlayDelay = TimeSpan.FromMilliseconds(50);
}
