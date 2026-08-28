using System;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>Defines shared durations for launcher motion and overlay transitions (ADR-016 ladder).</summary>
public static class MotionTokens
{
    /// <summary>Gets the shortest duration used for immediate feedback transitions.</summary>
    public static readonly TimeSpan FasterDuration = TimeSpan.FromMilliseconds(83);

    /// <summary>Gets the duration used for quick control and content transitions.</summary>
    public static readonly TimeSpan FastDuration = TimeSpan.FromMilliseconds(167);

    /// <summary>Gets the default duration used for standard motion such as dialog surfaces.</summary>
    public static readonly TimeSpan NormalDuration = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets the cap for larger continuous spatial changes, such as the setup wizard step page transition.</summary>
    public static readonly TimeSpan SpatialDuration = TimeSpan.FromMilliseconds(333);
}
