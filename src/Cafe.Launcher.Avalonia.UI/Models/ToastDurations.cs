using System;

namespace Cafe.Launcher.Avalonia.Models;

/// <summary>
/// The display durations a toast can be given. Callers pick a tier, never a number of
/// milliseconds; the ladder the tiers resolve to lives in <see cref="ToastDurations"/>.
/// </summary>
/// <remarks>
/// The names are positional on purpose: "Brief" and "Extended" avoid the primitive type names
/// the analyzer rejects on enum members, and a middle tier called "standard" would read as the
/// default when the common toasts are the brief ones.
/// </remarks>
public enum ToastDuration
{
    /// <summary>Shortest tier. Default for informational and success toasts.</summary>
    Brief,

    /// <summary>Middle tier. Default for warning toasts.</summary>
    Medium,

    /// <summary>Longest tier. Default for error toasts.</summary>
    Extended
}

/// <summary>
/// The fixed ladder of toast display durations, and the tier each severity defaults to.
/// This is developer-side vocabulary: not a user setting, and not a value callers can vary —
/// a toast disappears after one of the tiers in <see cref="ToastDuration"/>.
/// </summary>
public static class ToastDurations
{
    private static readonly TimeSpan BriefDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MediumDuration = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ExtendedDuration = TimeSpan.FromSeconds(8);

    /// <summary>Gets how long a toast of <paramref name="duration"/> stays on screen.</summary>
    public static TimeSpan Resolve(ToastDuration duration) => duration switch
    {
        ToastDuration.Medium => MediumDuration,
        ToastDuration.Extended => ExtendedDuration,
        _ => BriefDuration
    };

    /// <summary>Gets the tier a severity defaults to when the caller does not pick one.</summary>
    public static ToastDuration ForSeverity(ToastSeverity severity) => severity switch
    {
        ToastSeverity.Warning => ToastDuration.Medium,
        ToastSeverity.Error => ToastDuration.Extended,
        _ => ToastDuration.Brief
    };
}
